namespace InventorGostSpec.Pdf;

/// <summary>
/// Размеры (мм) листа спецификации А4 по ГОСТ 2.106 (форма 1 — первый лист, 1а — последующие)
/// с основными надписями по ГОСТ 2.104 (форма 2 — первый лист, 2а — последующие).
/// Начало координат — левый верхний угол листа, ось Y — вниз.
/// </summary>
public static class GostLayout
{
    public const double PageWidth = 210;
    public const double PageHeight = 297;

    // Рамка: слева 20 мм (поле подшивки), с остальных сторон 5 мм.
    public const double FrameLeft = 20;
    public const double FrameTop = 5;
    public const double FrameRight = 205;
    public const double FrameBottom = 292;

    /// <summary>Высота шапки таблицы спецификации.</summary>
    public const double HeaderHeight = 15;

    /// <summary>Высота основной надписи по форме 2 (первый лист) и 2а (последующие).</summary>
    public const double FirstTitleHeight = 40;
    public const double NextTitleHeight = 15;

    /// <summary>Строк спецификации на первом листе и на последующих.</summary>
    public const int FirstPageRows = 29;
    public const int NextPageRows = 32;

    /// <summary>Графы спецификации: Формат, Зона, Поз., Обозначение, Наименование, Кол., Примечание.</summary>
    public static readonly double[] ColumnWidths = { 6, 6, 8, 70, 63, 10, 22 };

    public static readonly string[] ColumnTitles = { "Формат", "Зона", "Поз.", "Обозначение", "Наименование", "Кол.", "Приме-\nчание" };

    /// <summary>Графы, заголовок которых пишется вертикально (узкие).</summary>
    public static readonly bool[] VerticalTitles = { true, true, true, false, false, true, false };

    public const int FormatColumn = 0;
    public const int ZoneColumn = 1;
    public const int PositionColumn = 2;
    public const int DesignationColumn = 3;
    public const int NameColumn = 4;
    public const int QuantityColumn = 5;
    public const int NoteColumn = 6;

    /// <summary>
    /// Высота прописных букв (ГОСТ 2.304, шрифт 3,5): строки таблицы, все надписи и данные основной надписи,
    /// графы на поле подшивки, вертикальные заголовки граф — как в бланке КОМПАС-3D.
    /// </summary>
    public const double TextHeight = 3.5;

    /// <summary>Горизонтальные заголовки граф таблицы («Обозначение», «Наименование», «Примечание») — шрифт 5.</summary>
    public const double HeaderTextHeight = 5;

    /// <summary>Наименование изделия в основной надписи — шрифт 5 (3,5, если не помещается в две строки).</summary>
    public const double NameTextHeight = 5;

    /// <summary>Обозначение документа в основной надписи — шрифт 7.</summary>
    public const double DesignationTextHeight = 7;

    /// <summary>Отступ текста от линий графы.</summary>
    public const double Padding = 1;

    /// <summary>Толщина основной (сплошной толстой) и тонкой линий.</summary>
    public const double ThickLine = 0.5;
    public const double ThinLine = 0.18;

    public static int RowsOnPage(int pageIndex) => pageIndex == 0 ? FirstPageRows : NextPageRows;

    public static double ColumnLeft(int column) => FrameLeft + ColumnWidths.Take(column).Sum();

    /// <summary>Ширина для текста в графе (без отступов).</summary>
    public static double TextWidth(int column) => ColumnWidths[column] - 2 * Padding;
}
