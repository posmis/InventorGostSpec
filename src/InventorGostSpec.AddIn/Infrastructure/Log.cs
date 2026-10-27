using System.Text;

namespace InventorGostSpec.AddIn.Infrastructure;

/// <summary>Журнал ошибок и сообщений для разбора в %AppData%\InventorGostSpec\log.txt.</summary>
internal static class Log
{
    private const long MaxSize = 1024 * 1024;

    public static string FilePath => System.IO.Path.Combine(SettingsStore.Directory, "log.txt");

    /// <summary>Сообщение для разбора (например, где искался чертёж и что найдено).</summary>
    public static void Info(string where, string message) => Write($"[{where}] {message}");

    public static void Error(string where, Exception ex) => Write($"[{where}] {ex}");

    private static void Write(string text)
    {
        try
        {
            System.IO.Directory.CreateDirectory(SettingsStore.Directory);
            var file = new System.IO.FileInfo(FilePath);
            if (file.Exists && file.Length > MaxSize)
            {
                file.Delete();
            }

            System.IO.File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {text}{Environment.NewLine}", Encoding.UTF8);
        }
        catch
        {
            // Журнал не должен ломать работу Inventor.
        }
    }
}
