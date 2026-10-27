using InventorGostSpec.AddIn.Infrastructure;
using InventorGostSpec.AddIn.InventorApi;
using InventorGostSpec.Core.Model;
using InventorGostSpec.Core.Parsing;
using InventorGostSpec.Core.Sections;
using InventorGostSpec.Core.Storage;
using InventorGostSpec.UI;
using InventorGostSpec.UI.ViewModels;
using InventorGostSpec.UI.Views;

namespace InventorGostSpec.AddIn.Commands;

/// <summary>
/// Автоматическое назначение разделов компонентам первого уровня сборки.
/// Компоненты, у которых раздел уже задан, пропускаются.
/// </summary>
internal sealed class AutoAssignCommand : AddInCommand
{
    public const string Id = "InventorGostSpec.AutoAssign";

    public override string InternalName => Id;

    public override string DisplayName => "Авторазделы";

    public override string Description =>
        "Назначить разделы компонентам без раздела: Content Center — стандартные изделия, сборки — сборочные единицы, детали — детали. " +
        "Для стандартных изделий заполняются поля сортировки, для деталей с чертежом — формат.";

    public override string IconName => "auto-assign";

    public override void Execute(AddInContext context)
    {
        var assembly = context.RequireAssembly();
        if (assembly is null)
        {
            return;
        }

        var items = BomAccess.GetFirstLevel(assembly)
            .GroupBy(i => i.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        // Защита: команда меняет файлы многих компонентов — нужно явное подтверждение галочкой.
        var toAssign = items.Count(i => i.Target.Get(PropertyNames.Section).Length == 0);
        if (toAssign == 0)
        {
            context.Messages.Info($"У всех компонентов первого уровня ({items.Count}) раздел уже задан — назначать нечего.");
            return;
        }

        var confirmation = new ConfirmationViewModel(
            "Авторазделы",
            $"Разделы будут назначены компонентам без раздела: {toAssign} из {items.Count}.\n\n" +
            "• Content Center — «Стандартные изделия» (и поля сортировки из наименования), сборки — «Сборочные единицы», " +
            "детали — «Детали», виртуальные — «Прочие изделия».\n" +
            "• Деталям без формата — формат из чертежа, а если чертежа нет — «БЧ».\n" +
            "• Компоненты с уже заданным разделом не меняются.\n\n" +
            "Свойства записываются в файлы компонентов и действуют во всех сборках. После выполнения сохраните сборку.",
            "Понимаю, что свойства будут записаны в файлы компонентов",
            "Выполнить");
        if (WindowOwner.ShowDialog(new ConfirmationWindow(confirmation), context.MainWindowHandle) != true)
        {
            return;
        }

        var assigned = 0;
        var skipped = 0;
        var failed = new List<string>();

        // Поиск чертежей — один на команду: файлы проекта перечисляются один раз.
        var noDrawing = context.Settings.Load().NoDrawingFormat();
        var drawings = new Lazy<DrawingSearch>(() => new DrawingSearch(
            context.Application,
            items.Select(i => System.IO.Path.GetDirectoryName(i.Target.FullFileName ?? "") ?? "").Distinct()));
        using (var transaction = new InventorTransaction(context.Application, (Document)assembly, "Авторазделы"))
        {
            foreach (var item in items)
            {
                var target = item.Target;
                if (target.Get(PropertyNames.Section).Length > 0)
                {
                    skipped++;
                    continue;
                }

                try
                {
                    Assign(context, target, drawings, noDrawing);
                    assigned++;
                }
                catch (Exception ex)
                {
                    Log.Error(Id + " " + target.DisplayName, ex);
                    failed.Add(target.DisplayName);
                }
            }

            transaction.Commit();
        }

        var message = $"Назначено разделов: {assigned}\nУже были заданы (пропущено): {skipped}";
        if (failed.Count > 0)
        {
            message += "\n\nНе удалось записать (файл только для чтения?):\n" + string.Join("\n", failed);
        }

        if (assigned > 0)
        {
            message += "\n\nСохраните сборку, чтобы свойства записались в файлы компонентов.";
        }

        context.Messages.Info(message);
    }

    /// <param name="noDrawing">Формат детали без чертежа по настройке: «БЧ» или пусто (тогда не записывается).</param>
    private static void Assign(AddInContext context, ComponentTarget target, Lazy<DrawingSearch> drawings, string noDrawing)
    {
        var section = SectionResolver.GetDefault(target.Kind, target.IsContentCenter);
        target.Set(PropertyNames.Section, section);

        if (section == SectionIds.StandardProducts)
        {
            // У Content Center наименование бывает и в Description, и в Part Number.
            var name = target.Description.Length > 0 ? target.Description : target.PartNumber;
            var parsed = StandardDesignationParser.Parse(name);
            SetIfEmpty(target, PropertyNames.SortName, parsed.Name);
            SetIfEmpty(target, PropertyNames.StandardNumber, parsed.StandardNumber);
            SetIfEmpty(target, PropertyNames.Parameters, parsed.Parameters);
        }

        // Формат берётся из чертежа только у деталей (не Content Center), без чертежа — «БЧ» или пусто (настройка). У сборочной единицы
        // в спецификации указывается формат её спецификации (А4) — его подставляет сама спецификация.
        if (target.Kind == ComponentKind.Part && !target.IsContentCenter && target.FullFileName is not null &&
            target.Get(PropertyNames.Format).Length == 0)
        {
            var drawing = drawings.Value.Find(target.FullFileName, target.PartNumber, isAssembly: false);
            SetIfEmpty(target, PropertyNames.Format, drawing is null ? noDrawing : DrawingAccess.ReadFormat(context.Application, drawing));
        }
    }

    private static void SetIfEmpty(ComponentTarget target, string propertyName, string value)
    {
        if (value.Length > 0 && target.Get(propertyName).Length == 0)
        {
            target.Set(propertyName, value);
        }
    }
}
