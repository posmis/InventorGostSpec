using ClosedXML.Excel;

namespace InventorGostSpec.Excel;

/// <summary>Оформление листов файла Excel: шапки и сетка таблиц. Общее для экспорта и разметки листов.</summary>
public static class ExcelStyle
{
    public static readonly XLColor HeaderFill = XLColor.FromHtml("#EEF1F5");
    public static readonly XLColor GridColor = XLColor.FromHtml("#D9DDE3");

    /// <summary>Шапка таблицы: жирный шрифт на сером фоне.</summary>
    public static void Header(IXLRange range)
    {
        range.Style.Font.Bold = true;
        range.Style.Fill.BackgroundColor = HeaderFill;
    }

    /// <summary>Тонкая светлая сетка по всем ячейкам диапазона.</summary>
    public static void Grid(IXLRange range)
    {
        var border = range.Style.Border;
        border.InsideBorder = XLBorderStyleValues.Thin;
        border.OutsideBorder = XLBorderStyleValues.Thin;
        border.InsideBorderColor = GridColor;
        border.OutsideBorderColor = GridColor;
    }
}
