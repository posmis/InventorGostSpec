using ClosedXML.Excel;
using InventorGostSpec.Core.Model;

namespace InventorGostSpec.Excel.Tests;

/// <summary>Чтение файлов, поправленных вручную.</summary>
public class ExcelReaderTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"spec-{Guid.NewGuid():N}.xlsx");

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    private void Write(Action<XLWorkbook> fill)
    {
        using var workbook = new XLWorkbook();
        fill(workbook);
        workbook.SaveAs(_path);
    }

    [Fact]
    public void Row_types_typed_by_hand_are_recognized()
    {
        Write(book =>
        {
            var sheet = book.AddWorksheet(ExcelFormat.SpecificationSheet);
            for (var c = 0; c < ExcelFormat.Columns.Length; c++)
            {
                sheet.Cell(1, c + 1).Value = ExcelFormat.Columns[c];
            }

            sheet.Cell(2, 1).Value = "раздел";
            sheet.Cell(3, 1).Value = " Резерв ";
            sheet.Cell(4, 1).Value = "НОВЫЙ ЛИСТ";
            sheet.Cell(5, 1).Value = "Что-то своё";
        });

        var rows = ExcelReader.Read(_path).Rows;

        Assert.Equal(
            new[] { ExcelFormat.RowTypes.Section, ExcelFormat.RowTypes.Reserve, ExcelFormat.RowTypes.PageBreak, "Что-то своё" },
            rows.Select(r => r.Type));
    }

    [Fact]
    public void Columns_are_found_by_header_in_any_order_and_values_are_text()
    {
        Write(book =>
        {
            var sheet = book.AddWorksheet(ExcelFormat.SpecificationSheet);
            var headers = new[] { "Наименование", "Кол.", "Поз.", "Тип строки", "Примечание", "Своя колонка" };
            for (var c = 0; c < headers.Length; c++)
            {
                sheet.Cell(1, c + 1).Value = headers[c];
            }

            sheet.Cell(2, 1).Value = "Провод";
            sheet.Cell(2, 2).Value = 2.5;
            sheet.Cell(2, 3).Value = 7;
            sheet.Cell(2, 4).Value = "Изделие";
            sheet.Cell(2, 5).Value = new DateTime(2025, 5, 1);
            sheet.Cell(2, 6).Value = "игнорируется";

            // Пустые строки в конце (оформление) отбрасываются, в середине — остаются.
            sheet.Cell(4, 1).Value = "Шайба";
            sheet.Cell(6, 6).Value = "только своя колонка";
        });

        var rows = ExcelReader.Read(_path).Rows;

        Assert.Equal(3, rows.Count);
        var wire = rows[0];
        Assert.Equal(("Провод", "2,5", "7", "Изделие", "01.05.2025"), (wire.Name, wire.Quantity, wire.Position, wire.Type, wire.Note));
        Assert.Equal("", wire.Designation);
        Assert.True(rows[1].IsEmpty);
        Assert.Equal("Шайба", rows[2].Name);
    }

    [Fact]
    public void Missing_specification_sheet_is_reported()
    {
        Write(book => book.AddWorksheet("Лист1"));

        var error = Assert.Throws<InvalidDataException>(() => ExcelReader.Read(_path));
        Assert.Contains(ExcelFormat.SpecificationSheet, error.Message);
    }

    [Fact]
    public void Title_fields_are_found_by_name_and_name_height_accepts_comma()
    {
        Write(book =>
        {
            book.AddWorksheet(ExcelFormat.SpecificationSheet);
            var title = book.AddWorksheet(ExcelFormat.TitleBlockSheet);
            title.Cell(2, 1).Value = ExcelFormat.TitleFields.Approver;
            title.Cell(2, 2).Value = "Сидоров";
            title.Cell(3, 1).Value = "обозначение";
            title.Cell(3, 2).Value = "АБВГ.301234.001";
            title.Cell(4, 1).Value = ExcelFormat.TitleFields.NameHeight;
            title.Cell(4, 2).Value = "3,5";
        });

        var block = ExcelReader.Read(_path).TitleBlock;

        Assert.Equal("Сидоров", block.Approver);
        Assert.Equal("АБВГ.301234.001", block.Designation);
        Assert.Equal(3.5, block.NameTextHeight);
        Assert.Empty(block.Changes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-2")]
    [InlineData("крупно")]
    public void Invalid_name_height_means_default(string value)
    {
        Write(book =>
        {
            book.AddWorksheet(ExcelFormat.SpecificationSheet);
            var title = book.AddWorksheet(ExcelFormat.TitleBlockSheet);
            title.Cell(2, 1).Value = ExcelFormat.TitleFields.NameHeight;
            title.Cell(2, 2).Value = value;
        });

        Assert.Null(ExcelReader.Read(_path).TitleBlock.NameTextHeight);
    }

    [Fact]
    public void Manual_title_data_of_missing_file_is_null()
    {
        Assert.Null(ExcelReader.ReadManualTitleData(_path));
        Assert.Empty(ExcelReader.ReadChanges(_path));
    }

    [Fact]
    public void File_open_in_another_program_can_still_be_read()
    {
        var context = SpecificationContext.Create(null, new Specification());
        ExcelExporter.Export(new Specification(), context, new TitleBlock
        {
            Changes = { new ChangeRecord { Number = "1", Document = "АБВГ.1-25" } },
        }, _path);

        // Excel держит открытый файл с запретом записи, но не чтения.
        using var locked = new FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);

        Assert.Equal("АБВГ.1-25", Assert.Single(ExcelReader.ReadChanges(_path)).Document);
    }
}
