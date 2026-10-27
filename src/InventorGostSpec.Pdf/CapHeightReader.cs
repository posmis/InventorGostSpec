namespace InventorGostSpec.Pdf;

/// <summary>
/// Настоящая высота прописных букв шрифта TrueType — по контуру буквы «Н» (или «H»): таблицы cmap, loca, glyf.
/// Значение CapHeight из метрик шрифта бывает неверным (у «Gost merged» оно равно Ascent),
/// и тогда текст получается мельче, чем задано по ГОСТ 2.304.
/// </summary>
internal static class CapHeightReader
{
    /// <summary>Высота прописных в долях em; <c>null</c> — не удалось прочитать.</summary>
    public static double? Read(string path)
    {
        try
        {
            var data = File.ReadAllBytes(path);
            var tables = ReadTables(data);
            if (!tables.TryGetValue("head", out var head) || !tables.TryGetValue("cmap", out var cmap)
                || !tables.TryGetValue("loca", out var loca) || !tables.TryGetValue("glyf", out var glyf))
            {
                return null;
            }

            var unitsPerEm = U16(data, head + 18);
            var longOffsets = U16(data, head + 50) == 1;
            foreach (var letter in new[] { 'Н', 'H' })
            {
                var glyph = GlyphIndex(data, cmap, letter);
                if (glyph == 0)
                {
                    continue;
                }

                var start = longOffsets ? (int)U32(data, loca + 4 * glyph) : 2 * U16(data, loca + 2 * glyph);
                var end = longOffsets ? (int)U32(data, loca + 4 * (glyph + 1)) : 2 * U16(data, loca + 2 * (glyph + 1));
                if (end <= start || unitsPerEm == 0)
                {
                    continue;
                }

                var yMax = (short)U16(data, glyf + start + 8);
                var yMin = (short)U16(data, glyf + start + 4);
                if (yMax > 0)
                {
                    return (double)(yMax - Math.Max((short)0, yMin)) / unitsPerEm;
                }
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or IndexOutOfRangeException or ArgumentException)
        {
            return null;
        }
    }

    private static Dictionary<string, int> ReadTables(byte[] data)
    {
        var tables = new Dictionary<string, int>();
        var count = U16(data, 4);
        for (var i = 0; i < count; i++)
        {
            var record = 12 + 16 * i;
            tables[System.Text.Encoding.ASCII.GetString(data, record, 4)] = (int)U32(data, record + 8);
        }

        return tables;
    }

    /// <summary>Номер глифа символа по подтаблице cmap формата 4 (Unicode BMP).</summary>
    private static int GlyphIndex(byte[] data, int cmap, char symbol)
    {
        var count = U16(data, cmap + 2);
        for (var i = 0; i < count; i++)
        {
            var platform = U16(data, cmap + 4 + 8 * i);
            var encoding = U16(data, cmap + 6 + 8 * i);
            var offset = cmap + (int)U32(data, cmap + 8 + 8 * i);
            if (!((platform == 3 && encoding is 1 or 10) || platform == 0) || U16(data, offset) != 4)
            {
                continue;
            }

            var segments = U16(data, offset + 6) / 2;
            var ends = offset + 14;
            var starts = ends + 2 * segments + 2;
            var deltas = starts + 2 * segments;
            var ranges = deltas + 2 * segments;
            for (var s = 0; s < segments; s++)
            {
                if (symbol > U16(data, ends + 2 * s))
                {
                    continue;
                }

                if (symbol < U16(data, starts + 2 * s))
                {
                    break;
                }

                var delta = (short)U16(data, deltas + 2 * s);
                var rangeOffset = U16(data, ranges + 2 * s);
                if (rangeOffset == 0)
                {
                    return (symbol + delta) & 0xFFFF;
                }

                var glyph = U16(data, ranges + 2 * s + rangeOffset + 2 * (symbol - U16(data, starts + 2 * s)));
                return glyph == 0 ? 0 : (glyph + delta) & 0xFFFF;
            }
        }

        return 0;
    }

    private static int U16(byte[] data, int offset) => (data[offset] << 8) | data[offset + 1];

    private static uint U32(byte[] data, int offset) => ((uint)U16(data, offset) << 16) | (uint)U16(data, offset + 2);
}
