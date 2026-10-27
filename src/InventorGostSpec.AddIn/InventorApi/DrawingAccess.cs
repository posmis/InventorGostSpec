using InventorGostSpec.Core.Model;

namespace InventorGostSpec.AddIn.InventorApi;

/// <summary>Поиск чертежа рядом с моделью и определение его формата.</summary>
internal static class DrawingAccess
{
    private static readonly string[] Extensions = { ".idw", ".dwg" };

    /// <summary>Чертёж с тем же именем в той же папке, что и модель (Корпус.ipt → Корпус.idw).</summary>
    public static string? FindDrawing(string modelFileName)
    {
        foreach (var extension in Extensions)
        {
            var path = System.IO.Path.ChangeExtension(modelFileName, extension);
            if (System.IO.File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>Формат первого листа чертежа («А3», «А4х3») или пустая строка.</summary>
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

            var sheet = drawing.Sheets[1];
            // Размеры листа в API — в сантиметрах.
            return SheetFormats.FromSize(sheet.Width * 10, sheet.Height * 10);
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
