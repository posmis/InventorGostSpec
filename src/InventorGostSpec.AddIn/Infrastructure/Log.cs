using System.Text;

namespace InventorGostSpec.AddIn.Infrastructure;

/// <summary>Журнал ошибок в %AppData%\InventorGostSpec\log.txt.</summary>
internal static class Log
{
    private const long MaxSize = 1024 * 1024;

    public static string FilePath => System.IO.Path.Combine(SettingsStore.Directory, "log.txt");

    public static void Error(string where, Exception ex)
    {
        try
        {
            System.IO.Directory.CreateDirectory(SettingsStore.Directory);
            var file = new System.IO.FileInfo(FilePath);
            if (file.Exists && file.Length > MaxSize)
            {
                file.Delete();
            }

            System.IO.File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{where}] {ex}{Environment.NewLine}", Encoding.UTF8);
        }
        catch
        {
            // Журнал не должен ломать работу Inventor.
        }
    }
}
