using System.Text;
using Inventor;
using InventorGostSpec.AddIn.Infrastructure;
using InventorGostSpec.AddIn.InventorApi;
using InventorGostSpec.Core.Model;
using InventorGostSpec.Core.Update;

namespace InventorGostSpec.AddIn.Commands;

/// <summary>
/// Формирование или обновление спецификации по составу сборки.
/// Спецификация хранится в файле сборки и меняется только этой командой.
/// </summary>
internal sealed class UpdateSpecificationCommand : AddInCommand
{
    public const string Id = "InventorGostSpec.UpdateSpecification";

    public override string InternalName => Id;

    public override string DisplayName => "Обновить\nспецификацию";

    public override string Description => "Сформировать или обновить спецификацию по составу сборки. Ручные правки сохраняются.";

    public override void Execute(AddInContext context)
    {
        var assembly = context.RequireAssembly();
        if (assembly is null)
        {
            return;
        }

        var document = (Document)assembly;
        var specification = SpecificationStore.Load(document) ?? new Specification();
        var specContext = SpecificationContext.Create(context.Settings.Load(), specification);
        var components = AssemblyReader.Read(context.Application, assembly);

        var result = SpecificationUpdater.Update(specification, components, specContext, DateTime.UtcNow);

        using (var transaction = new InventorTransaction(context.Application, document, "Обновить спецификацию"))
        {
            SpecificationStore.Save(document, specification);
            transaction.Commit();
        }

        context.Messages.Info(Describe(result, specification, specContext));
    }

    private static string Describe(UpdateResult result, Specification specification, SpecificationContext context)
    {
        var text = new StringBuilder();
        if (result.IsInitialBuild)
        {
            text.AppendLine($"Спецификация сформирована: {specification.Rows.Count} строк.");
        }
        else if (!result.HasChanges)
        {
            text.AppendLine("Изменений в составе сборки нет.");
        }
        else
        {
            text.AppendLine("Спецификация обновлена.");
            text.AppendLine($"Новых: {result.Added.Count}");
            text.AppendLine($"Изменённых: {result.Changed.Count}");
            text.AppendLine($"Удалённых из сборки (помечены): {result.Removed.Count}");
        }

        if (result.Positions.ExhaustedSectionIds.Count > 0)
        {
            var sections = result.Positions.ExhaustedSectionIds.Select(id => context.Catalog.GetOrCreate(id).Name);
            text.AppendLine();
            text.AppendLine($"Не хватило резервных номеров позиций в разделах: {string.Join(", ", sections)}.");
            text.AppendLine("Строки остались без номера. Перенумеруйте спецификацию или увеличьте резерв.");
        }

        text.AppendLine();
        text.Append("Сохраните сборку, чтобы спецификация записалась в файл.");
        return text.ToString();
    }
}
