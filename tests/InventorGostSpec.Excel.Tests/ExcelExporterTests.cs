using ClosedXML.Excel;
using InventorGostSpec.Core.Editing;
using InventorGostSpec.Core.Model;
using InventorGostSpec.Core.Positions;
using InventorGostSpec.Core.Sections;
using InventorGostSpec.Core.Update;

namespace InventorGostSpec.Excel.Tests;

public class ExcelExporterTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"spec-{Guid.NewGuid():N}.xlsx");

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    [Fact]
    public void Exports_rows_with_types_values_and_title_block()
    {
        var spec = new Specification();
        var settings = new SpecSettings { DefaultReserves = { [SectionIds.Parts] = new ReserveSettings { Count = 1 } } };
        var context = SpecificationContext.Create(settings, spec);
        SpecificationUpdater.Update(spec, new[]
        {
            new ComponentData { Key = "a.ipt", Kind = ComponentKind.Part, Designation = "АБВГ.715431.001", Name = "Корпус", Format = "А3", Quantity = 1 },
            new ComponentData { Key = "b.ipt", Kind = ComponentKind.Part, IsContentCenter = true, Name = "Болт М8х20 ГОСТ 7798-70", Quantity = 4 },
            new ComponentData { Key = "c", Kind = ComponentKind.Virtual, SectionId = SectionIds.Materials, Name = "Провод ПуГВ 1х1,5", Quantity = 2.5m, Unit = "м" },
        }, context, DateTime.UtcNow);
        var body = spec.Rows.Single(r => r.Get(SpecColumn.Name) == "Корпус");
        body.Note = "Покупная";
        SpecificationEditor.AddNote(spec, context, SectionIds.Parts, "Детали по ТУ", body.Id);
        var group = SpecificationEditor.AddGroup(spec, "Изделия, устанавливаемые по МЭ");
        SpecificationEditor.MoveToGroup(spec, context, spec.Rows.Single(r => r.Get(SpecColumn.Name).StartsWith("Провод")), group.Id);
        PositionAllocator.RenumberAll(spec, context);

        var titleBlock = new TitleBlock
        {
            Designation = "АБВГ.301234.001",
            Name = "Привод",
            Developer = "Иванов",
            Changes = { new ChangeRecord { Number = "1", Sheet = "1-3", Document = "АБВГ.12-25", Signature = "Петров", Date = "05.25" } },
        };
        ExcelExporter.Export(spec, context, titleBlock, _path);

        using var workbook = new XLWorkbook(_path);
        Assert.Equal(new[] { ExcelFormat.SpecificationSheet, ExcelFormat.TitleBlockSheet, ExcelFormat.ChangesSheet, ExcelFormat.HelpSheet },
            workbook.Worksheets.Select(w => w.Name));

        var sheet = workbook.Worksheet(ExcelFormat.SpecificationSheet);
        Assert.Equal(ExcelFormat.Columns, Enumerable.Range(1, ExcelFormat.Columns.Length).Select(c => sheet.Cell(1, c).GetString()));

        var rows = Enumerable.Range(2, sheet.LastRowUsed()!.RowNumber() - 1)
            .Select(r => (Type: sheet.Cell(r, ExcelFormat.TypeColumn).GetString(), Name: sheet.Cell(r, ExcelFormat.NameColumn).GetString(), Row: r))
            .ToList();
        Assert.Equal(
            new[]
            {
                ("Раздел", "Детали"), ("Изделие", "Корпус"), ("Текст", "Детали по ТУ"), ("Резерв", ""),
                ("Раздел", "Стандартные изделия"), ("Изделие", "Болт М8х20 ГОСТ 7798-70"),
                ("Группа", "Изделия, устанавливаемые по МЭ"), ("Раздел", "Материалы"), ("Изделие", "Провод ПуГВ 1х1,5"),
            },
            rows.Select(r => (r.Type, r.Name)));

        var bodyRow = rows.Single(r => r.Name == "Корпус").Row;
        Assert.Equal("А3", sheet.Cell(bodyRow, ExcelFormat.FormatColumn).GetString());
        Assert.Equal(1, sheet.Cell(bodyRow, ExcelFormat.PositionColumn).GetValue<int>());
        Assert.Equal("АБВГ.715431.001", sheet.Cell(bodyRow, ExcelFormat.DesignationColumn).GetString());
        Assert.Equal("Покупная", sheet.Cell(bodyRow, ExcelFormat.NoteColumn).GetString());

        var wireRow = rows.Single(r => r.Name.StartsWith("Провод")).Row;
        Assert.Equal(2.5, sheet.Cell(wireRow, ExcelFormat.QuantityColumn).GetValue<double>());
        Assert.Equal("м", sheet.Cell(wireRow, ExcelFormat.UnitColumn).GetString());
        Assert.Equal(2, sheet.Cell(rows.Single(r => r.Type == "Резерв").Row, ExcelFormat.PositionColumn).GetValue<int>());

        var title = workbook.Worksheet(ExcelFormat.TitleBlockSheet);
        Assert.Equal(ExcelFormat.TitleFields.Designation, title.Cell(2, 1).GetString());
        Assert.Equal("АБВГ.301234.001", title.Cell(2, 2).GetString());
        Assert.Equal("Иванов", title.Cell(6, 2).GetString());
        Assert.Equal(ExcelFormat.TitleFields.NameHeight, title.Cell(12, 1).GetString());
        Assert.True(title.Cell(12, 2).IsEmpty());

        // Изменения — текстом, без превращения «1-3» и «05.25» в даты.
        var changes = workbook.Worksheet(ExcelFormat.ChangesSheet);
        Assert.Equal(ExcelFormat.ChangeColumns, Enumerable.Range(1, ExcelFormat.ChangeColumns.Length).Select(c => changes.Cell(1, c).GetString()));
        Assert.Equal("1", changes.Cell(2, ExcelFormat.ChangeNumberColumn).GetString());
        Assert.Equal(XLDataType.Text, changes.Cell(2, ExcelFormat.ChangeSheetColumn).DataType);
        Assert.Equal("1-3", changes.Cell(2, ExcelFormat.ChangeSheetColumn).GetString());
        Assert.Equal("АБВГ.12-25", changes.Cell(2, ExcelFormat.ChangeDocumentColumn).GetString());
        Assert.Equal("05.25", changes.Cell(2, ExcelFormat.ChangeDateColumn).GetString());
        Assert.Equal("@", changes.Cell(5, ExcelFormat.ChangeDateColumn).Style.NumberFormat.Format);
    }

    [Fact]
    public void Reads_changes_back_skipping_empty_rows()
    {
        Assert.Empty(ExcelReader.ReadChanges(_path));

        var titleBlock = new TitleBlock
        {
            NameTextHeight = 7,
            Changes =
            {
                new ChangeRecord { Number = "1", Sheet = "2", Document = "АБВГ.5-25", Date = "05.25" },
                new ChangeRecord { Number = "2", Areas = "3", Sheet = "1-3", Document = "АБВГ.9-25", Signature = "Петров", Date = "10.25" },
            },
        };
        ExcelExporter.Export(new Specification(), SpecificationContext.Create(new SpecSettings(), new Specification()), titleBlock, _path);

        var changes = ExcelReader.ReadChanges(_path);
        Assert.Equal(7, ExcelReader.Read(_path).TitleBlock.NameTextHeight);
        Assert.Equal(new[] { "1", "2" }, changes.Select(c => c.Number));
        Assert.Equal("1-3", changes[1].Sheet);
        Assert.Equal("3", changes[1].Areas);
        Assert.Equal("Петров", changes[1].Signature);
        Assert.Equal("10.25", changes[1].Date);
    }
}
