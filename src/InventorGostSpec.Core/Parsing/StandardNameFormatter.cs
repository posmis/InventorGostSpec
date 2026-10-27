using System.Text.RegularExpressions;

namespace InventorGostSpec.Core.Parsing;

/// <summary>Наименование стандартного изделия, собранное из имени файла.</summary>
/// <param name="Text">Наименование для спецификации: «Болт М12х60 ГОСТ 7798-70».</param>
/// <param name="Parts">Части наименования (для полей сортировки).</param>
/// <param name="IsComplete">Найдены и наименование изделия, и обозначение стандарта.</param>
public sealed record StandardName(string Text, StandardDesignation Parts, bool IsComplete);

/// <summary>
/// Приведение наименования стандартного изделия к виду, принятому в спецификации по ГОСТ 2.106:
/// «наименование, параметры, обозначение стандарта» — «Болт М12х60 ГОСТ 7798-70».
/// Источник — имя файла Content Center, в котором части могут идти в другом порядке
/// и с другими разделителями («ГОСТ 7798-70 - Болт M12 x 60»).
/// </summary>
public static class StandardNameFormatter
{
    private const string Modifiers = @"(?:\s*(?:Р|ИСО|ISO|МЭК|IEC|EN|ЕН)(?![\p{L}]))*";

    // Обозначение стандарта с номером. У ОСТ номер из двух частей: «ОСТ 1 31100-80».
    private static readonly Regex Standard = new(
        @"(?<![\p{L}\d])(?:ОСТ\s*\d+\s+\d+(?:[.\-–]\d+)*|(?:ГОСТ|СТО|СТП|ISO|ИСО|DIN|EN|ТУ)" + Modifiers + @"\s*\d+(?:[.\-–]\d+)*)",
        RegexOptions.CultureInvariant);

    // «x», «X», «×», «*» между числами → кириллическая «х» без пробелов: «М12 x 60» → «М12х60».
    private static readonly Regex Multiply = new(@"(?<=\d)\s*[xXхХ×*]\s*(?=\d)", RegexOptions.CultureInvariant);

    // Латинская M перед числом (резьба) → кириллическая М.
    private static readonly Regex LatinThread = new(@"(?<![\p{L}])M(?=\d)", RegexOptions.CultureInvariant);

    private static readonly Regex Separators = new(@"(?<=^|\s)[-–—_,;]+(?=\s|$)", RegexOptions.CultureInvariant);

    /// <summary>Наименование из имени файла (без расширения).</summary>
    /// <param name="fallbackName">
    /// Наименование изделия («Болт»), если его нет в имени файла, — например, из текущего Description.
    /// </param>
    public static StandardName FromFileName(string fileName, string? fallbackName = null)
    {
        var text = Clean(fileName.Replace('_', ' '));

        var standard = "";
        var match = Standard.Match(text);
        if (match.Success)
        {
            standard = NormalizeStandard(match.Value);
            text = Clean(text.Remove(match.Index, match.Length));
        }

        text = Separators.Replace(text, " ");
        text = Clean(LatinThread.Replace(Multiply.Replace(text, "х"), "М"));

        var nameTokens = new List<string>();
        var parameterTokens = new List<string>();
        foreach (var token in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            // Слова наименования идут до первого параметра; всё после — параметры.
            if (parameterTokens.Count == 0 && IsNameToken(token))
            {
                nameTokens.Add(token);
            }
            else
            {
                parameterTokens.Add(token);
            }
        }

        var name = string.Join(' ', nameTokens);
        if (name.Length == 0 && !string.IsNullOrWhiteSpace(fallbackName))
        {
            name = Clean(fallbackName);
        }

        if (name.Length > 0)
        {
            name = char.ToUpper(name[0]) + name[1..];
        }

        var parameters = string.Join(' ', parameterTokens);
        var parts = new StandardDesignation(name, parameters, standard);
        var result = string.Join(' ', new[] { name, parameters, standard }.Where(s => s.Length > 0));
        return new StandardName(result, parts, name.Length > 0 && standard.Length > 0);
    }

    /// <summary>
    /// Наименование уже в нужном виде: есть наименование изделия и стандарт в конце,
    /// и приведение не меняет его.
    /// </summary>
    public static bool IsWellFormed(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return false;
        }

        var formatted = FromFileName(description);
        return formatted.IsComplete && formatted.Text == Clean(description);
    }

    private static string NormalizeStandard(string standard)
    {
        standard = Clean(standard.Replace('–', '-'));
        // «ГОСТ7798-70» → «ГОСТ 7798-70».
        return Regex.Replace(standard, @"(?<=\p{L})(?=\d)", " ");
    }

    private static bool IsNameToken(string token)
    {
        if (!token.All(c => char.IsLetter(c) || c == '-'))
        {
            return false;
        }

        // Одиночная заглавная буква — обычно исполнение («Шайба А 12»), одиночная строчная — предлог.
        return token.Length > 1 || char.IsLower(token[0]);
    }

    private static string Clean(string text) => Regex.Replace(text.Trim(), @"\s+", " ");
}
