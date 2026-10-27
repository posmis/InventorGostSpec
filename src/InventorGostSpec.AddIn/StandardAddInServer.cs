using System.Runtime.InteropServices;
using Inventor;
using InventorGostSpec.AddIn.Commands;
using InventorGostSpec.AddIn.Infrastructure;

namespace InventorGostSpec.AddIn;

/// <summary>Точка входа аддина. Inventor создаёт этот класс по ClassId из файла .addin.</summary>
[Guid(AddInInfo.Guid)]
[ComVisible(true)]
public sealed class StandardAddInServer : ApplicationAddInServer
{
    private Inventor.Application? _application;
    private CommandRegistry? _commands;

    public void Activate(ApplicationAddInSite addInSiteObject, bool firstTime)
    {
        try
        {
            _application = addInSiteObject.Application;
            var context = new AddInContext(_application);

            _commands = new CommandRegistry(context);
            _commands.Register(new AssignSectionCommand());
            _commands.Register(new AutoAssignCommand());
            _commands.Register(new UpdateSpecificationCommand());
            _commands.Register(new WritePositionsCommand());

            RibbonBuilder.Build(_application, _commands);
        }
        catch (Exception ex)
        {
            Log.Error("Activate", ex);
            throw;
        }
    }

    public void Deactivate()
    {
        _commands?.Dispose();
        _commands = null;

        if (_application is not null)
        {
            Marshal.ReleaseComObject(_application);
            _application = null;
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    public void ExecuteCommand(int commandID)
    {
        // Устаревший механизм, команды работают через ButtonDefinition.
    }

    public object Automation => null!;
}
