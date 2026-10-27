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

    // Высота шрифтов — в FontSizes (настройки утилиты PDF).

    /// <summary>Расстояние между базовыми линиями соседних строк текста в одной графе — 1,2 высоты букв (как в КОМПАС-3D).</summary>
    public const double LineSpacing = 1.2;

    /// <summary>Отступ в узкой графе «Формат», чтобы сжатый крупный шрифт был читаемым.</summary>
    public const double NarrowPadding = 0.5;

    /// <summary>
    /// Наклон шрифта: сдвиг вправо на единицу высоты, как у текста в КОМПАС-3D (≈20°).
    /// Шрифт в файле прямой, наклон делается при выводе.
    /// </summary>
    public const double ItalicShear = 0.364;

    /// <summary>
    /// Базовая линия текста в графе — ниже середины, как в КОМПАС-3D: в строке 8 мм — на 1,5 мм выше нижней линии,
    /// в графе основной надписи 5 мм — почти на линии.
    /// </summary>
    public static double BaselineGap(double cellHeight, double capHeight) => Math.Max(0.3, (cellHeight - capHeight) / 2 - 0.75);

    /// <summary>Базовая линия в строке таблицы — на 1,5 мм выше нижней линии (как в КОМПАС-3D), с местом для подчёркивания.</summary>
    public const double RowBaselineGap = 1.5;

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
