using InventorGostSpec.Core.Model;

namespace InventorGostSpec.AddIn.InventorApi;

/// <summary>Формат чертежа: форматы всех листов. Поиск чертежа — <see cref="DrawingSearch"/>.</summary>
internal static class DrawingAccess
{
    /// <summary>
    /// Форматы всех листов чертежа одной строкой в порядке уменьшения («А1, А2х3, А2») или пустая строка.
    /// Закрытый чертёж открывается невидимо и закрывается.
    /// </summary>
    public static string ReadFormat(Inventor.Application application, string drawingFileName)
    {
        var document = FindOpen(application, drawingFileName);
        var openedHere = false;
        try
        {
            if (document is null)
            {
                document = application.Documents.Open(drawingFileName, false);
                openedHere = true;
            }

            if (document is not DrawingDocument drawing || drawing.Sheets.Count == 0)
            {
                return "";
            }

            // Размеры листа в API — в сантиметрах.
            var formats = new List<string>();
            foreach (Inventor.Sheet sheet in drawing.Sheets)
            {
                formats.Add(SheetFormats.FromSize(sheet.Width * 10, sheet.Height * 10));
            }

            return SheetFormats.Combine(formats);
        }
        catch (Exception ex)
        {
            Infrastructure.Log.Error("DrawingAccess.ReadFormat " + drawingFileName, ex);
            return "";
        }
        finally
        {
            if (openedHere)
            {
                document?.Close(true);
            }
        }
    }

    private static Document? FindOpen(Inventor.Application application, string fileName)
    {
        foreach (Document document in application.Documents)
        {
            if (string.Equals(document.FullFileName, fileName, StringComparison.OrdinalIgnoreCase))
            {
                return document;
            }
        }

        return null;
    }
}
