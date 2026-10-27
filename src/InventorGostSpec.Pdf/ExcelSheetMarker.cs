using ClosedXML.Excel;
using InventorGostSpec.Excel;

namespace InventorGostSpec.Pdf;

/// <summary>
/// Разметка листов PDF в файле Excel: жирная линия под последней строкой каждого листа и номер листа
/// в колонке «Лист PDF» у первой строки листа. Раскладка та же, что при выводе в PDF.
/// После ручной правки файла разметку нужно обновить (кнопка в утилите PDF).
/// </summary>
public static class ExcelSheetMarker
{
    private static readonly XLColor ThinColor = XLColor.FromHtml("#D9DDE3");
    private static readonly XLColor MarkColor = XLColor.FromHtml("#1F2937");

    /// <summary>Размечает файл и возвращает число листов.</summary>
    public static int Mark(string path, PdfOptions options)
    {
        var file = ExcelReader.Read(path);
        var pages = GostPdfWriter.Paginate(file, options);

        // Первая строка файла (индекс) каждого листа: номер листа → строка.
        var firstRows = new List<(int Number, int Row)>();
        for (var page = 0; page < pages.Count; page++)
        {
            var rows = pages[page].Select(l => l.SourceRow).Where(r => r >= 0).ToList();
            if (rows.Count > 0)
            {
                firstRows.Add((page + 1, rows.Min()));
            }
        }

        using (var workbook = new XLWorkbook(path))
        {
            var sheet = workbook.Worksheet(ExcelFormat.SpecificationSheet);
            var last = Math.Max(sheet.LastRowUsed()?.RowNumber() ?? 1, file.Rows.Count + 1);
            var width = ExcelFormat.Columns.Length;

            // Снимаем прошлую разметку.
            var body = sheet.Range(2, 1, last, width);
            body.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            body.Style.Border.BottomBorderColor = ThinColor;
            sheet.Range(1, ExcelFormat.SheetColumn, last, ExcelFormat.SheetColumn).Clear();

            var header = sheet.Cell(1, ExcelFormat.SheetColumn);
            header.Value = ExcelFormat.SheetColumnTitle;
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = XLColor.FromHtml("#EEF1F5");
            header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            sheet.Column(ExcelFormat.SheetColumn).Width = 9;

            foreach (var (number, firstRow) in firstRows)
            {
                var excelRow = firstRow + 2;
                var cell = sheet.Cell(excelRow, ExcelFormat.SheetColumn);
                cell.Value = number;
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = MarkColor;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                if (number > 1)
                {
                    var border = sheet.Range(excelRow - 1, 1, excelRow - 1, ExcelFormat.SheetColumn).Style.Border;
                    border.BottomBorder = XLBorderStyleValues.Thick;
                    border.BottomBorderColor = MarkColor;
                }
            }

            workbook.Save();
        }

        return pages.Count;
    }
}
