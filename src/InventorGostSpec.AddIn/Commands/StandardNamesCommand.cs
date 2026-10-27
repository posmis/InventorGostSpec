using InventorGostSpec.AddIn.Infrastructure;
using InventorGostSpec.AddIn.InventorApi;
using InventorGostSpec.Core.Sections;
using InventorGostSpec.Core.Storage;
using InventorGostSpec.UI;
using InventorGostSpec.UI.ViewModels;
using InventorGostSpec.UI.Views;

namespace InventorGostSpec.AddIn.Commands;

/// <summary>
/// Запись имени файла стандартного изделия в его наименование (Description) — как есть, без перестановок.
/// Со спецификацией не связана.
/// </summary>
internal sealed class StandardNamesCommand : AddInCommand
{
    public const string Id = "InventorGostSpec.StandardNames";

    public override string InternalName => Id;

    public override string DisplayName => "Наименования\nстандартных";

    public override string Description =>
        "Записать в наименование (Description) стандартных изделий сборки имя их файла. " +
        "Перед записью показывается, что было и что станет.";

    public override string IconName => "standard-names";

    public override void Execute(AddInContext context)
    {
        var assembly = context.RequireAssembly();
        if (assembly is null)
        {
            return;
        }

        var document = (Document)assembly;
        var preselected = SelectedFiles(assembly);
        var targets = new Dictionary<string, ComponentTarget>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<StandardNameCandidate>();

        // Все уровни сборки: стандартные изделия подсборок тоже попадают в их спецификации.
        foreach (Document referenced in document.AllReferencedDocuments)
        {
            if (referenced is not PartDocument || targets.ContainsKey(referenced.FullFileName))
            {
                continue;
            }

            var target = ComponentTarget.FromDocument(referenced);
            if (!target.IsContentCenter && target.Get(PropertyNames.Section) != SectionIds.StandardProducts)
            {
                continue;
            }

            targets[referenced.FullFileName] = target;
            candidates.Add(new StandardNameCandidate(
                referenced.FullFileName,
                System.IO.Path.GetFileNameWithoutExtension(referenced.FullFileName),
                target.Description,
                preselected.Contains(referenced.FullFileName)));
        }

        if (candidates.Count == 0)
        {
            context.Messages.Info(
                "В сборке нет стандартных изделий: компонентов Content Center или деталей с разделом «Стандартные изделия».");
            return;
        }

        var viewModel = new StandardNamesViewModel(candidates);
        if (WindowOwner.ShowDialog(new StandardNamesWindow(viewModel), context.MainWindowHandle) != true)
        {
            return;
        }

        var changed = 0;
        var failed = new List<string>();
        using (var transaction = new InventorTransaction(context.Application, document, "Наименования стандартных изделий"))
        {
            foreach (var item in viewModel.GetChanges())
            {
                var target = targets[item.Candidate.Key];
                try
                {
                    Rename(target, item.Proposed, viewModel.ClearSortFields);
                    changed++;
                }
                catch (Exception ex)
                {
                    Log.Error(Id + " " + item.Candidate.Key, ex);
                    failed.Add($"{item.FileName}: {ex.Message}");
                }
            }

            transaction.Commit();
        }

        var message = $"Изменено наименований: {changed}";
        if (failed.Count > 0)
        {
            message += "\n\nНе удалось изменить (файл только для чтения?):\n" + string.Join("\n", failed);
        }

        if (changed > 0)
        {
            message += "\n\nСохраните сборку, чтобы изменения записались в файлы изделий. " +
                       "В спецификации новые наименования появятся после «Обновить из сборки».";
        }

        context.Messages.Info(message);
    }

    /// <param name="clearSortFields">
    /// Удалить поля сортировки, заполненные по старому наименованию: спецификация возьмёт их из нового.
    /// </param>
    private static void Rename(ComponentTarget target, string name, bool clearSortFields)
    {
        PropertyAccess.Set(target.PropertySets, PropertyAccess.DesignTrackingSet, PropertyAccess.Description, name);
        if (clearSortFields)
        {
            target.Set(PropertyNames.SortName, "");
            target.Set(PropertyNames.StandardNumber, "");
            target.Set(PropertyNames.Parameters, "");
        }
    }

    private static HashSet<string> SelectedFiles(AssemblyDocument assembly)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var selected in assembly.SelectSet)
        {
            if (selected is ComponentOccurrence { Definition.Document: Document document })
            {
                result.Add(document.FullFileName);
            }
        }

        return result;
    }
}
