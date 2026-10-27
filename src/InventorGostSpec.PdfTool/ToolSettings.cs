using System.IO;
using System.Text.Json;

namespace InventorGostSpec.PdfTool;

/// <summary>Настройки утилиты: %APPDATA%\InventorGostSpec\pdf.json. Действуют и при вызове из Inventor.</summary>
public sealed class ToolSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "InventorGostSpec", "pdf.json");

    /// <summary>Семейство шрифта или путь к .ttf; пусто — первый найденный шрифт ГОСТ.</summary>
    public string Font { get; set; } = "";

    public bool Italic { get; set; }

    public bool OpenAfter { get; set; } = true;

    public static ToolSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<ToolSettings>(File.ReadAllText(FilePath)) ?? new ToolSettings()
                : new ToolSettings();
        }
        catch (Exception)
        {
            return new ToolSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception)
        {
            // Настройки — удобство, их потеря не мешает работе.
        }
    }
}
