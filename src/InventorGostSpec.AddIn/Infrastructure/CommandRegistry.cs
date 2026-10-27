using System.Runtime.InteropServices;
using InventorGostSpec.AddIn.Commands;

namespace InventorGostSpec.AddIn.Infrastructure;

/// <summary>Создаёт кнопки Inventor для команд и вызывает команды с обработкой ошибок.</summary>
internal sealed class CommandRegistry : IDisposable
{
    private readonly AddInContext _context;
    private readonly List<(AddInCommand Command, ButtonDefinition Button, ButtonDefinitionSink_OnExecuteEventHandler Handler)> _items = new();

    public CommandRegistry(AddInContext context)
    {
        _context = context;
    }

    public ButtonDefinition this[string internalName] =>
        _items.First(i => i.Command.InternalName == internalName).Button;

    public void Register(AddInCommand command)
    {
        var definitions = _context.Application.CommandManager.ControlDefinitions;
        var button = FindExisting(definitions, command.InternalName)
                     ?? definitions.AddButtonDefinition(
                         command.DisplayName,
                         command.InternalName,
                         CommandTypesEnum.kNonShapeEditCmdType,
                         AddInInfo.ClientId,
                         command.Description,
                         command.Description,
                         IconLoader.Load(command.IconName, 16, _context.IsDarkTheme),
                         IconLoader.Load(command.IconName, 32, _context.IsDarkTheme),
                         ButtonDisplayEnum.kAlwaysDisplayText);

        ButtonDefinitionSink_OnExecuteEventHandler handler = _ => Execute(command);
        button.OnExecute += handler;
        _items.Add((command, button, handler));
    }

    public void Dispose()
    {
        foreach (var (_, button, handler) in _items)
        {
            button.OnExecute -= handler;
            Marshal.ReleaseComObject(button);
        }

        _items.Clear();
    }

    private void Execute(AddInCommand command)
    {
        try
        {
            command.Execute(_context);
        }
        catch (Exception ex)
        {
            Log.Error(command.InternalName, ex);
            _context.Messages.Error($"{command.DisplayName.Replace('\n', ' ')}: {ex.Message}\n\nПодробности: {Log.FilePath}");
        }
    }

    private static ButtonDefinition? FindExisting(ControlDefinitions definitions, string internalName)
    {
        try
        {
            return definitions[internalName] as ButtonDefinition;
        }
        catch (COMException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
