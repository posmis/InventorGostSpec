using ClosedXML.Excel;
using InventorGostSpec.Core.Model;
using InventorGostSpec.Excel;

namespace InventorGostSpec.Pdf.Tests;

public class ManualLineBreakTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"spec-{Guid.NewGuid():N}.xlsx");

    public void Dispose()
    {
        File.Delete(_path);
    }

    [Fact]
    public void Alt_enter_in_excel_cell_breaks_line_in_pdf()
    {
        // Файл, как его выгружает аддин, затем ручная правка: Alt+Enter внутри ячейки даёт «\n».
        var context = SpecificationContext.Create(new SpecSettings(), new Specification());
        ExcelExporter.Export(new Specification(), context, new TitleBlock { Name = "Привод\nленточного конвейера" }, _path);
        using (var workbook = new XLWorkbook(_path))
        {
            var sheet = workbook.Worksheet(ExcelFormat.SpecificationSheet);
            sheet.Cell(2, ExcelFormat.TypeColumn).Value = ExcelFormat.RowTypes.Item;
            sheet.Cell(2, ExcelFormat.PositionColumn).Value = 1;
            sheet.Cell(2, ExcelFormat.NameColumn).Value = "Болт\r\nМ8х20 ГОСТ 7798-70";
            sheet.Cell(2, ExcelFormat.NoteColumn).Value = "Покупн.\nизделие";
            workbook.Save();
        }

        var file = ExcelReader.Read(_path);
        var page = Assert.Single(SpecPaginator.Paginate(file.Rows, (text, _) => text.Length));

        Assert.Equal(new[] { "Болт", "М8х20 ГОСТ 7798-70" }, page.Select(l => l.Name));
        Assert.Equal(new[] { "Покупн.", "изделие" }, page.Select(l => l.Note));
        Assert.Equal(new[] { "1", "" }, page.Select(l => l.Position));
        Assert.Equal("Привод\nленточного конвейера", file.TitleBlock.Name);
    }
}
