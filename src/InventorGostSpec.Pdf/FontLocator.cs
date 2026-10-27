using System.Text;

namespace InventorGostSpec.Pdf;

/// <summary>Установленный шрифт TrueType: семейство и файл.</summary>
public sealed record InstalledFont(string Family, string Style, string Path)
{
    public bool IsRegular =>
        Style.Equals("Regular", StringComparison.OrdinalIgnoreCase) || Style.Equals("Normal", StringComparison.OrdinalIgnoreCase)
        || Style.Equals("Обычный", StringComparison.OrdinalIgnoreCase) || Style.Equals("Book", StringComparison.OrdinalIgnoreCase);

    public bool IsItalic =>
        Style.Contains("Italic", StringComparison.OrdinalIgnoreCase) || Style.Contains("Oblique", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Поиск шрифтов TrueType в системных папках по имени семейства (таблица name файла .ttf).
/// PDFsharp не читает шрифты Windows сам, поэтому файл находим здесь.
/// </summary>
public static class FontLocator
{
    /// <summary>Шрифты ГОСТ 2.304 в порядке предпочтения: КОМПАС, AutoCAD, свободные.</summary>
    public static readonly string[] GostFamilies =
    {
        "GOST type B", "GOST type A", "GOST Type BU", "GOST Type AU", "ISOCPEUR", "OpenGost Type B TT", "OpenGost Type A TT",
        "GOST 2.304 type B", "GOST 2.304 type A",
    };

    /// <summary>Запасные шрифты с кириллицей, если шрифта ГОСТ нет.</summary>
    public static readonly string[] FallbackFamilies = { "Arial", "Liberation Sans", "DejaVu Sans", "FreeSans" };

    private static List<InstalledFont>? _cache;

    public static IReadOnlyList<InstalledFont> All => _cache ??= Scan(FontFolders());

    /// <summary>Шрифты ГОСТ, найденные в системе (по одному на семейство).</summary>
    public static IReadOnlyList<string> InstalledGostFamilies() =>
        GostFamilies.Where(f => All.Any(font => font.Family.Equals(f, StringComparison.OrdinalIgnoreCase))).ToList();

    /// <summary>
    /// Файл шрифта: указанное семейство (или путь к .ttf), иначе первый найденный шрифт ГОСТ, иначе запасной.
    /// </summary>
    public static InstalledFont? Find(string? family)
    {
        if (!string.IsNullOrWhiteSpace(family))
        {
            if (File.Exists(family))
            {
                return ReadNames(family) is { } names ? names with { Path = family } : null;
            }

            return FindFamily(family.Trim());
        }

        return GostFamilies.Concat(FallbackFamilies).Select(FindFamily).FirstOrDefault(f => f is not null);
    }

    private static InstalledFont? FindFamily(string family)
    {
        var fonts = All.Where(f => f.Family.Equals(family, StringComparison.OrdinalIgnoreCase)).ToList();
        return fonts.FirstOrDefault(f => f.IsRegular) ?? fonts.FirstOrDefault(f => !f.IsItalic) ?? fonts.FirstOrDefault();
    }

    private static IEnumerable<string> FontFolders()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        if (windows.Length > 0)
        {
            yield return windows;
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(local, "Microsoft", "Windows", "Fonts");

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return "/usr/share/fonts";
        yield return "/usr/local/share/fonts";
        yield return Path.Combine(home, ".fonts");
        yield return Path.Combine(home, ".local", "share", "fonts");
    }

    private static List<InstalledFont> Scan(IEnumerable<string> folders)
    {
        var fonts = new List<InstalledFont>();
        foreach (var folder in folders.Distinct().Where(Directory.Exists))
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(folder, "*.ttf", new EnumerationOptions { RecurseSubdirectories = true, MatchCasing = MatchCasing.CaseInsensitive, IgnoreInaccessible = true });
                foreach (var file in files)
                {
                    if (ReadNames(file) is { } font)
                    {
                        fonts.Add(font);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Недоступная папка шрифтов — просто пропускаем.
            }
        }

        return fonts;
    }

    /// <summary>Семейство и начертание из таблицы name (ID 1 и 2; ID 16/17, если есть). <c>null</c> — не TrueType.</summary>
    public static InstalledFont? ReadNames(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            var version = ReadUInt32(reader);
            if (version != 0x00010000 && version != 0x74727565)
            {
                return null; // .ttc, OpenType CFF и прочее PDFsharp не поддерживает.
            }

            var tables = ReadUInt16(reader);
            stream.Seek(6, SeekOrigin.Current);
            uint nameOffset = 0;
            for (var i = 0; i < tables; i++)
            {
                var tag = Encoding.ASCII.GetString(reader.ReadBytes(4));
                reader.ReadUInt32();
                var offset = ReadUInt32(reader);
                reader.ReadUInt32();
                if (tag == "name")
                {
                    nameOffset = offset;
                }
            }

            if (nameOffset == 0)
            {
                return null;
            }

            stream.Seek(nameOffset, SeekOrigin.Begin);
            reader.ReadUInt16();
            var count = ReadUInt16(reader);
            var storage = nameOffset + ReadUInt16(reader);
            var names = new Dictionary<int, (int Rank, string Value)>();
            for (var i = 0; i < count; i++)
            {
                var platform = ReadUInt16(reader);
                var encoding = ReadUInt16(reader);
                var language = ReadUInt16(reader);
                var id = ReadUInt16(reader);
                var length = ReadUInt16(reader);
                var offset = ReadUInt16(reader);
                if (id is not (1 or 2 or 16 or 17))
                {
                    continue;
                }

                // Предпочитаем английское имя Windows (Unicode), затем любое Unicode, затем Mac Roman.
                var rank = platform == 3 && language == 0x409 ? 0 : platform is 3 or 0 ? 1 : platform == 1 && encoding == 0 ? 2 : -1;
                if (rank < 0 || (names.TryGetValue(id, out var existing) && existing.Rank <= rank))
                {
                    continue;
                }

                var position = stream.Position;
                stream.Seek(storage + offset, SeekOrigin.Begin);
                var bytes = reader.ReadBytes(length);
                stream.Seek(position, SeekOrigin.Begin);
                var value = platform == 1 ? Encoding.Latin1.GetString(bytes) : Encoding.BigEndianUnicode.GetString(bytes);
                names[id] = (rank, value.Trim());
            }

            string? Name(int id) => names.TryGetValue(id, out var name) && name.Value.Length > 0 ? name.Value : null;
            var family = Name(16) ?? Name(1);
            return family is null ? null : new InstalledFont(family, Name(17) ?? Name(2) ?? "Regular", path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EndOfStreamException or ArgumentException)
        {
            return null;
        }
    }

    private static ushort ReadUInt16(BinaryReader reader)
    {
        var bytes = reader.ReadBytes(2);
        if (bytes.Length < 2)
        {
            throw new EndOfStreamException();
        }

        return (ushort)((bytes[0] << 8) | bytes[1]);
    }

    private static uint ReadUInt32(BinaryReader reader) => ((uint)ReadUInt16(reader) << 16) | ReadUInt16(reader);
}
