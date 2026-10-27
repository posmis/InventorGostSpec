using System.Text.RegularExpressions;

namespace InventorGostSpec.Core.Sorting;

/// <summary>
/// Последовательность документов в разделе «Документация» по ГОСТ 2.102
/// (код документа — буквенная часть в конце обозначения, например «СБ» в «АБВГ.301234.001СБ»).
/// </summary>
public static class DocumentCodes
{
    private static readonly string[] BeforeSchemes = { "", "СБ", "ВО", "ТЧ", "ГЧ", "МЭ", "МЧ", "УЧ" };

    private static readonly string[] AfterSchemes =
    {
        "ВС", "ВД", "ВП", "ВИ", "ДП", "ПТ", "ЭП", "ТП", "ПЗ", "ТУ", "ПМ", "ТБ", "РР",
        // Эксплуатационные документы (ГОСТ 2.601).
        "ВЭ", "РЭ", "ИМ", "ФО", "ПС", "ЭТ", "КИ", "ЗИ", "УП", "ИС",
        // Ремонтные документы (ГОСТ 2.602).
        "РС", "РК", "ВРД",
    };

    private static readonly Regex CodeAtEnd = new(@"\d\s*(?<code>[А-Я]{1,3}\d?)\s*$", RegexOptions.CultureInvariant);
    private static readonly Regex Scheme = new(@"^[ЭГПХКВЛРЕС]\d$", RegexOptions.CultureInvariant);
    private static readonly Regex Instruction = new(@"^И\d$", RegexOptions.CultureInvariant);
    private static readonly Regex OtherDocument = new(@"^Д\d$", RegexOptions.CultureInvariant);

    /// <summary>Код документа из обозначения. Пустая строка — код отсутствует.</summary>
    public static string ExtractCode(string? designation)
    {
        if (string.IsNullOrWhiteSpace(designation))
        {
            return "";
        }

        var match = CodeAtEnd.Match(NaturalComparer.Normalize(designation));
        return match.Success ? match.Groups["code"].Value : "";
    }

    /// <summary>Ранг документа в последовательности ГОСТ 2.102. Меньше — раньше.</summary>
    public static int Rank(string code)
    {
        var index = Array.IndexOf(BeforeSchemes, code);
        if (index >= 0)
        {
            return index;
        }

        if (OtherDocument.IsMatch(code))
        {
            return 300;
        }

        if (Scheme.IsMatch(code))
        {
            return 100;
        }

        index = Array.IndexOf(AfterSchemes, code);
        if (index >= 0)
        {
            return 200 + index;
        }

        if (Instruction.IsMatch(code))
        {
            return 290;
        }

        return 400;
    }

    public static int Compare(string? designationX, string? designationY)
    {
        var codeX = ExtractCode(designationX);
        var codeY = ExtractCode(designationY);
        var result = Rank(codeX).CompareTo(Rank(codeY));
        return result != 0 ? result : NaturalComparer.Instance.Compare(codeX, codeY);
    }
}
