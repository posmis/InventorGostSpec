using ClosedXML.Excel;
using InventorGostSpec.Core.Model;
using InventorGostSpec.Excel;

namespace InventorGostSpec.Pdf.Tests;

public class SheetMarkerTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"spec-{Guid.NewGuid():N}.xlsx");

    public void Dispose()
    {
        File.Delete(_path);
    }

    private static SheetRow Item(int position) =>
        new(ExcelFormat.RowTypes.Item, "", "", position.ToString(), "", $"Деталь {position}", "1", "", "");

    [Fact]
    public void Page_break_and_blank_rows_from_excel()
    {
        var rows = new[]
        {
            Item(1),
            new SheetRow(ExcelFormat.RowTypes.Blank, "", "", "", "", "", "", "", ""),
            new SheetRow(ExcelFormat.RowTypes.Text, "", "", "", "", "", "", "", ""),
            new SheetRow(ExcelFormat.RowTypes.PageBreak, "", "", "", "", "", "", "", ""),
            Item(2),
        };

        var pages = SpecPaginator.Paginate(rows, (text, _) => text.Length);

        Assert.Equal(2, pages.Count);
        Assert.Equal("1", pages[0][0].Position);
        Assert.Equal("2", pages[1][0].Position);
        Assert.Equal(4, pages[1][0].SourceRow);
    }

    [Fact]
    public void Marks_sheet_boundaries_in_excel_and_replaces_old_marks()
    {
        var file = new Specification();
        var context = SpecificationContext.Create(new SpecSettings(), file);
        ExcelExporter.Export(file, context, new TitleBlock(), _path);
        using (var workbook = new XLWorkbook(_path))
        {
            var sheet = workbook.Worksheet(ExcelFormat.SpecificationSheet);
            for (var i = 1; i <= 40; i++)
            {
                sheet.Cell(i + 1, ExcelFormat.TypeColumn).Value = ExcelFormat.RowTypes.Item;
                sheet.Cell(i + 1, ExcelFormat.PositionColumn).Value = i;
                sheet.Cell(i + 1, ExcelFormat.NameColumn).Value = $"Деталь {i}";
            }

            workbook.Save();
        }

        Assert.Equal(2, ExcelSheetMarker.Mark(_path, new PdfOptions()));
        Assert.Equal(2, ExcelSheetMarker.Mark(_path, new PdfOptions()));

        using var result = new XLWorkbook(_path);
        var marked = result.Worksheet(ExcelFormat.SpecificationSheet);
        Assert.Equal(ExcelFormat.SheetColumnTitle, marked.Cell(1, ExcelFormat.SheetColumn).GetString());
        Assert.Equal(1, marked.Cell(2, ExcelFormat.SheetColumn).GetValue<int>());

        // 29 строк на первом листе: строка 30 файла — последняя на листе 1, строка 31 — первая на листе 2.
        Assert.Equal(2, marked.Cell(31, ExcelFormat.SheetColumn).GetValue<int>());
        Assert.Equal(XLBorderStyleValues.Thick, marked.Cell(30, ExcelFormat.NameColumn).Style.Border.BottomBorder);
        Assert.NotEqual(XLBorderStyleValues.Thick, marked.Cell(29, ExcelFormat.NameColumn).Style.Border.BottomBorder);
        Assert.Equal(1 + 2, marked.Column(ExcelFormat.SheetColumn).CellsUsed().Count());

        // Колонка «Лист PDF» не мешает чтению.
        Assert.Equal(40, ExcelReader.Read(_path).Rows.Count);
    }
}
