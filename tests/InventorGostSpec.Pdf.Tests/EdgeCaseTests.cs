using InventorGostSpec.Core.Model;
using InventorGostSpec.Excel;

namespace InventorGostSpec.Pdf.Tests;

public class PdfEdgeCaseTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"edge-{Guid.NewGuid():N}");

    public void Dispose()
    {
        foreach (var file in new[] { _path, _path + ".pdf", _path + ".xlsx" }.Where(File.Exists))
        {
            File.Delete(file);
        }
    }

    private static double Measure(string text, double capHeight) => text.Length * 2.0;

    private static SheetRow Row(string type, string name = "", string position = "") =>
        new(type, "", "", position, "", name, "", "", "");

    [Theory]
    [InlineData("1–3", 2, true)]
    [InlineData("3-1", 2, true)]
    [InlineData("2;5", 5, true)]
    [InlineData("10", 1, false)]
    [InlineData("1-", 1, true)]
    public void Change_sheet_ranges(string sheets, int sheet, bool expected)
    {
        Assert.Equal(expected, GostPdfWriter.SheetMatches(sheets, sheet));
    }

    [Fact]
    public void Group_title_followed_by_section_title_has_one_free_line_between()
    {
        var page = Assert.Single(SpecPaginator.Paginate(
            new[] { Row(ExcelFormat.RowTypes.Group, "По МЭ"), Row(ExcelFormat.RowTypes.Section, "Детали"), Row(ExcelFormat.RowTypes.Item, "Корпус", "1") },
            Measure));

        Assert.Equal(new[] { "", "По МЭ", "", "Детали", "", "Корпус" }, page.Select(l => l.Name));
        Assert.Equal(LineStyle.Group, page[1].Style);
    }

    [Fact]
    public void Consecutive_page_breaks_and_break_at_start_do_not_create_empty_sheets()
    {
        var pages = SpecPaginator.Paginate(
            new[]
            {
                Row(ExcelFormat.RowTypes.PageBreak),
                Row(ExcelFormat.RowTypes.Item, "Корпус", "1"),
                Row(ExcelFormat.RowTypes.PageBreak),
                Row(ExcelFormat.RowTypes.PageBreak),
                Row(ExcelFormat.RowTypes.Item, "Ось", "2"),
                Row(ExcelFormat.RowTypes.PageBreak),
            },
            Measure);

        Assert.Equal(new[] { "Корпус", "Ось" }, pages.Select(p => Assert.Single(p).Name));
    }

    [Fact]
    public void Empty_file_gives_one_empty_sheet()
    {
        var pages = SpecPaginator.Paginate(Array.Empty<SheetRow>(), Measure);

        Assert.Empty(Assert.Single(pages));
    }

    [Fact]
    public void Long_unbreakable_text_is_kept_on_one_line()
    {
        var lines = SpecPaginator.Wrap("АБВГ.301234.001-01АБВГ.301234.001-02АБВГ.301234.001-03", 20, Measure);

        Assert.Single(lines);
    }

    [Fact]
    public void Item_longer_than_a_sheet_continues_on_next_sheet()
    {
        var text = string.Join("\n", Enumerable.Range(1, 40).Select(i => $"Строка {i}"));

        var pages = SpecPaginator.Paginate(new[] { Row(ExcelFormat.RowTypes.Item, text, "1") }, Measure);

        Assert.Equal(new[] { 29, 11 }, pages.Select(p => p.Count));
        Assert.Equal("1", pages[0][0].Position);
        Assert.Equal("Строка 30", pages[1][0].Name);
    }

    [Fact]
    public void Non_font_files_are_rejected()
    {
        File.WriteAllText(_path, "это не шрифт");

        Assert.Null(FontLocator.ReadNames(_path));
        Assert.Null(CapHeightReader.Read(_path));
        Assert.Null(FontLocator.ReadNames(_path + ".нет"));
        Assert.Throws<InvalidOperationException>(() => GostPdfWriter.Write(new SpecificationFile(), new PdfOptions { Font = _path }, _path + ".pdf"));
    }

    [Fact]
    public void Font_can_be_given_by_file_path()
    {
        var bundled = FontLocator.Bundled()!;

        Assert.Equal(bundled.Family, FontLocator.Find(bundled.Path)!.Family);
        Assert.Equal(bundled.Path, FontLocator.Find("gost MERGED")!.Path);
    }

    [Fact]
    public void Title_block_with_long_texts_and_many_changes_is_written()
    {
        var file = new SpecificationFile
        {
            TitleBlock = new TitleBlock
            {
                Designation = "АБВГ.301234.001-01 очень длинное обозначение документа",
                Name = string.Join(" ", Enumerable.Repeat("Наименование", 12)),
                NameTextHeight = 10,
                Organization = "Общество с ограниченной ответственностью «Очень длинное название»",
                Letter = "О1 А Б В",
                Changes =
                {
                    new ChangeRecord { Number = "1" }, new ChangeRecord { Number = "2", Sheet = "1" },
                    new ChangeRecord { Number = "3", Sheet = "2-3" }, new ChangeRecord { Number = "4", Sheet = "абв" },
                },
            },
        };
        for (var i = 0; i < 70; i++)
        {
            file.Rows.Add(Row(ExcelFormat.RowTypes.Item, $"Деталь {i}", (i + 1).ToString()));
        }

        var options = new PdfOptions { Sizes = new FontSizes { TableRows = 3.5, Organization = 14 }, MinWidthPercent = 0 };
        Assert.Equal(3, GostPdfWriter.Write(file, options, _path + ".pdf"));
        Assert.True(new FileInfo(_path + ".pdf").Length > 1000);
    }

    [Fact]
    public void Marker_puts_boundary_before_row_after_page_break()
    {
        var context = SpecificationContext.Create(null, new Specification());
        ExcelExporter.Export(new Specification(), context, new TitleBlock(), _path + ".xlsx");
        using (var workbook = new ClosedXML.Excel.XLWorkbook(_path + ".xlsx"))
        {
            var sheet = workbook.Worksheet(ExcelFormat.SpecificationSheet);
            sheet.Cell(2, ExcelFormat.TypeColumn).Value = ExcelFormat.RowTypes.Item;
            sheet.Cell(2, ExcelFormat.NameColumn).Value = "Корпус";
            sheet.Cell(3, ExcelFormat.TypeColumn).Value = ExcelFormat.RowTypes.PageBreak;
            sheet.Cell(4, ExcelFormat.TypeColumn).Value = ExcelFormat.RowTypes.Item;
            sheet.Cell(4, ExcelFormat.NameColumn).Value = "Ось";
            workbook.Save();
        }

        Assert.Equal(2, ExcelSheetMarker.Mark(_path + ".xlsx", new PdfOptions()));

        using var result = new ClosedXML.Excel.XLWorkbook(_path + ".xlsx");
        var marked = result.Worksheet(ExcelFormat.SpecificationSheet);
        Assert.Equal(2, marked.Cell(4, ExcelFormat.SheetColumn).GetValue<int>());
        Assert.Equal(ClosedXML.Excel.XLBorderStyleValues.Thick, marked.Cell(3, ExcelFormat.NameColumn).Style.Border.BottomBorder);
    }
}

public class FormatFootnoteTests
{
    private static double Measure(string text, double capHeight) => text.Length * 1.0;

    [Fact]
    public void Several_formats_typed_in_excel_go_to_note()
    {
        var row = new SheetRow(ExcelFormat.RowTypes.Item, "А1, А2", "", "3", "АБВГ.715431.001", "Корпус", "1", "", "Литьё");

        var lines = Assert.Single(SpecPaginator.Paginate(new[] { row }, Measure));

        Assert.Equal("*)", lines[0].Format);
        Assert.Equal(new[] { "*)А1, А2", "Литьё" }, lines.Select(l => l.Note));
        Assert.Equal("", lines[1].Format);
    }

    [Fact]
    public void Exported_row_keeps_single_format_and_moves_derived()
    {
        var spec = new Specification();
        var context = SpecificationContext.Create(null, spec);
        Core.Update.SpecificationUpdater.Update(spec, new[]
        {
            new ComponentData { Key = "a.ipt", Kind = ComponentKind.Part, Designation = "А.001", Name = "Корпус", Format = "А4х3", Quantity = 1 },
            new ComponentData { Key = "b.ipt", Kind = ComponentKind.Part, Designation = "А.002", Name = "Ось", Format = "А3", Quantity = 1 },
        }, context, DateTime.UtcNow);
        var path = Path.Combine(Path.GetTempPath(), $"fmt-{Guid.NewGuid():N}.xlsx");
        try
        {
            ExcelExporter.Export(spec, context, new TitleBlock(), path);
            var rows = ExcelReader.Read(path).Rows.Where(r => r.Type == ExcelFormat.RowTypes.Item).ToList();

            Assert.Equal(("*)", "*)А4х3"), (rows[0].Format, rows[0].Note));
            Assert.Equal(("А3", ""), (rows[1].Format, rows[1].Note));

            // Повторный вывод того же файла не добавляет вторую сноску.
            var page = Assert.Single(SpecPaginator.Paginate(rows, Measure));
            Assert.Equal(new[] { "*)", "А3" }, page.Select(l => l.Format));
            Assert.Equal("*)А4х3", page[0].Note);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class NoteWrapTests
{
    private static double Measure(string text, double capHeight) => text.Length * 2.0;

    [Fact]
    public void Note_is_compressed_further_than_other_columns()
    {
        // 28 мм в графе 20 мм: при 80 % — перенос, при 70 % — одна строка.
        Assert.Equal(2, SpecPaginator.Wrap("А1, А2, А3, А4", 20, Measure, 5, 80).Count);
        Assert.Single(SpecPaginator.Wrap("А1, А2, А3, А4", 20, Measure, 5, 70));
    }

    [Fact]
    public void Note_lines_are_filled_up_to_allowed_compression()
    {
        static double Wide(string text, double capHeight) => text.Length * 2.7;

        // Строк всё равно две, но в первую входит больше: «*)А1, А2,» сжимается до 86 %.
        Assert.Equal(new[] { "*)А1,", "А2, А4" }, SpecPaginator.Wrap("*)А1, А2, А4", 20, Wide, 5, 75));
        Assert.Equal(new[] { "*)А1, А2,", "А4" }, SpecPaginator.Wrap("*)А1, А2, А4", 20, Wide, 5, 75, 60));
    }

    [Fact]
    public void Paginator_uses_note_settings()
    {
        var row = new SheetRow(ExcelFormat.RowTypes.Item, "", "", "1", "А.001", "Корпус", "1", "", "А1, А2, А3.");

        // 22 мм текста в графе (20 мм без отступов): 75 % — одна строка, 100 % — перенос.
        var page = Assert.Single(SpecPaginator.Paginate(new[] { row }, Measure, new PdfOptions()));
        Assert.Equal("А1, А2, А3.", Assert.Single(page).Note);

        var strict = Assert.Single(SpecPaginator.Paginate(new[] { row }, Measure, new PdfOptions { NoteMinWidthPercent = 100 }));
        Assert.Equal(2, strict.Count);
    }

    [Fact]
    public void Word_fitting_at_60_percent_is_only_compressed()
    {
        Assert.Equal(new[] { "Термообработка" }, SpecPaginator.Wrap("Термообработка", 20, Measure, 5, 70, 60));
    }

    [Fact]
    public void Long_word_is_hyphenated_by_syllables()
    {
        var lines = SpecPaginator.Wrap("Закалка высокочастотная", 12, Measure, 5, 70, 60);

        Assert.Equal(new[] { "Закалка", "высоко-", "частотная" }, lines);
        Assert.All(lines, l => Assert.True(Measure(l, 5) <= 12 * 100 / 60.0));
    }

    [Theory]
    [InlineData("термообработка", new[] { 3, 5, 7, 9, 12 })]
    [InlineData("сталь", new int[0])]
    [InlineData("подъезд", new[] { 4 })]
    public void Syllable_points(string word, int[] expected) =>
        Assert.Equal(expected, Hyphenation.Points(word));
}
