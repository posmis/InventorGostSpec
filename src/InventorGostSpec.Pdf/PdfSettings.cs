using System.Text.Json;

namespace InventorGostSpec.Pdf;

/// <summary>
/// Настройки вывода PDF: %APPDATA%\InventorGostSpec\pdf.json. Общие для утилиты, вызова из Inventor
/// и разметки листов в Excel — чтобы листы в Excel совпадали с PDF.
/// </summary>
public sealed class PdfSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "InventorGostSpec", "pdf.json");

    /// <summary>Семейство шрифта или путь к .ttf; пусто — первый найденный шрифт ГОСТ.</summary>
    public string Font { get; set; } = "";

    public bool OpenAfter { get; set; } = true;

    /// <summary>Высота шрифтов, мм.</summary>
    public FontSizes Sizes { get; set; } = new();

    /// <summary>Допустимое сужение текста, % ширины (0 — не переносить автоматически). См. <see cref="PdfOptions.MinWidthPercent"/>.</summary>
    public int MinWidthPercent { get; set; } = PdfOptions.DefaultMinWidthPercent;

    /// <summary>Параметры вывода по этим настройкам.</summary>
    public PdfOptions ToOptions() => new()
    {
        Font = Font.Length > 0 ? Font : null,
        Sizes = (Sizes ?? new FontSizes()).Normalized(),
        MinWidthPercent = Math.Clamp(MinWidthPercent, 0, 100),
    };

    /// <summary>Размеры шрифтов и сужение — по умолчанию (шрифт не меняется).</summary>
    public void ResetSizes()
    {
        Sizes = new FontSizes();
        MinWidthPercent = PdfOptions.DefaultMinWidthPercent;
    }

    public static PdfSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<PdfSettings>(File.ReadAllText(FilePath)) ?? new PdfSettings()
                : new PdfSettings();
        }
        catch (Exception)
        {
            return new PdfSettings();
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
