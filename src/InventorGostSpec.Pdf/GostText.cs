using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace InventorGostSpec.Pdf;

public enum TextAlign
{
    Left,
    Center,
    Right,
}

/// <summary>
/// Текст шрифтом ГОСТ в миллиметрах: размер задаётся высотой прописных букв (ГОСТ 2.304),
/// слишком длинный текст сжимается по ширине, как в КОМПАС.
/// </summary>
internal sealed class GostText
{
    private readonly string _family;
    private readonly XFontStyleEx _style;
    private readonly Dictionary<double, XFont> _fonts = new();
    private readonly XGraphics _measure;
    private readonly double _capRatio;

    public GostText(string family, bool italic)
    {
        _family = family;
        _style = italic ? XFontStyleEx.Italic : XFontStyleEx.Regular;
        _measure = XGraphics.CreateMeasureContext(new XSize(2000, 2000), XGraphicsUnit.Point, XPageDirection.Downwards);

        var metrics = new XFont(family, 100, _style, new XPdfFontOptions(PdfFontEncoding.Unicode)).Metrics;
        _capRatio = metrics.CapHeight > 0 && metrics.UnitsPerEm > 0 ? (double)metrics.CapHeight / metrics.UnitsPerEm : 0.7;
    }

    public XFont Font(double capHeight)
    {
        var em = Math.Round(capHeight / _capRatio, 4);
        if (!_fonts.TryGetValue(em, out var font))
        {
            font = new XFont(_family, em, _style, new XPdfFontOptions(PdfFontEncoding.Unicode));
            _fonts[em] = font;
        }

        return font;
    }

    /// <summary>Ширина текста (мм) при заданной высоте прописных букв (мм).</summary>
    public double Measure(string text, double capHeight) =>
        text.Length == 0 ? 0 : _measure.MeasureString(text, Font(capHeight)).Width;

    /// <summary>Текст на базовой линии; если шире <paramref name="maxWidth"/> — сжимается по ширине. Возвращает ширину.</summary>
    public double Draw(XGraphics gfx, string text, double x, double baseline, double capHeight, double maxWidth, TextAlign align)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        var width = Measure(text, capHeight);
        var scale = width > maxWidth && width > 0 ? maxWidth / width : 1;
        var drawn = width * scale;
        var left = align switch
        {
            TextAlign.Center => x + (maxWidth - drawn) / 2,
            TextAlign.Right => x + maxWidth - drawn,
            _ => x,
        };

        var state = gfx.Save();
        gfx.TranslateTransform(left, baseline);
        gfx.ScaleTransform(scale, 1);
        gfx.DrawString(text, Font(capHeight), XBrushes.Black, 0, 0, XStringFormats.BaseLineLeft);
        gfx.Restore(state);
        return drawn;
    }

    /// <summary>Текст в прямоугольнике: по вертикали по центру, по горизонтали — с отступом <see cref="GostLayout.Padding"/>.</summary>
    public double DrawInBox(XGraphics gfx, string text, double x, double y, double width, double height, double capHeight, TextAlign align)
    {
        var padding = Math.Min(GostLayout.Padding, width / 4);
        return Draw(gfx, text, x + padding, y + (height + capHeight) / 2, capHeight, width - 2 * padding, align);
    }

    /// <summary>Несколько строк по центру прямоугольника (межстрочный интервал — 1,6 высоты букв).</summary>
    public void DrawLines(XGraphics gfx, IReadOnlyList<string> lines, double x, double y, double width, double height, double capHeight, TextAlign align)
    {
        var step = capHeight * 1.6;
        var top = y + (height - (lines.Count - 1) * step - capHeight) / 2;
        for (var i = 0; i < lines.Count; i++)
        {
            var padding = Math.Min(GostLayout.Padding, width / 4);
            Draw(gfx, lines[i], x + padding, top + capHeight + i * step, capHeight, width - 2 * padding, align);
        }
    }

    /// <summary>Текст снизу вверх (повёрнут на 90°) по центру прямоугольника.</summary>
    public void DrawVertical(XGraphics gfx, IReadOnlyList<string> lines, double x, double y, double width, double height, double capHeight)
    {
        var state = gfx.Save();
        gfx.TranslateTransform(x + width / 2, y + height / 2);
        gfx.RotateTransform(-90);
        DrawLines(gfx, lines, -height / 2, -width / 2, height, width, capHeight, TextAlign.Center);
        gfx.Restore(state);
    }
}
