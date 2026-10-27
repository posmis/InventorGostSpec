using InventorGostSpec.AddIn.Infrastructure;
using InventorGostSpec.AddIn.InventorApi;
using InventorGostSpec.Core.Parsing;
using InventorGostSpec.Core.Sections;
using InventorGostSpec.Core.Storage;
using InventorGostSpec.UI;
using InventorGostSpec.UI.ViewModels;
using InventorGostSpec.UI.Views;

namespace InventorGostSpec.AddIn.Commands;

/// <summary>
/// Приведение наименований (Description) стандартных изделий сборки к виду для спецификации
/// по имени файла: «Болт М12х60 ГОСТ 7798-70». Со спецификацией не связана.
/// </summary>
internal sealed class StandardNamesCommand : AddInCommand
{
    public const string Id = "InventorGostSpec.StandardNames";

    public override string InternalName => Id;

    public override string DisplayName => "Наименования\nстандартных";

    public override string Description =>
        "Записать наименования стандартных изделий сборки по именам их файлов в виде «Болт М12х60 ГОСТ 7798-70». " +
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
                    Rename(target, item.Proposed);
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

    /// <summary>
    /// Записывает наименование и поля сортировки: они выводятся из наименования
    /// и иначе остались бы от старого.
    /// </summary>
    private static void Rename(ComponentTarget target, string name)
    {
        PropertyAccess.Set(target.PropertySets, PropertyAccess.DesignTrackingSet, PropertyAccess.Description, name);

        var parts = StandardDesignationParser.Parse(name);
        target.Set(PropertyNames.SortName, parts.Name);
        target.Set(PropertyNames.StandardNumber, parts.StandardNumber);
        target.Set(PropertyNames.Parameters, parts.Parameters);
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
