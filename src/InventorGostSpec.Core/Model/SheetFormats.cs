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

    private static bool Near(double value, double expected) => Math.Abs(value - expected) <= ToleranceMm;
}
