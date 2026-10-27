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

        public void ReportError(Exception exception) => Log.Error("SpecificationWindow", exception);
    }
}
