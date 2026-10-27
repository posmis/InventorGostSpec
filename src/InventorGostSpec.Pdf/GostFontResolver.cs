using PdfSharp.Fonts;

namespace InventorGostSpec.Pdf;

/// <summary>
/// Отдаёт PDFsharp шрифты из файлов: имя семейства в XFont — путь к файлу .ttf.
/// Устанавливается один раз на процесс.
/// </summary>
internal sealed class GostFontResolver : IFontResolver
{
    private static readonly object Lock = new();
    private static GostFontResolver? _instance;
    private readonly Dictionary<string, byte[]> _fonts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Имя семейства для XFont: путь к файлу. Если файл уже зарегистрирован — то же имя.</summary>
    public static string Register(string path)
    {
        lock (Lock)
        {
            if (_instance is null)
            {
                _instance = new GostFontResolver();
                GlobalFontSettings.FontResolver = _instance;
            }

            var key = Path.GetFullPath(path);
            if (!_instance._fonts.ContainsKey(key))
            {
                _instance._fonts[key] = File.ReadAllBytes(key);
            }

            return key;
        }
    }

    // Наклон по запросу имитируется: у шрифтов ГОСТ обычно нет отдельного курсивного файла.
    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
        _fonts.ContainsKey(familyName) ? new FontResolverInfo(familyName, false, isItalic) : null;

    public byte[]? GetFont(string faceName)
    {
        lock (Lock)
        {
            return _fonts.TryGetValue(faceName, out var bytes) ? bytes : null;
        }
    }
}
