using Inventor;

namespace InventorGostSpec.AddIn.Infrastructure;

/// <summary>Общие объекты, доступные командам.</summary>
internal sealed class AddInContext
{
    public AddInContext(Inventor.Application application)
    {
        Application = application;
        Settings = new SettingsStore();
    }

    public Inventor.Application Application { get; }

    public SettingsStore Settings { get; }

    /// <summary>HWND главного окна Inventor — владелец наших окон и сообщений.</summary>
    public IntPtr MainWindowHandle => new(Application.MainFrameHWND);

    public Messages Messages => new(MainWindowHandle);

    /// <summary>Включена тёмная тема Inventor (значки берутся в тёмном варианте).</summary>
    public bool IsDarkTheme
    {
        get
        {
            try
            {
                return Application.ThemeManager.ActiveTheme.Name.Contains("Dark", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Log.Error("IsDarkTheme", ex);
                return false;
            }
        }
    }

    /// <summary>Активная сборка или <c>null</c> с сообщением пользователю.</summary>
    public AssemblyDocument? RequireAssembly()
    {
        if (Application.ActiveDocument is AssemblyDocument assembly)
        {
            return assembly;
        }

        Messages.Warning("Откройте сборку.");
        return null;
    }
}
