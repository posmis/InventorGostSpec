using InventorGostSpec.AddIn.Infrastructure;
using InventorGostSpec.AddIn.InventorApi;
using InventorGostSpec.Core.Model;
using InventorGostSpec.Core.Parsing;
using InventorGostSpec.Core.Sections;
using InventorGostSpec.Core.Storage;

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

        var assigned = 0;
        var skipped = 0;
        var failed = new List<string>();

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
                    Assign(context, target);
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

    private static void Assign(AddInContext context, ComponentTarget target)
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

        // Формат берётся из чертежа только у деталей. У сборочной единицы в спецификации указывается
        // формат её спецификации (А4) — его подставляет сама спецификация, а не формат сборочного чертежа.
        if (target.Kind == ComponentKind.Part && target.FullFileName is not null && target.Get(PropertyNames.Format).Length == 0 &&
            DrawingAccess.FindDrawing(target.FullFileName) is { } drawing)
        {
            SetIfEmpty(target, PropertyNames.Format, DrawingAccess.ReadFormat(context.Application, drawing));
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
