using System.Text.RegularExpressions;

namespace InventorGostSpec.Core.Model;

/// <summary>
/// Выбор чертежа модели по имени файла среди файлов проекта: чем ближе имя и папка к модели, тем лучше.
/// Если по имени не нашлось, чертёж ищется по содержимому (какие модели в нём) — это делает аддин.
/// </summary>
public static class DrawingFiles
{
    public static readonly IReadOnlyList<string> Extensions = new[] { ".idw", ".dwg" };

    public static bool IsDrawing(string path) =>
        Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Насколько файл подходит как чертёж модели (меньше — лучше), <c>null</c> — не подходит:
    /// 0–1 — то же имя, что у модели (в её папке / в другой папке проекта);
    /// 2–3 — имя по обозначению: «АБВГ.301234.001», «…001СБ», «…001 СБ»;
    /// 4–5 — имя начинается с обозначения («АБВГ.301234.001СБ Привод») или с имени модели и кода «СБ» («Привод СБ»).
    /// </summary>
    public static int? Rank(string drawingFile, string modelFile, string? partNumber)
    {
        if (!IsDrawing(drawingFile))
        {
            return null;
        }

        var name = Path.GetFileNameWithoutExtension(drawingFile).Trim();
        var modelName = Path.GetFileNameWithoutExtension(modelFile).Trim();
        var designation = Designations.WithoutAssemblyDrawingCode(partNumber);
        var sameFolder = string.Equals(
            Path.GetFullPath(Path.GetDirectoryName(drawingFile) ?? ""),
            Path.GetFullPath(Path.GetDirectoryName(modelFile) ?? ""),
            StringComparison.OrdinalIgnoreCase) ? 0 : 1;

        if (string.Equals(name, modelName, StringComparison.OrdinalIgnoreCase))
        {
            return 0 + sameFolder;
        }

        if (designation.Length > 0 &&
            Regex.IsMatch(name, "^" + Regex.Escape(designation) + @"(\s?[СC]Б)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return 2 + sameFolder;
        }

        if (Designations.IsFileOfDocument(name, designation) ||
            (modelName.Length > 0 && name.StartsWith(modelName, StringComparison.OrdinalIgnoreCase) &&
             Regex.IsMatch(name[modelName.Length..], @"^[ _]?[СC]Б", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
        {
            return 4 + sameFolder;
        }

        return null;
    }

    /// <summary>Лучший по имени чертёж модели среди файлов; при равенстве — с кодом «СБ», затем по пути.</summary>
    public static string? BestByName(IEnumerable<string> drawingFiles, string modelFile, string? partNumber) =>
        drawingFiles
            .Select(f => (File: f, Rank: Rank(f, modelFile, partNumber)))
            .Where(x => x.Rank is not null)
            .OrderBy(x => x.Rank)
            .ThenByDescending(x => Regex.IsMatch(Path.GetFileNameWithoutExtension(x.File), "[СC]Б", RegexOptions.IgnoreCase))
            .ThenBy(x => x.File, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.File)
            .FirstOrDefault();

    /// <summary>
    /// Чертёж модели по содержимому: из чертежей, в которых есть виды модели, — тот, где других моделей нет
    /// (для детали не подходит сборочный чертёж с её листом). Если такого нет, а модель — сборка, — чертёж,
    /// где она есть вместе с другими моделями (сборочный чертёж с листами деталей).
    /// </summary>
    /// <param name="references">Чертёж → модели, на которые в нём есть виды.</param>
    public static string? BestByContent(IReadOnlyDictionary<string, IReadOnlyList<string>> references, string modelFile, bool isAssembly)
    {
        var withModel = references
            .Where(r => r.Value.Contains(modelFile, StringComparer.OrdinalIgnoreCase))
            .OrderBy(r => Rank(r.Key, modelFile, null) ?? 9)
            .ThenBy(r => r.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var only = withModel.FirstOrDefault(r => r.Value.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1).Key;
        return only ?? (isAssembly ? withModel.FirstOrDefault().Key : null);
    }
}
