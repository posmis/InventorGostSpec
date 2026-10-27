using InventorGostSpec.Core.Model;
using InventorGostSpec.Excel;
using PdfSharp.Pdf.IO;

namespace InventorGostSpec.Pdf.Tests;

public class PdfWriterTests : IDisposable
{
    private readonly string _pdf = Path.Combine(Path.GetTempPath(), $"spec-{Guid.NewGuid():N}.pdf");

    public void Dispose()
    {
        File.Delete(_pdf);
    }

    [Fact]
    public void Writes_sheets_with_fallback_font()
    {
        var file = new SpecificationFile
        {
            TitleBlock = new TitleBlock
            {
                Designation = "АБВГ.301234.001",
                Name = "Привод",
                Changes = { new ChangeRecord { Number = "1", Sheet = "2", Document = "АБВГ.12-25" } },
            },
        };
        file.Rows.Add(new SheetRow(ExcelFormat.RowTypes.Section, "", "", "", "", "Детали", "", "", ""));
        for (var i = 1; i <= 40; i++)
        {
            file.Rows.Add(new SheetRow("", "А4", "", i.ToString(), $"АБВГ.715431.{i:000}", $"Деталь {i}", "1", "", ""));
        }

        Assert.NotNull(FontLocator.Find(null));
        var pages = GostPdfWriter.Write(file, new PdfOptions(), _pdf);

        Assert.Equal(2, pages);
        using var document = PdfReader.Open(_pdf, PdfDocumentOpenMode.Import);
        Assert.Equal(2, document.PageCount);
        Assert.Equal(210, document.Pages[0].Width.Millimeter, 1);
    }

    [Fact]
    public void Unknown_font_is_reported()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            GostPdfWriter.Write(new SpecificationFile(), new PdfOptions { Font = "Нет такого шрифта" }, _pdf));

        Assert.Contains("Нет такого шрифта", error.Message);
    }

    [Fact]
    public void Reads_font_family_from_file()
    {
        var font = FontLocator.Find(null)!;

        var names = FontLocator.ReadNames(font.Path);

        Assert.Equal(font.Family, names!.Family);
    }
}
