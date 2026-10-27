namespace InventorGostSpec.Pdf;

/// <summary>Настройки вывода PDF.</summary>
public sealed class PdfOptions
{
    /// <summary>По умолчанию текст можно сузить до 80 % ширины, прежде чем переносить.</summary>
    public const int DefaultMinWidthPercent = 80;

    /// <summary>
    /// Семейство шрифта («GOST type B») или путь к файлу .ttf.
    /// Пусто — поставляемый шрифт «Gost merged», иначе первый найденный шрифт ГОСТ, иначе Arial.
    /// </summary>
    public string? Font { get; set; }

    public FontSizes Sizes { get; set; } = new();

    /// <summary>
    /// Насколько можно сузить текст по ширине (в процентах от исходной ширины), чтобы не переносить его.
    /// 100 — не сужать, сразу переносить; 0 — автоматически не переносить вовсе (только по Alt+Enter), сужать сколько нужно.
    /// </summary>
    public int MinWidthPercent { get; set; } = DefaultMinWidthPercent;
}
