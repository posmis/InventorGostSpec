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
    /// Чертёж модели (сборки или детали), по порядку:
    /// 1) чертёж с тем же именем, что и модель (Привод.iam → Привод.idw);
    /// 2) чертёж с именем по обозначению модели: «АБВГ.301234.001.idw», «АБВГ.301234.001СБ.idw», «АБВГ.301234.001 СБ.idw»;
    /// 3) открытый в Inventor чертёж только этой модели (чертёж с видами нескольких моделей не подходит);
    /// 4) чертёж в папке модели, имя которого начинается с её обозначения («АБВГ.301234.001СБ Привод.idw»;
    ///    исполнения вида «…-01» не подходят) или с имени модели и кода «СБ» («Привод СБ.idw»).
    /// </summary>
    public static string? FindModelDrawing(Inventor.Application application, string modelFileName, string partNumber)
    {
        if (FindDrawing(modelFileName) is { } sameName)
        {
            return sameName;
        }

        var folder = System.IO.Path.GetDirectoryName(modelFileName) ?? "";
        var modelName = System.IO.Path.GetFileNameWithoutExtension(modelFileName);
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

        if (FindOpenDrawingOf(application, modelFileName) is { } open)
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
                    return Designations.IsFileOfDocument(name, designation) ||
                           (name.StartsWith(modelName, StringComparison.OrdinalIgnoreCase) &&
                            System.Text.RegularExpressions.Regex.IsMatch(name[modelName.Length..], @"^[ _]?[СC]Б",
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase));
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

    /// <summary>Открытый чертёж, все виды которого — этой модели (не сборочный чертёж с листами деталей).</summary>
    private static string? FindOpenDrawingOf(Inventor.Application application, string modelFileName)
    {
        try
        {
            foreach (Document document in application.Documents)
            {
                if (document.DocumentType != DocumentTypeEnum.kDrawingDocumentObject || string.IsNullOrEmpty(document.FullFileName))
                {
                    continue;
                }

                var models = new List<string>();
                foreach (Document referenced in document.ReferencedDocuments)
                {
                    models.Add(referenced.FullFileName);
                }

                if (models.Count == 1 && string.Equals(models[0], modelFileName, StringComparison.OrdinalIgnoreCase))
                {
                    return document.FullFileName;
                }
            }
        }
        catch (Exception ex)
        {
            Infrastructure.Log.Error("DrawingAccess.FindOpenDrawingOf", ex);
        }

        return null;
    }

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
