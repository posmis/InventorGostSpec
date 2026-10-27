using InventorGostSpec.Core.Model;
using InventorGostSpec.Core.Parsing;
using InventorGostSpec.Core.Sections;
using InventorGostSpec.Core.Sorting;

namespace InventorGostSpec.Core.Tests;

public class NaturalComparerTests
{
    [Theory]
    [InlineData("АБВГ.301234.002", "АБВГ.301234.010")]
    [InlineData("М8", "М12")]
    [InlineData("М12х40", "М12х60")]
    [InlineData("болт", "Гайка")]
    [InlineData("Ежик", "Ёлка")]
    public void Orders_ascending(string smaller, string larger)
    {
        Assert.True(NaturalComparer.Instance.Compare(smaller, larger) < 0);
        Assert.True(NaturalComparer.Instance.Compare(larger, smaller) > 0);
    }

    [Fact]
    public void Latin_lookalikes_compare_as_cyrillic()
    {
        // «M12» набрано латинской M.
        Assert.True(NaturalComparer.Instance.Compare("M12", "М8") > 0);
        Assert.True(NaturalComparer.Instance.Compare("M12", "Н1") < 0);
    }

    [Fact]
    public void Empty_values_go_last()
    {
        Assert.True(NaturalComparer.CompareEmptyLast("", "А") > 0);
        Assert.True(NaturalComparer.CompareEmptyLast("А", " ") < 0);
        Assert.Equal(0, NaturalComparer.CompareEmptyLast(null, ""));
    }
}

public class StandardCategoryTests
{
    [Theory]
    [InlineData("ГОСТ 7798-70", StandardCategory.Interstate)]
    [InlineData("ГОСТ ISO 4014-2013", StandardCategory.Interstate)]
    [InlineData("ГОСТ Р 52627-2006", StandardCategory.National)]
    [InlineData("ГОСТ Р ИСО 4017-2013", StandardCategory.National)]
    [InlineData("ГOCT 7798-70", StandardCategory.Interstate)] // латинские O и C
    [InlineData("ОСТ 1 31100-80", StandardCategory.Industry)]
    [InlineData("ОСТ1 31100-80", StandardCategory.Industry)]
    [InlineData("СТО 12345678-001-2020", StandardCategory.Organization)]
    [InlineData("DIN 933", StandardCategory.Foreign)]
    [InlineData("ISO 4762", StandardCategory.Foreign)]
    [InlineData("EN 14399-4", StandardCategory.Foreign)]
    [InlineData("ТУ 14-1-123-90", StandardCategory.Other)]
    [InlineData("", StandardCategory.None)]
    public void Categorizes(string number, StandardCategory expected) =>
        Assert.Equal(expected, StandardCategories.Categorize(number));

    [Fact]
    public void Strips_prefix() => Assert.Equal("52627-2006", StandardCategories.StripPrefix("ГОСТ Р 52627-2006"));
}

public class StandardDesignationParserTests
{
    [Theory]
    [InlineData("Болт М12х60 ГОСТ 7798-70", "Болт", "М12х60", "ГОСТ 7798-70")]
    [InlineData("Гайка М12-6Н.5 ГОСТ 5915-70", "Гайка", "М12-6Н.5", "ГОСТ 5915-70")]
    [InlineData("Шайба А 12 ГОСТ 11371-78", "Шайба", "А 12", "ГОСТ 11371-78")]
    [InlineData("Подшипник 205 ГОСТ 8338-75", "Подшипник", "205", "ГОСТ 8338-75")]
    [InlineData("Кольцо   020-025-30   ГОСТ Р 52627-2006", "Кольцо", "020-025-30", "ГОСТ Р 52627-2006")]
    [InlineData("Винт с потайной головкой М6х20 ГОСТ 17475-80", "Винт с потайной головкой", "М6х20", "ГОСТ 17475-80")]
    [InlineData("Корпус", "Корпус", "", "")]
    public void Parses(string text, string name, string parameters, string standard) =>
        Assert.Equal(new StandardDesignation(name, parameters, standard), StandardDesignationParser.Parse(text));
}

public class DocumentCodesTests
{
    [Theory]
    [InlineData("АБВГ.301234.001СБ", "СБ")]
    [InlineData("АБВГ.301234.001 Э3", "Э3")]
    [InlineData("АБВГ.301234.001", "")]
    [InlineData("АБВГ.301234.001ТУ", "ТУ")]
    public void Extracts_code(string designation, string code) => Assert.Equal(code, DocumentCodes.ExtractCode(designation));

    [Fact]
    public void Documents_follow_gost_2_102_order()
    {
        var rows = new[] { "АБВГ.1.001ТУ", "АБВГ.1.001ПЗ", "АБВГ.1.001Э3", "АБВГ.1.001СБ", "АБВГ.1.001РЭ" }
            .Select(d => TestData.Row(SectionIds.Documentation, d))
            .OrderBy(r => r, new GostRowComparer(SortRule.Documentation))
            .Select(r => r.Get(SpecColumn.Designation));

        Assert.Equal(new[] { "АБВГ.1.001СБ", "АБВГ.1.001Э3", "АБВГ.1.001ПЗ", "АБВГ.1.001ТУ", "АБВГ.1.001РЭ" }, rows);
    }
}

public class GostRowComparerTests
{
    private static IEnumerable<string> Sort(SortRule rule, params SpecRow[] rows) =>
        rows.OrderBy(r => r, new GostRowComparer(rule)).Select(r => r.Get(SpecColumn.Name));

    [Fact]
    public void Parts_by_designation_with_empty_last()
    {
        var result = Sort(
            SortRule.ByDesignation,
            TestData.Row(SectionIds.Parts, "", "Без обозначения"),
            TestData.Row(SectionIds.Parts, "АБВГ.715431.010", "Вал"),
            TestData.Row(SectionIds.Parts, "АБВГ.715431.002", "Ось"));

        Assert.Equal(new[] { "Ось", "Вал", "Без обозначения" }, result);
    }

    [Fact]
    public void Standard_products_follow_gost()
    {
        var result = Sort(
            SortRule.StandardProducts,
            TestData.Row(SectionIds.StandardProducts, name: "Шайба 12 ГОСТ 6402-70"),
            TestData.Row(SectionIds.StandardProducts, name: "Болт М12х60 ГОСТ 7798-70"),
            TestData.Row(SectionIds.StandardProducts, name: "Болт М8х20 ГОСТ 7798-70"),
            TestData.Row(SectionIds.StandardProducts, name: "Болт М12х40 ГОСТ 7798-70"),
            TestData.Row(SectionIds.StandardProducts, name: "Болт М6х20 ГОСТ 7796-70"),
            TestData.Row(SectionIds.StandardProducts, name: "Кольцо 020-025-30 ГОСТ Р 52627-2006"),
            TestData.Row(SectionIds.StandardProducts, name: "Гайка М12 ОСТ 1 33041-80"),
            TestData.Row(SectionIds.StandardProducts, name: "Гайка М12 ГОСТ 5915-70"));

        Assert.Equal(
            new[]
            {
                // Межгосударственные: по наименованию, затем по номеру стандарта, затем по размерам.
                "Болт М6х20 ГОСТ 7796-70",
                "Болт М8х20 ГОСТ 7798-70",
                "Болт М12х40 ГОСТ 7798-70",
                "Болт М12х60 ГОСТ 7798-70",
                "Гайка М12 ГОСТ 5915-70",
                "Шайба 12 ГОСТ 6402-70",
                // Национальные.
                "Кольцо 020-025-30 ГОСТ Р 52627-2006",
                // Отраслевые.
                "Гайка М12 ОСТ 1 33041-80",
            },
            result);
    }

    [Fact]
    public void Foreign_standards_go_first()
    {
        var result = Sort(
            SortRule.StandardProducts,
            TestData.Row(SectionIds.StandardProducts, name: "Болт М8х20 ГОСТ 7798-70"),
            TestData.Row(SectionIds.StandardProducts, name: "Гайка барашек DIN 315 - M8 - St - C"),
            TestData.Row(SectionIds.StandardProducts, name: "Винт ISO 4762 - M6 x 20 - 8.8"),
            TestData.Row(SectionIds.StandardProducts, name: "Кольцо 020-025-30 ГОСТ Р 52627-2006"),
            TestData.Row(SectionIds.StandardProducts, name: "Болт М8х20 ГОСТ ISO 4014-2013"));

        Assert.Equal(
            new[]
            {
                "Винт ISO 4762 - M6 x 20 - 8.8",
                "Гайка барашек DIN 315 - M8 - St - C",
                "Болт М8х20 ГОСТ ISO 4014-2013",
                "Болт М8х20 ГОСТ 7798-70",
                "Кольцо 020-025-30 ГОСТ Р 52627-2006",
            },
            result);
    }

    [Fact]
    public void Gost_iso_goes_with_gost_even_with_stale_standard_property()
    {
        // SPEC_StandardNumber остался от старого наименования из Content Center.
        var stale = TestData.Row(SectionIds.StandardProducts, name: "Винт ГОСТ ISO 7380-1-M4x8");
        stale.Source.StandardNumber = "ISO 7380-1";

        var result = Sort(
            SortRule.StandardProducts,
            TestData.Row(SectionIds.StandardProducts, name: "Шайба 8 ГОСТ 6402-70"),
            stale,
            TestData.Row(SectionIds.StandardProducts, name: "ГОСТ ISO 4762-M6x20"),
            TestData.Row(SectionIds.StandardProducts, name: "Гайка барашек DIN 315 - M8 - St - C"),
            TestData.Row(SectionIds.StandardProducts, name: "Болт М8х20 ГОСТ 7798-70"));

        Assert.Equal("Гайка барашек DIN 315 - M8 - St - C", result.First());
        Assert.Equal(StandardCategory.Interstate, StandardCategories.Categorize(StandardDesignationParser.Parse("ГОСТ ISO 4762-M6x20").StandardNumber));
        Assert.DoesNotContain("ГОСТ ISO", result.First());
        Assert.Equal(4, result.Skip(1).Count(n => n.Contains("ГОСТ")));
    }

    [Fact]
    public void Standard_products_group_has_priority_over_name()
    {
        var bearing = TestData.Row(SectionIds.StandardProducts, name: "Подшипник 205 ГОСТ 8338-75");
        bearing.Source.Group = "Подшипники";
        var bolt = TestData.Row(SectionIds.StandardProducts, name: "Болт М8х20 ГОСТ 7798-70");
        bolt.Source.Group = "Крепежные изделия";

        Assert.Equal(new[] { bolt.Get(SpecColumn.Name), bearing.Get(SpecColumn.Name) }, Sort(SortRule.StandardProducts, bearing, bolt));
    }

    [Fact]
    public void Materials_by_kind_then_name()
    {
        static SpecRow Material(string name, MaterialKind kind)
        {
            var row = TestData.Row(SectionIds.Materials, name: name);
            row.Source.MaterialKind = kind.ToString();
            return row;
        }

        var result = Sort(
            SortRule.Materials,
            Material("Эмаль ПФ-115", MaterialKind.PaintsChemicals),
            Material("Лист 2 ГОСТ 19903-2015", MaterialKind.FerrousMetals),
            Material("Круг 20 ГОСТ 2590-2006", MaterialKind.FerrousMetals),
            Material("Лента Л63", MaterialKind.NonFerrousMetals),
            Material("Без вида", MaterialKind.Other));

        Assert.Equal(new[] { "Круг 20 ГОСТ 2590-2006", "Лист 2 ГОСТ 19903-2015", "Лента Л63", "Эмаль ПФ-115", "Без вида" }, result);
    }
}
