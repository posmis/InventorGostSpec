using InventorGostSpec.AddIn.Infrastructure;
using InventorGostSpec.Core.Model;

namespace InventorGostSpec.AddIn.InventorApi;

/// <summary>
/// Поиск чертежей моделей, как у команды Inventor «Открыть чертёж»: по всей рабочей папке активного проекта
/// (и по папкам моделей), сначала по имени файла, затем — по содержимому чертежа (какие модели в нём).
/// Содержимое чертежей запоминается до закрытия Inventor (пока файл не изменился).
/// </summary>
internal sealed class DrawingSearch
{
    /// <summary>Сколько чертежей можно открыть за один поиск, чтобы прочитать, какие модели в них.</summary>
    private const int MaxOpened = 400;

    private const int MaxFiles = 30_000;

    private static readonly Dictionary<string, (DateTime Stamp, IReadOnlyList<string> Models)> ContentCache =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Inventor.Application _application;
    private readonly List<string> _files;
    private readonly string _scope;
    private Dictionary<string, IReadOnlyList<string>>? _contents;
    private int _opened;

    /// <param name="modelFolders">Папки моделей: ищутся и вне рабочей папки проекта.</param>
    public DrawingSearch(Inventor.Application application, IEnumerable<string> modelFolders)
    {
        _application = application;
        var roots = new List<string>();
        var workspace = Workspace(application);
        if (workspace is not null)
        {
            roots.Add(workspace);
        }

        roots.AddRange(modelFolders.Where(f => f.Length > 0));
        _scope = string.Join("; ", roots.Distinct(StringComparer.OrdinalIgnoreCase));
        _files = roots
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .SelectMany(EnumerateDrawings)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxFiles)
            .ToList();
    }

    /// <summary>Чертёж модели или <c>null</c>.</summary>
    public string? Find(string modelFile, string partNumber, bool isAssembly)
    {
        var byName = DrawingFiles.BestByName(_files, modelFile, partNumber);
        if (byName is not null)
        {
            Log.Info("Чертёж", $"{modelFile}: {byName} (по имени)");
            return byName;
        }

        var byContent = DrawingFiles.BestByContent(Contents(), modelFile, isAssembly);
        Log.Info("Чертёж", byContent is not null
            ? $"{modelFile}: {byContent} (по содержимому)"
            : $"{modelFile}: не найден. Папки: {_scope}; чертежей: {_files.Count}, открыто для проверки: {_opened}");
        return byContent;
    }

    /// <summary>Какие модели в каждом чертеже: из памяти Inventor, из кэша или открыв чертёж невидимо.</summary>
    private Dictionary<string, IReadOnlyList<string>> Contents()
    {
        if (_contents is not null)
        {
            return _contents;
        }

        _contents = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var open = OpenDrawings();
        var index = 0;
        foreach (var file in _files.Where(f => string.Equals(System.IO.Path.GetExtension(f), ".idw", StringComparison.OrdinalIgnoreCase)
                                               || open.ContainsKey(f)))
        {
            index++;
            DateTime stamp;
            try
            {
                stamp = System.IO.File.GetLastWriteTimeUtc(file);
            }
            catch (Exception)
            {
                continue;
            }

            if (open.TryGetValue(file, out var models) ||
                (ContentCache.TryGetValue(file, out var cached) && cached.Stamp == stamp && (models = cached.Models) is not null))
            {
                _contents[file] = models;
                continue;
            }

            if (_opened >= MaxOpened)
            {
                continue;
            }

            SetStatus($"Поиск чертежей: {index} из {_files.Count}");
            models = ReadModels(file);
            _opened++;
            ContentCache[file] = (stamp, models);
            _contents[file] = models;
        }

        SetStatus("");
        return _contents;
    }

    private IReadOnlyList<string> ReadModels(string file)
    {
        Document? document = null;
        try
        {
            document = _application.Documents.Open(file, false);
            return References(document);
        }
        catch (Exception ex)
        {
            Log.Error("DrawingSearch " + file, ex);
            return Array.Empty<string>();
        }
        finally
        {
            try
            {
                document?.Close(true);
            }
            catch (Exception)
            {
                // Чертёж мог быть открыт пользователем за это время — не закрываем принудительно.
            }
        }
    }

    private Dictionary<string, IReadOnlyList<string>> OpenDrawings()
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (Document document in _application.Documents)
            {
                if (document.DocumentType == DocumentTypeEnum.kDrawingDocumentObject && !string.IsNullOrEmpty(document.FullFileName))
                {
                    result[document.FullFileName] = References(document);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("DrawingSearch.OpenDrawings", ex);
        }

        return result;
    }

    private static IReadOnlyList<string> References(Document document)
    {
        var models = new List<string>();
        foreach (Document referenced in document.ReferencedDocuments)
        {
            models.Add(referenced.FullFileName);
        }

        return models;
    }

    private void SetStatus(string text)
    {
        try
        {
            _application.StatusBarText = text;
        }
        catch (Exception)
        {
            // Строка состояния — только для удобства.
        }
    }

    private static string? Workspace(Inventor.Application application)
    {
        try
        {
            var project = application.DesignProjectManager.ActiveDesignProject;
            var path = project.WorkspacePath;
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            // Путь рабочей папки может быть задан относительно файла проекта.
            if (!System.IO.Path.IsPathRooted(path))
            {
                path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(project.FullFileName) ?? "", path);
            }

            return System.IO.Directory.Exists(path) ? System.IO.Path.GetFullPath(path) : null;
        }
        catch (Exception ex)
        {
            Log.Error("DrawingSearch.Workspace", ex);
            return null;
        }
    }

    private static IEnumerable<string> EnumerateDrawings(string root)
    {
        try
        {
            return System.IO.Directory
                .EnumerateFiles(root, "*.*", new System.IO.EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                .Where(DrawingFiles.IsDrawing)
                .Where(f => !f.Contains($"{System.IO.Path.DirectorySeparatorChar}OldVersions{System.IO.Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or ArgumentException)
        {
            Log.Error("DrawingSearch " + root, ex);
            return Array.Empty<string>();
        }
    }
}
