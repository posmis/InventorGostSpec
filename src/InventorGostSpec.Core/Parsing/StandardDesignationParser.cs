using System.Text.RegularExpressions;

namespace InventorGostSpec.Core.Parsing;

/// <summary>Части наименования стандартного изделия.</summary>
public sealed record StandardDesignation(string Name, string Parameters, string StandardNumber);

/// <summary>
/// Разбирает наименование вида «Болт М12х60 ГОСТ 7798-70» на наименование изделия («Болт»),
/// параметры («М12х60») и обозначение стандарта («ГОСТ 7798-70»).
/// </summary>
public static class StandardDesignationParser
{
    private static readonly Regex StandardStart = new(
        @"(?<![\p{L}\d])(ГОСТ|ОСТ|СТО|СТП|ISO|ИСО|DIN|EN|ТУ)(?![\p{L}])",
        RegexOptions.CultureInvariant);

    public static StandardDesignation Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new StandardDesignation("", "", "");
        }

        text = Regex.Replace(text.Trim(), @"\s+", " ");

        var standard = "";
        var match = StandardStart.Match(text);
        if (match.Success)
        {
            standard = text[match.Index..].Trim();
            text = text[..match.Index].Trim();
        }

        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var nameLength = 0;
        while (nameLength < tokens.Length && IsNameToken(tokens[nameLength]))
        {
            nameLength++;
        }

        // Предлоги в конце наименования относятся к параметрам («Шайба с ...» не бывает без продолжения).
        while (nameLength > 0 && IsShortLowercase(tokens[nameLength - 1]))
        {
            nameLength--;
        }

        var name = string.Join(' ', tokens.Take(nameLength));
        var parameters = string.Join(' ', tokens.Skip(nameLength));
        return new StandardDesignation(name, parameters, standard);
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

    private static bool IsShortLowercase(string token) => token.Length == 1 && char.IsLower(token[0]);
}
