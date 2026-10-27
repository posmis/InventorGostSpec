using InventorGostSpec.AddIn.Infrastructure;
using InventorGostSpec.AddIn.InventorApi;
using InventorGostSpec.Core.Model;
using InventorGostSpec.UI;
using InventorGostSpec.UI.ViewModels;
using InventorGostSpec.UI.Views;

namespace InventorGostSpec.AddIn.Commands;

/// <summary>
/// Ручное назначение раздела и полей спецификации выбранным компонентам сборки
/// (или самой открытой детали/сборке, если ничего не выбрано).
/// </summary>
internal sealed class AssignSectionCommand : AddInCommand
{
    public const string Id = "InventorGostSpec.AssignSection";

    public override string InternalName => Id;

    public override string DisplayName => "Раздел";

    public override string Description => "Назначить раздел спецификации и поля для сортировки выбранным компонентам";

    public override string IconName => "assign-section";

    public override void Execute(AddInContext context)
    {
        var document = context.Application.ActiveDocument;
        if (document is null)
        {
            context.Messages.Warning("Откройте деталь или сборку.");
            return;
        }

        var targets = CollectTargets(context, document);
        if (targets.Count == 0)
        {
            return;
        }

        var settings = context.Settings.Load();
        var catalog = SpecificationContext.Create(settings, new Specification()).Catalog;
        var initial = ComponentTarget.SpecProperties.ToDictionary(
            name => name,
            name => CommonValue(targets.Select(t => t.Get(name))));
        var description = targets.Count == 1
            ? $"Компонент: {targets[0].DisplayName}"
            : $"Выбрано компонентов: {targets.Count}";

        var autoSections = targets.Select(t => SectionResolver.GetDefault(t.Kind, t.IsContentCenter)).Distinct().ToList();
        var viewModel = new AssignSectionViewModel(
            catalog.Sections, initial, description, autoSections.FirstOrDefault(), autoSectionVaries: autoSections.Count > 1);
        if (WindowOwner.ShowDialog(new AssignSectionWindow(viewModel), context.MainWindowHandle) != true)
        {
            return;
        }

        var changes = viewModel.GetChanges();
        if (changes.Count == 0)
        {
            return;
        }

        var failed = new List<string>();
        using (var transaction = new InventorTransaction(context.Application, document, "Раздел спецификации"))
        {
            foreach (var target in targets)
            {
                try
                {
                    foreach (var (name, value) in changes)
                    {
                        target.Set(name, value);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(Id + " " + target.DisplayName, ex);
                    failed.Add(target.DisplayName);
                }
            }

            transaction.Commit();
        }

        if (failed.Count > 0)
        {
            context.Messages.Warning(
                "Не удалось записать свойства (файл только для чтения?):\n" + string.Join("\n", failed));
        }
    }

    private static List<ComponentTarget> CollectTargets(AddInContext context, Document document)
    {
        var targets = new List<ComponentTarget>();
        if (document is AssemblyDocument assembly)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var folder = BomAccess.AssemblyFolder(assembly);
            foreach (var selected in assembly.SelectSet)
            {
                if (selected is ComponentOccurrence occurrence &&
                    ComponentTarget.FromDefinition(occurrence.Definition) is { } target &&
                    seen.Add(target.GetKey(folder)))
                {
                    targets.Add(target);
                }
            }

            if (targets.Count == 0 &&
                context.Messages.Confirm("Компоненты не выбраны. Назначить раздел самой сборке?"))
            {
                targets.Add(ComponentTarget.FromDocument(document));
            }
        }
        else if (document is PartDocument)
        {
            targets.Add(ComponentTarget.FromDocument(document));
        }
        else
        {
            context.Messages.Warning("Откройте деталь или сборку.");
        }

        return targets;
    }

    /// <summary>Общее значение или <c>null</c>, если значения различаются.</summary>
    private static string? CommonValue(IEnumerable<string> values)
    {
        var distinct = values.Distinct(StringComparer.Ordinal).Take(2).ToList();
        return distinct.Count == 1 ? distinct[0] : null;
    }
}
