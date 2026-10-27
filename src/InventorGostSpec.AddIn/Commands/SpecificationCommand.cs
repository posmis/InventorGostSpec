using InventorGostSpec.AddIn.Infrastructure;
using InventorGostSpec.AddIn.InventorApi;
using InventorGostSpec.Core.Model;
using InventorGostSpec.Core.Update;
using InventorGostSpec.UI.ViewModels;
using InventorGostSpec.UI.Views;

namespace InventorGostSpec.AddIn.Commands;

/// <summary>
/// Окно спецификации сборки. Если спецификации ещё нет, она формируется по составу сборки.
/// Дальше спецификация меняется только по командам пользователя в окне.
/// </summary>
internal sealed class SpecificationCommand : AddInCommand
{
    public const string Id = "InventorGostSpec.Specification";

    public override string InternalName => Id;

    public override string DisplayName => "Спецификация";

    public override string Description => "Открыть спецификацию сборки по ГОСТ 2.106";

    public override string IconName => "specification";

    public override void Execute(AddInContext context)
    {
        var assembly = context.RequireAssembly();
        if (assembly is null)
        {
            return;
        }

        var host = new InventorSpecificationHost(context, assembly);
        var specification = SpecificationStore.Load((Document)assembly);
        var isNew = specification is null;
        if (specification is null)
        {
            // Первое формирование ничего не перезаписывает, поэтому выполняется сразу.
            specification = new Specification();
            var specContext = SpecificationContext.Create(host.LoadSettings(), specification);
            SpecificationUpdater.Update(specification, host.ReadComponents(), specContext, DateTime.UtcNow);
        }

        SpecificationWindow.ShowDialog(host, specification, context.MainWindowHandle, isNew);
    }

    private sealed class InventorSpecificationHost : ISpecificationHost
    {
        private readonly AddInContext _context;
        private readonly AssemblyDocument _assembly;

        public InventorSpecificationHost(AddInContext context, AssemblyDocument assembly)
        {
            _context = context;
            _assembly = assembly;
        }

        public string DocumentName => ((Document)_assembly).DisplayName;

        public SpecSettings LoadSettings() => _context.Settings.Load();

        public void SaveSettings(SpecSettings settings) => _context.Settings.Save(settings);

        public IReadOnlyList<ComponentData> ReadComponents() => AssemblyReader.Read(_context.Application, _assembly);

        public void Save(Specification specification)
        {
            var document = (Document)_assembly;
            using var transaction = new InventorTransaction(_context.Application, document, "Сохранить спецификацию");
            SpecificationStore.Save(document, specification);
            transaction.Commit();
        }

        public string WritePositions(Specification specification)
        {
            using var transaction = new InventorTransaction(_context.Application, (Document)_assembly, "Позиции в BOM");
            var report = PositionWriter.Write(_assembly, specification);
            transaction.Commit();
            return report + "\n\nСохраните сборку, чтобы изменения записались в файл.";
        }

        public IReadOnlyDictionary<string, string?>? ReadComponentProperties(string componentKey)
        {
            var item = FindItem(componentKey);
            return item is null
                ? null
                : ComponentTarget.SpecProperties.ToDictionary(name => name, name => (string?)item.Target.Get(name));
        }

        public ComponentData WriteComponentProperties(string componentKey, IReadOnlyDictionary<string, string> changes)
        {
            var item = FindItem(componentKey) ?? throw new InvalidOperationException("Компонент не найден в сборке.");
            using (var transaction = new InventorTransaction(_context.Application, (Document)_assembly, "Свойства компонента"))
            {
                foreach (var (name, value) in changes)
                {
                    item.Target.Set(name, value);
                }

                transaction.Commit();
            }

            return AssemblyReader.ToComponentData(item.Target, item.Key, AssemblyReader.Quantity(item.Row));
        }

        public string? ReadComponentNote(string componentKey)
        {
            if (FindItem(componentKey) is not { } item)
            {
                return null;
            }

            var propertyName = _context.Settings.Load().NotePropertyName;
            var note = string.IsNullOrWhiteSpace(propertyName) ? "" : item.Target.Get(propertyName);
            return note.Length > 0
                ? note
                : PropertyAccess.Get(item.Target.PropertySets, PropertyAccess.SummarySet, PropertyAccess.Comments);
        }

        public ExportInfo GetExportInfo()
        {
            var document = (Document)_assembly;
            var sets = document.PropertySets;
            string Get(string set, string name) => PropertyAccess.TryGet(sets, set, name);

            var designation = Designations.WithoutAssemblyDrawingCode(Get(PropertyAccess.DesignTrackingSet, PropertyAccess.PartNumber));
            var name = Get(PropertyAccess.DesignTrackingSet, PropertyAccess.Description);
            var title = new TitleBlock
            {
                Designation = designation,
                Name = name.Length > 0 ? name : Get(PropertyAccess.SummarySet, PropertyAccess.Title),
                Developer = Get(PropertyAccess.DesignTrackingSet, PropertyAccess.Designer),
                Checker = Get(PropertyAccess.DesignTrackingSet, PropertyAccess.CheckedBy),
                Approver = Get(PropertyAccess.DesignTrackingSet, PropertyAccess.EngineeringApprovedBy),
                Organization = Get(PropertyAccess.DocumentSummarySet, PropertyAccess.Company),
            };

            var folder = System.IO.Path.GetDirectoryName(document.FullFileName) ?? "";
            var baseName = designation.Length > 0 ? designation : System.IO.Path.GetFileNameWithoutExtension(document.FullFileName);
            var fileName = string.Concat($"{baseName} Спецификация.xlsx".Split(System.IO.Path.GetInvalidFileNameChars()));
            return new ExportInfo(title, System.IO.Path.Combine(folder, fileName));
        }

        public string GetAutoSectionId(string componentKey) =>
            FindItem(componentKey) is { } item
                ? SectionResolver.GetDefault(item.Target.Kind, item.Target.IsContentCenter)
                : "";

        public void ReportError(Exception exception) => Log.Error("SpecificationWindow", exception);

        private BomItem? FindItem(string componentKey) =>
            BomAccess.GetFirstLevel(_assembly)
                .FirstOrDefault(i => string.Equals(i.Key, componentKey, StringComparison.OrdinalIgnoreCase));
    }
}
