using InventorGostSpec.Excel;

namespace InventorGostSpec.Pdf.Tests;

public class SettingsTests
{
    // Ширина пропорциональна высоте букв: 0,4 высоты на символ.
    private static double Measure(string text, double capHeight) => text.Length * capHeight * 0.4;

    private const string Name = "Гайка барашек DIN 315 - M8 - St - C";

    [Fact]
    public void Min_width_percent_controls_compression_before_wrapping()
    {
        // 35 символов × 2 мм = 70 мм в графе 60 мм: нужно сужение до 86 %.
        Assert.Single(SpecPaginator.Wrap(Name, 60, Measure, 5, 80));
        Assert.Equal(2, SpecPaginator.Wrap(Name, 60, Measure, 5, 90).Count);
        Assert.Equal(2, SpecPaginator.Wrap(Name, 60, Measure, 5, 100).Count);
    }

    [Fact]
    public void Zero_percent_means_no_automatic_wrapping()
    {
        Assert.Equal(new[] { Name + " " + Name }, SpecPaginator.Wrap(Name + " " + Name, 20, Measure, 5, 0));
        Assert.Equal(new[] { "Первая", "Вторая" }, SpecPaginator.Wrap("Первая\nВторая", 1, Measure, 5, 0));
    }

    [Fact]
    public void Table_row_font_size_is_used_for_wrapping()
    {
        // 45 символов: шрифт 5 — 90 мм (в графу 61 мм не сузить до 80 %), шрифт 3,5 — 63 мм (сужается).
        var row = new SheetRow(ExcelFormat.RowTypes.Item, "", "", "1", "", Name + ", оцинк.", "1", "", "");
        var small = new PdfOptions { Sizes = new FontSizes { TableRows = 3.5 } };

        Assert.Single(Assert.Single(SpecPaginator.Paginate(new[] { row }, Measure, small)));
        Assert.Equal(2, Assert.Single(SpecPaginator.Paginate(new[] { row }, Measure, new PdfOptions())).Count);
    }

    [Fact]
    public void Broken_settings_fall_back_to_defaults_and_reset_restores_them()
    {
        var settings = new PdfSettings { Sizes = new FontSizes { TableRows = 0, Organization = 7 }, MinWidthPercent = 150 };

        var options = settings.ToOptions();
        Assert.Equal(5, options.Sizes.TableRows);
        Assert.Equal(7, options.Sizes.Organization);
        Assert.Equal(100, options.MinWidthPercent);

        settings.ResetSizes();
        Assert.Equal(10, settings.Sizes.Organization);
        Assert.Equal(80, settings.MinWidthPercent);
    }
}
