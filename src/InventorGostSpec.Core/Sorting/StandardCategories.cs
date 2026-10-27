using System.Text.RegularExpressions;

namespace InventorGostSpec.Core.Sorting;

/// <summary>Категории стандартов в последовательности записи стандартных изделий.</summary>
public enum StandardCategory
{
    /// <summary>Межгосударственные стандарты (ГОСТ, ГОСТ ISO).</summary>
    Interstate = 0,

    /// <summary>Национальные стандарты (ГОСТ Р).</summary>
    National = 1,

    /// <summary>Отраслевые стандарты (ОСТ).</summary>
    Industry = 2,

    /// <summary>Стандарты организаций (СТО, СТП).</summary>
    Organization = 3,

    /// <summary>Прочие (ISO, DIN и т. д.).</summary>
    Other = 4,

    /// <summary>Стандарт не указан.</summary>
    None = 5,
}

public static class StandardCategories
{
    // Сравнение идёт по нормализованной строке, где латинские двойники уже заменены кириллицей,
    // поэтому и модификаторы (ISO, IEC, EN) нормализуются так же.
    private static readonly string Modifiers = string.Join(
        "|",
        new[] { "ИСО", "ISO", "МЭК", "IEC", "ЕН", "EN" }.Select(NaturalComparer.Normalize).Distinct());

    private static readonly Regex Prefix = new(
        $@"^(?<prefix>ГОСТ\s*Р(?!\p{{L}})(\s*({Modifiers})(?!\p{{L}}))*|ГОСТ(?!\p{{L}})(\s*({Modifiers})(?!\p{{L}}))*|ОСТ(?!\p{{L}})|СТО(?!\p{{L}})|СТП(?!\p{{L}}))\s*",
        RegexOptions.CultureInvariant);

    private static readonly Regex National = new(@"^ГОСТ\s*Р(?!\p{L})", RegexOptions.CultureInvariant);

    public static StandardCategory Categorize(string? standardNumber)
    {
        if (string.IsNullOrWhiteSpace(standardNumber))
        {
            return StandardCategory.None;
        }

        var normalized = NaturalComparer.Normalize(standardNumber);
        var match = Prefix.Match(normalized);
        if (!match.Success)
        {
            return StandardCategory.Other;
        }

        var prefix = match.Groups["prefix"].Value;
        if (prefix.StartsWith("ГОСТ", StringComparison.Ordinal))
        {
            return National.IsMatch(prefix) ? StandardCategory.National : StandardCategory.Interstate;
        }

        return prefix.StartsWith("ОСТ", StringComparison.Ordinal)
            ? StandardCategory.Industry
            : StandardCategory.Organization;
    }

    /// <summary>Номер стандарта без префикса категории: «ГОСТ Р 52627-2006» → «52627-2006».</summary>
    public static string StripPrefix(string? standardNumber)
    {
        if (string.IsNullOrWhiteSpace(standardNumber))
        {
            return "";
        }

        var normalized = NaturalComparer.Normalize(standardNumber);
        var match = Prefix.Match(normalized);
        return match.Success ? normalized[match.Length..] : normalized;
    }
}
