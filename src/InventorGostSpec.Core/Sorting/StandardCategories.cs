using System.Text.RegularExpressions;

namespace InventorGostSpec.Core.Sorting;

/// <summary>Категории стандартов в последовательности записи стандартных изделий.</summary>
public enum StandardCategory
{
    /// <summary>Зарубежные стандарты (DIN, ISO, EN) — записываются первыми.</summary>
    Foreign = 0,

    /// <summary>Межгосударственные стандарты (ГОСТ, ГОСТ ISO).</summary>
    Interstate = 1,

    /// <summary>Национальные стандарты (ГОСТ Р).</summary>
    National = 2,

    /// <summary>Отраслевые стандарты (ОСТ).</summary>
    Industry = 3,

    /// <summary>Стандарты организаций (СТО, СТП).</summary>
    Organization = 4,

    /// <summary>Прочие (ТУ и т. д.).</summary>
    Other = 5,

    /// <summary>Стандарт не указан.</summary>
    None = 6,
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

    private static readonly Regex Foreign = new(
        $@"^({string.Join("|", new[] { "DIN", "ISO", "ИСО", "EN" }.Select(NaturalComparer.Normalize).Distinct())})(?!\p{{L}})",
        RegexOptions.CultureInvariant);

    public static StandardCategory Categorize(string? standardNumber)
    {
        if (string.IsNullOrWhiteSpace(standardNumber))
        {
            return StandardCategory.None;
        }

        var normalized = NaturalComparer.Normalize(standardNumber);
        if (Foreign.IsMatch(normalized))
        {
            return StandardCategory.Foreign;
        }

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
