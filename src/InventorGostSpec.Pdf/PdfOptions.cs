namespace InventorGostSpec.Pdf;

/// <summary>Настройки вывода PDF.</summary>
public sealed class PdfOptions
{
    /// <summary>
    /// Семейство шрифта («GOST type B») или путь к файлу .ttf.
    /// Пусто — первый найденный шрифт ГОСТ, иначе Arial.
    /// </summary>
    public string? Font { get; set; }

    /// <summary>Наклонный шрифт (тип Б с наклоном по ГОСТ 2.304), если у шрифта нет своего наклона.</summary>
    public bool Italic { get; set; }
}
