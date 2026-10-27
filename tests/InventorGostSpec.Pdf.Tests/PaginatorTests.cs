using InventorGostSpec.Excel;

namespace InventorGostSpec.Pdf.Tests;

public class PaginatorTests
{
    // Моноширинная «метрика»: 2 мм на символ — графа «Наименование» (61 мм) вмещает 30 символов.
    private static double Measure(string text, double capHeight) => text.Length * 2.0;

    private static SheetRow Item(string name, string position = "", string designation = "") =>
        new(ExcelFormat.RowTypes.Item, "", "", position, designation, name, "1", "", "");

    private static SheetRow Section(string name) => new(ExcelFormat.RowTypes.Section, "", "", "", "", name, "", "", "");

    [Fact]
    public void Standard_designation_is_not_split_between_lines()
    {
        var lines = SpecPaginator.Wrap("Заклепка 3,2x8 AlA/St ГОСТ Р ИСО 15973-2005", 30 * 2, Measure);

        Assert.Equal(new[] { "Заклепка 3,2x8 AlA/St", "ГОСТ Р ИСО 15973-2005" }, lines);
    }

    [Fact]
    public void Long_words_stay_whole_and_explicit_line_breaks_are_kept()
    {
        Assert.Equal(new[] { "АБВГ.301234.001-01АБВГ.301234.001" }, SpecPaginator.Wrap("АБВГ.301234.001-01АБВГ.301234.001", 20, Measure));
        Assert.Equal(new[] { "Первая", "Вторая" }, SpecPaginator.Wrap("Первая\nВторая", 100, Measure));
    }

    [Fact]
    public void Section_gets_free_lines_around_it_but_not_at_top_of_sheet()
    {
        var pages = SpecPaginator.Paginate(new[] { Section("Детали"), Item("Корпус", "1"), Section("Стандартные изделия"), Item("Болт", "2") }, Measure);

        var page = Assert.Single(pages);
        Assert.Equal(new[] { "Детали", "", "Корпус", "", "Стандартные изделия", "", "Болт" }, page.Select(l => l.Name));
        Assert.Equal(LineStyle.Section, page[0].Style);
    }

    [Fact]
    public void Section_header_is_not_left_at_bottom_of_sheet()
    {
        // 26 изделий + заголовок раздела: на первом листе 29 строк, заголовок с пустой строкой и первым изделием не помещается.
        var rows = new List<SheetRow> { Section("Детали") };
        rows.AddRange(Enumerable.Range(1, 26).Select(i => Item($"Деталь {i}", i.ToString())));
        rows.Add(Section("Стандартные изделия"));
        rows.Add(Item("Болт", "27"));

        var pages = SpecPaginator.Paginate(rows, Measure);

        Assert.Equal(2, pages.Count);
        Assert.Equal("Деталь 26", pages[0][^1].Name);
        Assert.Equal(new[] { "Стандартные изделия", "", "Болт" }, pages[1].Select(l => l.Name));
    }

    [Fact]
    public void Multiline_item_is_kept_on_one_sheet()
    {
        var rows = Enumerable.Range(1, 28).Select(i => Item($"Деталь {i}", i.ToString())).ToList();
        rows.Add(Item("Очень длинное наименование изделия, которое не помещается в одну строку", "29"));

        var pages = SpecPaginator.Paginate(rows, Measure);

        Assert.Equal(28, pages[0].Count);
        Assert.Equal("29", pages[1][0].Position);
        Assert.Equal(3, pages[1].Count);
        Assert.All(pages[1].Skip(1), line => Assert.Equal("", line.Position));
    }

    [Fact]
    public void Sheets_hold_29_and_then_32_lines_and_quantity_carries_unit()
    {
        var rows = Enumerable.Range(1, 70).Select(i => Item($"Деталь {i}", i.ToString())).ToList();
        rows[0] = rows[0] with { Quantity = "2,5", Unit = "м" };

        var pages = SpecPaginator.Paginate(rows, Measure);

        Assert.Equal(new[] { 29, 32, 9 }, pages.Select(p => p.Count));
        Assert.Equal("2,5 м", pages[0][0].Quantity);
    }

    [Theory]
    [InlineData("2", 2, true)]
    [InlineData("2", 1, false)]
    [InlineData("1-3", 3, true)]
    [InlineData("1, 4", 4, true)]
    [InlineData("1, 4", 2, false)]
    [InlineData("", 1, true)]
    [InlineData("", 2, false)]
    [InlineData("Все", 1, true)]
    public void Change_record_sheet_numbers(string sheets, int sheet, bool expected)
    {
        Assert.Equal(expected, GostPdfWriter.SheetMatches(sheets, sheet));
    }
}
