namespace InventorGostSpec.Pdf;

/// <summary>Настройки вывода PDF.</summary>
public sealed class PdfOptions
{
    /// <summary>По умолчанию текст можно сузить до 80 % ширины, прежде чем переносить.</summary>
    public const int DefaultMinWidthPercent = 80;

    /// <summary>В узкой графе «Примечание» — до 75 %: «*)А1, А2,» помещается в строку.</summary>
    public const int DefaultNoteMinWidthPercent = 75;

    /// <summary>Длинное слово в «Примечании» сужается до 60 %, дальше переносится по слогам.</summary>
    public const int DefaultWordMinWidthPercent = 60;

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

    /// <summary>Допустимое сужение текста в графе «Примечание», %.</summary>
    public int NoteMinWidthPercent { get; set; } = DefaultNoteMinWidthPercent;

    /// <summary>До скольких % ширины сужается длинное слово в «Примечании», прежде чем переноситься по слогам.</summary>
    public int WordMinWidthPercent { get; set; } = DefaultWordMinWidthPercent;
}
