using InventorGostSpec.Core.Model;

namespace InventorGostSpec.Core.Tests;

public class SheetFormatListTests
{
    [Theory]
    [InlineData(new[] { "А3", "А1", "А2", "А2х3", "А3" }, "А1, А2х3, А2, А3")]
    [InlineData(new[] { "A4x3", "А4", "А4х4" }, "А4х4, А4х3, А4")]
    [InlineData(new[] { "А4" }, "А4")]
    [InlineData(new[] { "", "А3", "" }, "А3")]
    [InlineData(new[] { "А2, А3", "А1" }, "А1, А2, А3")]
    [InlineData(new[] { "Нестандартный", "А4" }, "А4, Нестандартный")]
    [InlineData(new string[0], "")]
    public void Formats_of_sheets_are_listed_from_largest(string[] formats, string expected)
    {
        Assert.Equal(expected, SheetFormats.Combine(formats));
    }

    [Theory]
    [InlineData("А1", false)]
    [InlineData("А4", false)]
    [InlineData("", false)]
    [InlineData("БЧ", false)]
    [InlineData("*)", false)]
    [InlineData("А4х3", true)]
    [InlineData("A3x4", true)]
    [InlineData("А1, А2", true)]
    public void Footnote_only_for_derived_or_several_formats(string format, bool expected)
    {
        Assert.Equal(expected, SheetFormats.NeedsFootnote(format));
    }

    [Fact]
    public void Formats_go_to_note_with_footnote_mark()
    {
        Assert.Equal(("*)", "*)А4х3"), SheetFormats.ForColumns("А4х3", ""));
        Assert.Equal(("*)", "*)А1, А2\nЛитьё"), SheetFormats.ForColumns(" А1, А2 ", "Литьё"));
        Assert.Equal(("А3", "Литьё"), SheetFormats.ForColumns("А3", "Литьё"));
    }
}
