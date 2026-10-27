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

    /// <summary>Наименования документов по коду (ГОСТ 2.102, 2.601, 2.602) — графа «Наименование» в разделе «Документация».</summary>
    private static readonly Dictionary<string, string> Names = new()
    {
        ["СБ"] = "Сборочный чертеж",
        ["ВО"] = "Чертеж общего вида",
        ["ТЧ"] = "Теоретический чертеж",
        ["ГЧ"] = "Габаритный чертеж",
        ["МЭ"] = "Электромонтажный чертеж",
        ["МЧ"] = "Монтажный чертеж",
        ["УЧ"] = "Упаковочный чертеж",
        ["ВС"] = "Ведомость спецификаций",
        ["ВД"] = "Ведомость ссылочных документов",
        ["ВП"] = "Ведомость покупных изделий",
        ["ВИ"] = "Ведомость разрешения применения покупных изделий",
        ["ДП"] = "Ведомость держателей подлинников",
        ["ПТ"] = "Ведомость технического предложения",
        ["ЭП"] = "Ведомость эскизного проекта",
        ["ТП"] = "Ведомость технического проекта",
        ["ПЗ"] = "Пояснительная записка",
        ["ТУ"] = "Технические условия",
        ["ПМ"] = "Программа и методика испытаний",
        ["ТБ"] = "Таблицы",
        ["РР"] = "Расчеты",
        ["ВЭ"] = "Ведомость эксплуатационных документов",
        ["РЭ"] = "Руководство по эксплуатации",
        ["ИМ"] = "Инструкция по монтажу, пуску, регулированию и обкатке изделия",
        ["ФО"] = "Формуляр",
        ["ПС"] = "Паспорт",
        ["ЭТ"] = "Этикетка",
        ["КИ"] = "Каталог изделия",
        ["ЗИ"] = "Ведомость ЗИП",
        ["РС"] = "Руководство по ремонту",
        ["ВРД"] = "Ведомость документов для ремонта",
    };

    private static readonly Dictionary<char, string> SchemeKinds = new()
    {
        ['Э'] = "электрическая", ['Г'] = "гидравлическая", ['П'] = "пневматическая", ['Х'] = "газовая",
        ['К'] = "кинематическая", ['В'] = "вакуумная", ['Л'] = "оптическая", ['Р'] = "энергетическая",
        ['Е'] = "деления", ['С'] = "комбинированная",
    };

    private static readonly string[] SchemeTypes =
    {
        "объединенная", "структурная", "функциональная", "принципиальная", "соединений", "подключения", "общая", "расположения",
    };

    /// <summary>Частые документы — для меню «Тип документа».</summary>
    public static readonly IReadOnlyList<string> Common = new[]
    {
        "СБ", "ВО", "ГЧ", "МЭ", "МЧ", "УЧ", "Э3", "Э4", "Э5", "ПЭ3", "ВП", "ПЗ", "ТУ", "ПМ", "РЭ", "ПС", "ФО", "ЭТ",
    };

    /// <summary>
    /// Наименование документа по коду: «СБ» — «Сборочный чертеж», «Э3» — «Схема электрическая принципиальная»,
    /// «ПЭ3» — «Перечень элементов». Пустая строка — код неизвестен (наименование вписывается вручную).
    /// </summary>
    public static string NameOf(string? code)
    {
        code = NaturalComparer.Normalize(code ?? "");
        if (Names.TryGetValue(code, out var name))
        {
            return name;
        }

        if (code.Length == 3 && code[0] == 'П' && Scheme.IsMatch(code[1..]))
        {
            return "Перечень элементов";
        }

        if (Scheme.IsMatch(code) && SchemeKinds.TryGetValue(code[0], out var kind) && code[1] - '0' < SchemeTypes.Length)
        {
            return $"Схема {kind} {SchemeTypes[code[1] - '0']}";
        }

        return "";
    }

    /// <summary>Наименование документа по коду в конце обозначения («АБВГ.301234.001СБ» → «Сборочный чертеж»).</summary>
    public static string NameOfDesignation(string? designation) => NameOf(ExtractCode(designation));

    /// <summary>Обозначение с другим кодом документа: прежний код в конце заменяется («…001СБ» + «ТУ» → «…001ТУ»).</summary>
    public static string WithCode(string? designation, string code)
    {
        var text = (designation ?? "").Trim();
        var current = ExtractCode(text);
        if (current.Length > 0)
        {
            text = text[..^current.Length].TrimEnd();
        }

        return text + code.Trim();
    }

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
