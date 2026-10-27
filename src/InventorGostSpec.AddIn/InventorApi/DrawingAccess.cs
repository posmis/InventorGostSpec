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

    /// <summary>
    /// Сборочный чертёж сборки, по порядку:
    /// 1) чертёж с тем же именем, что и сборка (Привод.iam → Привод.idw);
    /// 2) чертёж с именем по обозначению сборки: «АБВГ.301234.001.idw», «АБВГ.301234.001СБ.idw», «АБВГ.301234.001 СБ.idw»;
    /// 3) открытый в Inventor чертёж, который ссылается на эту сборку;
    /// 4) чертёж в папке сборки, имя которого начинается с обозначения или имени сборки
    ///    («АБВГ.301234.001СБ Привод.idw»; исполнения вида «…-01» не подходят).
    /// </summary>
    public static string? FindAssemblyDrawing(Inventor.Application application, string assemblyFileName, string partNumber)
    {
        if (FindDrawing(assemblyFileName) is { } sameName)
        {
            return sameName;
        }

        var folder = System.IO.Path.GetDirectoryName(assemblyFileName) ?? "";
        var assemblyName = System.IO.Path.GetFileNameWithoutExtension(assemblyFileName);
        var designation = Designations.WithoutAssemblyDrawingCode(partNumber).Trim();
        if (designation.Length > 0)
        {
            foreach (var name in new[] { designation, designation + "СБ", designation + " СБ" })
            {
                foreach (var extension in Extensions)
                {
                    var path = System.IO.Path.Combine(folder, name + extension);
                    if (System.IO.File.Exists(path))
                    {
                        return path;
                    }
                }
            }
        }

        if (FindOpenDrawingOf(application, assemblyFileName) is { } open)
        {
            return open;
        }

        try
        {
            var candidates = System.IO.Directory.EnumerateFiles(folder)
                .Where(f => Extensions.Contains(System.IO.Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .Where(f =>
                {
                    var name = System.IO.Path.GetFileNameWithoutExtension(f);
                    return Designations.IsFileOfDocument(name, designation) || Designations.IsFileOfDocument(name, assemblyName);
                })
                .OrderByDescending(f => System.IO.Path.GetFileNameWithoutExtension(f).Contains("СБ", StringComparison.OrdinalIgnoreCase))
                .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return candidates.FirstOrDefault();
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or ArgumentException)
        {
            Infrastructure.Log.Error("DrawingAccess.FindAssemblyDrawing " + folder, ex);
            return null;
        }
    }

    /// <summary>Открытый чертёж, в котором есть виды этой сборки.</summary>
    private static string? FindOpenDrawingOf(Inventor.Application application, string assemblyFileName)
    {
        try
        {
            foreach (Document document in application.Documents)
            {
                if (document.DocumentType != DocumentTypeEnum.kDrawingDocumentObject || string.IsNullOrEmpty(document.FullFileName))
                {
                    continue;
                }

                foreach (Document referenced in document.ReferencedDocuments)
                {
                    if (string.Equals(referenced.FullFileName, assemblyFileName, StringComparison.OrdinalIgnoreCase))
                    {
                        return document.FullFileName;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Infrastructure.Log.Error("DrawingAccess.FindOpenDrawingOf", ex);
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
