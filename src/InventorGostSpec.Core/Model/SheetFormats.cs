namespace InventorGostSpec.Core.Model;

/// <summary>Обозначение формата листа по ГОСТ 2.301 по его размерам.</summary>
public static class SheetFormats
{
    private const double A4Short = 210;
    private const double A4Long = 297;
    private const double ToleranceMm = 2;

    private static readonly (string Name, double Short, double Long)[] Basic =
    {
        ("А0", 841, 1189),
        ("А1", 594, 841),
        ("А2", 420, 594),
        ("А3", 297, 420),
        ("А4", 210, 297),
        ("А5", 148, 210),
    };

    /// <summary>
    /// Возвращает «А4», «А3», ... или дополнительный формат («А4х3», «А3х4»).
    /// Пустая строка — размер не соответствует ни одному формату.
    /// </summary>
    public static string FromSize(double widthMm, double heightMm)
    {
        var shortSide = Math.Min(widthMm, heightMm);
        var longSide = Math.Max(widthMm, heightMm);

        foreach (var (name, s, l) in Basic)
        {
            if (Near(shortSide, s) && Near(longSide, l))
            {
                return name;
            }
        }

        // Дополнительные форматы: короткая сторона основного формата, длинная кратна его длинной стороне.
        foreach (var (name, side, step) in new[] { ("А4", A4Long, A4Short), ("А3", 420.0, 297.0), ("А2", 594.0, 420.0), ("А1", 841.0, 594.0), ("А0", 1189.0, 841.0) })
        {
            if (!Near(shortSide, side))
            {
                continue;
            }

            var multiple = Math.Round(longSide / step);
            if (multiple >= 2 && Near(longSide, multiple * step))
            {
                return $"{name}х{multiple:0}";
            }
        }

        return "";
    }

    /// <summary>Деталь без чертежа (ГОСТ 2.106): в графе «Формат» пишут «БЧ».</summary>
    public const string NoDrawing = "БЧ";

    /// <summary>Сноска графы «Формат» (ГОСТ 2.106): формат не помещается в графу и записан в «Примечании».</summary>
    public const string Footnote = "*)";

    /// <summary>
    /// Форматы листов документа одной строкой: без повторов, в порядке уменьшения — по основному формату от большего
    /// к меньшему, у одного основного дополнительный формат раньше («А1, А2х3, А2, А3»). Нераспознанные значения — в конце.
    /// </summary>
    public static string Combine(IEnumerable<string> formats)
    {
        var distinct = formats
            .SelectMany(f => (f ?? "").Split(','))
            .Select(Normalize)
            .Where(f => f.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return string.Join(", ", distinct
            .Select((f, index) => (Format: f, Parsed: Parse(f), Index: index))
            .OrderBy(x => x.Parsed?.Base ?? int.MaxValue)
            .ThenByDescending(x => x.Parsed?.Multiple ?? 0)
            .ThenBy(x => x.Index)
            .Select(x => x.Format));
    }

    /// <summary>
    /// Формат не помещается в графу «Формат» по ГОСТ: дополнительный («А4х3») или несколько форматов («А1, А2»).
    /// Одиночный основной формат («А1», «А4») помещается.
    /// </summary>
    public static bool NeedsFootnote(string? format)
    {
        format = (format ?? "").Trim();
        return format.Length > 0 && format != Footnote &&
               (format.Contains(',') || Parse(format) is { Multiple: > 1 });
    }

    /// <summary>
    /// Значения граф «Формат» и «Примечание» для вывода: если формат не помещается — в графе «Формат» «*)»,
    /// а в «Примечании» «*)А4х3», прежнее примечание — со следующей строки.
    /// </summary>
    public static (string Format, string Note) ForColumns(string? format, string? note)
    {
        format = (format ?? "").Trim();
        note = (note ?? "").Trim();
        if (!NeedsFootnote(format))
        {
            return (format, note);
        }

        var footnote = Footnote + format;
        return (Footnote, note.Length > 0 ? footnote + "\n" + note : footnote);
    }

    /// <summary>«A2x3», «а2 × 3» → «А2х3» (кириллица, как в ГОСТ).</summary>
    private static string Normalize(string format)
    {
        var text = format.Trim().Replace(" ", "");
        return Parse(text) is { } parsed
            ? parsed.Multiple > 1 ? $"А{parsed.Base}х{parsed.Multiple}" : $"А{parsed.Base}"
            : format.Trim();
    }

    /// <summary>Номер основного формата (0 — А0) и кратность (1 — основной формат).</summary>
    private static (int Base, int Multiple)? Parse(string format)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            format.Trim().Replace(" ", ""), @"^[АAаa](\d)(?:[хxХX×*](\d+))?$");
        if (!match.Success)
        {
            return null;
        }

        var multiple = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 1;
        return (int.Parse(match.Groups[1].Value), multiple);
    }

    private static bool Near(double value, double expected) => Math.Abs(value - expected) <= ToleranceMm;
}
