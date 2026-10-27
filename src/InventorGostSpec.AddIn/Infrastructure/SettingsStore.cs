using InventorGostSpec.Core.Model;
using InventorGostSpec.Core.Storage;

namespace InventorGostSpec.AddIn.Infrastructure;

/// <summary>Глобальные настройки в %AppData%\InventorGostSpec\settings.json.</summary>
internal sealed class SettingsStore
{
    public static string Directory => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "InventorGostSpec");

    public static string FilePath => System.IO.Path.Combine(Directory, "settings.json");

    public SpecSettings Load()
    {
        if (!System.IO.File.Exists(FilePath))
        {
            return new SpecSettings();
        }

        try
        {
            return SpecificationSerializer.DeserializeSettings(System.IO.File.ReadAllText(FilePath));
        }
        catch (Exception ex)
        {
            Log.Error("SettingsStore.Load", ex);
            return new SpecSettings();
        }
    }

    public void Save(SpecSettings settings)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var temp = FilePath + ".tmp";
        System.IO.File.WriteAllText(temp, SpecificationSerializer.SerializeSettings(settings));
        System.IO.File.Move(temp, FilePath, overwrite: true);
    }
}
