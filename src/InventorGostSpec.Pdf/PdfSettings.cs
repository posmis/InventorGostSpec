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

    /// <summary>Допустимое сужение в графе «Примечание», %. См. <see cref="PdfOptions.NoteMinWidthPercent"/>.</summary>
    public int NoteMinWidthPercent { get; set; } = PdfOptions.DefaultNoteMinWidthPercent;

    /// <summary>Сужение длинного слова в «Примечании» до переноса по слогам, %. См. <see cref="PdfOptions.WordMinWidthPercent"/>.</summary>
    public int WordMinWidthPercent { get; set; } = PdfOptions.DefaultWordMinWidthPercent;

    /// <summary>Параметры вывода по этим настройкам.</summary>
    public PdfOptions ToOptions() => new()
    {
        Font = Font.Length > 0 ? Font : null,
        Sizes = (Sizes ?? new FontSizes()).Normalized(),
        MinWidthPercent = Math.Clamp(MinWidthPercent, 0, 100),
        NoteMinWidthPercent = Math.Clamp(NoteMinWidthPercent, 1, 100),
        WordMinWidthPercent = Math.Clamp(WordMinWidthPercent, 1, 100),
    };

    /// <summary>Размеры шрифтов и сужение — по умолчанию (шрифт не меняется).</summary>
    public void ResetSizes()
    {
        Sizes = new FontSizes();
        MinWidthPercent = PdfOptions.DefaultMinWidthPercent;
        NoteMinWidthPercent = PdfOptions.DefaultNoteMinWidthPercent;
        WordMinWidthPercent = PdfOptions.DefaultWordMinWidthPercent;
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

    /// <summary>Где хранятся настройки.</summary>
    public static string Location => FilePath;

    /// <summary>Сохраняет настройки. Не удалось (нет доступа к папке) — текст ошибки, иначе <c>null</c>.</summary>
    public string? Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            return null;
        }
        catch (Exception ex)
        {
            // Настройки — удобство, их потеря не мешает созданию PDF.
            return ex.Message;
        }
    }
}
