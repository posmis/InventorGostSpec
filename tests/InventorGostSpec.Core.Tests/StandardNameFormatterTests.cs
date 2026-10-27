using InventorGostSpec.Core.Parsing;

namespace InventorGostSpec.Core.Tests;

public class StandardNameFormatterTests
{
    [Theory]
    [InlineData("Болт М12х60 ГОСТ 7798-70", "Болт М12х60 ГОСТ 7798-70")]
    [InlineData("Болт ГОСТ 7798-70 M12x60", "Болт М12х60 ГОСТ 7798-70")]
    [InlineData("ГОСТ 7798-70 - Болт M12 x 60", "Болт М12х60 ГОСТ 7798-70")]
    [InlineData("Болт_М12х60_ГОСТ_7798-70", "Болт М12х60 ГОСТ 7798-70")]
    [InlineData("Гайка М12-6Н.5 ГОСТ 5915-70", "Гайка М12-6Н.5 ГОСТ 5915-70")]
    [InlineData("Шайба А 12 ГОСТ 11371-78", "Шайба А 12 ГОСТ 11371-78")]
    [InlineData("Шайба ГОСТ 11371-78 А 12", "Шайба А 12 ГОСТ 11371-78")]
    [InlineData("Кольцо ГОСТ Р 52627-2006 020-025-30", "Кольцо 020-025-30 ГОСТ Р 52627-2006")]
    [InlineData("Винт ГОСТ Р ИСО 4762-2012 М6x20", "Винт М6х20 ГОСТ Р ИСО 4762-2012")]
    [InlineData("Гайка ОСТ 1 33041-80 М8", "Гайка М8 ОСТ 1 33041-80")]
    [InlineData("Подшипник ГОСТ8338-75 205", "Подшипник 205 ГОСТ 8338-75")]
    [InlineData("Винт с потайной головкой ГОСТ 17475-80 М6х20", "Винт с потайной головкой М6х20 ГОСТ 17475-80")]
    [InlineData("болт ГОСТ 7798–70 М10х40", "Болт М10х40 ГОСТ 7798-70")]
    public void Builds_gost_name_from_file_name(string fileName, string expected)
    {
        var result = StandardNameFormatter.FromFileName(fileName);

        Assert.Equal(expected, result.Text);
        Assert.True(result.IsComplete);
    }

    [Theory]
    [InlineData("Гайка барашек DIN 315 - M8 - St - C")]
    [InlineData("Винт ISO 4762 - M6 x 20 - 8.8")]
    [InlineData("Гайка_барашек_DIN 315 - M8 - St - C")]
    public void Foreign_standard_names_are_kept_as_is(string fileName)
    {
        var result = StandardNameFormatter.FromFileName(fileName);

        Assert.True(result.IsForeign);
        Assert.Equal(fileName.Replace('_', ' '), result.Text);
        Assert.True(StandardNameFormatter.IsWellFormed(result.Text));
    }

    [Fact]
    public void Gost_with_iso_is_not_foreign()
    {
        var result = StandardNameFormatter.FromFileName("Болт ГОСТ ISO 4014-2013 M12x60");

        Assert.False(result.IsForeign);
        Assert.Equal("Болт М12х60 ГОСТ ISO 4014-2013", result.Text);
    }

    [Fact]
    public void Missing_name_is_taken_from_fallback()
    {
        var result = StandardNameFormatter.FromFileName("ГОСТ 7798-70 М12x60", "Болт");

        Assert.Equal("Болт М12х60 ГОСТ 7798-70", result.Text);
        Assert.Equal(new StandardDesignation("Болт", "М12х60", "ГОСТ 7798-70"), result.Parts);
    }

    [Fact]
    public void Incomplete_name_is_reported()
    {
        Assert.False(StandardNameFormatter.FromFileName("ГОСТ 7798-70 М12x60").IsComplete);
        Assert.False(StandardNameFormatter.FromFileName("Болт М12x60").IsComplete);
    }

    [Theory]
    [InlineData("Болт М12х60 ГОСТ 7798-70", true)]
    [InlineData("Болт ГОСТ 7798-70 М12х60", false)]
    [InlineData("Болт M12x60 ГОСТ 7798-70", false)]
    [InlineData("Болт М12х60", false)]
    [InlineData("", false)]
    public void Recognizes_well_formed_names(string description, bool expected) =>
        Assert.Equal(expected, StandardNameFormatter.IsWellFormed(description));
}
