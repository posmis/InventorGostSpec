using InventorGostSpec.Excel;

namespace InventorGostSpec.Pdf;

public enum LineStyle
{
    Normal,

    /// <summary>Заголовок раздела: по центру графы «Наименование», подчёркнут.</summary>
    Section,

    /// <summary>Заголовок группы «Изделия, устанавливаемые по …»: по центру графы «Наименование».</summary>
    Group,
}

/// <summary>Одна строка таблицы спецификации на листе.</summary>
public sealed class PrintLine
{
    public string Format { get; init; } = "";
    public string Zone { get; init; } = "";
    public string Position { get; init; } = "";
    public string Designation { get; init; } = "";
    public string Name { get; init; } = "";
    public string Quantity { get; init; } = "";
    public string Note { get; init; } = "";
    public LineStyle Style { get; init; }

    public bool IsBlank =>
        Format.Length == 0 && Zone.Length == 0 && Position.Length == 0 && Designation.Length == 0
        && Name.Length == 0 && Quantity.Length == 0 && Note.Length == 0;

    public static PrintLine Blank => new();

    public string this[int column] => column switch
    {
        GostLayout.FormatColumn => Format,
        GostLayout.ZoneColumn => Zone,
        GostLayout.PositionColumn => Position,
        GostLayout.DesignationColumn => Designation,
        GostLayout.NameColumn => Name,
        GostLayout.QuantityColumn => Quantity,
        _ => Note,
    };
}

/// <summary>
/// Раскладка строк файла Excel по листам: перенос длинного текста по словам, свободные строки вокруг заголовков
/// (ГОСТ 2.106: выше заголовка — не менее одной, ниже — одна), заголовок не остаётся внизу листа без строк после него,
/// многострочная запись не разрывается между листами.
/// </summary>
public static class SpecPaginator
{
    /// <param name="measure">Ширина текста в мм при заданной высоте прописных букв.</param>
    public static List<List<PrintLine>> Paginate(IReadOnlyList<SheetRow> rows, Func<string, double, double> measure)
    {
        var blocks = rows.Select(row => ToBlock(row, measure)).ToList();
        var pages = new List<List<PrintLine>> { new() };

        for (var i = 0; i < blocks.Count; i++)
        {
            var page = pages[^1];
            var lines = Trim(blocks[i].Lines, page);
            if (lines.Count == 0)
            {
                continue;
            }

            var capacity = GostLayout.RowsOnPage(pages.Count - 1);
            var needed = lines.Count + KeptLines(blocks, i);
            if (page.Count > 0 && page.Count + Math.Min(needed, capacity) > capacity)
            {
                page = new List<PrintLine>();
                pages.Add(page);
                capacity = GostLayout.RowsOnPage(pages.Count - 1);
                lines = Trim(blocks[i].Lines, page);
            }

            // Запись длиннее листа (редкость) переносится построчно.
            foreach (var line in lines)
            {
                if (page.Count >= capacity)
                {
                    page = new List<PrintLine>();
                    pages.Add(page);
                    capacity = GostLayout.RowsOnPage(pages.Count - 1);
                    if (line.IsBlank)
                    {
                        continue;
                    }
                }

                page.Add(line);
            }
        }

        // Свободные строки в конце листов не нужны (они и так пустые) — убираем, чтобы не плодить листы.
        foreach (var page in pages)
        {
            while (page.Count > 0 && page[^1].IsBlank)
            {
                page.RemoveAt(page.Count - 1);
            }
        }

        while (pages.Count > 1 && pages[^1].Count == 0)
        {
            pages.RemoveAt(pages.Count - 1);
        }

        return pages;
    }

    private sealed record Block(List<PrintLine> Lines, bool KeepWithNext);

    /// <summary>Сколько строк следующих блоков должно встать на тот же лист, что и заголовок.</summary>
    private static int KeptLines(List<Block> blocks, int index)
    {
        var total = 0;
        for (var i = index + 1; i < blocks.Count && blocks[i - 1].KeepWithNext; i++)
        {
            total += blocks[i].Lines.Count;
        }

        return total;
    }

    /// <summary>Свободные строки в начале листа и подряд за другой свободной строкой не повторяются.</summary>
    private static List<PrintLine> Trim(List<PrintLine> lines, List<PrintLine> page)
    {
        var skip = 0;
        var previousBlank = page.Count == 0 || page[^1].IsBlank;
        while (skip < lines.Count && lines[skip].IsBlank && previousBlank)
        {
            skip++;
        }

        return lines.Skip(skip).ToList();
    }

    private static Block ToBlock(SheetRow row, Func<string, double, double> measure)
    {
        var type = row.Type.Length > 0 ? row.Type : row.IsEmpty ? "" : ExcelFormat.RowTypes.Item;
        var nameWidth = GostLayout.TextWidth(GostLayout.NameColumn);
        switch (type)
        {
            case ExcelFormat.RowTypes.Section:
            case ExcelFormat.RowTypes.Group:
            {
                var style = type == ExcelFormat.RowTypes.Section ? LineStyle.Section : LineStyle.Group;
                var lines = new List<PrintLine> { PrintLine.Blank };
                lines.AddRange(Wrap(row.Name, nameWidth, measure).Select(text => new PrintLine { Name = text, Style = style }));
                lines.Add(PrintLine.Blank);
                return new Block(lines, true);
            }

            case ExcelFormat.RowTypes.Text:
                return new Block(Wrap(row.Name, nameWidth, measure).Select(text => new PrintLine { Name = text }).ToList(), false);

            // Резерв — просто свободная строка, номер позиции не печатается.
            case "":
            case ExcelFormat.RowTypes.Reserve:
                return new Block(new List<PrintLine> { PrintLine.Blank }, false);

            default:
            {
                var designation = Wrap(row.Designation, GostLayout.TextWidth(GostLayout.DesignationColumn), measure);
                var name = Wrap(row.Name, nameWidth, measure);
                var note = Wrap(row.Note, GostLayout.TextWidth(GostLayout.NoteColumn), measure);
                var count = Math.Max(1, Math.Max(designation.Count, Math.Max(name.Count, note.Count)));
                var quantity = row.Unit.Length > 0 && row.Quantity.Length > 0 ? $"{row.Quantity} {row.Unit}" : row.Quantity;
                var lines = Enumerable.Range(0, count).Select(i => new PrintLine
                {
                    Format = i == 0 ? row.Format : "",
                    Zone = i == 0 ? row.Zone : "",
                    Position = i == 0 ? row.Position : "",
                    Designation = i < designation.Count ? designation[i] : "",
                    Name = i < name.Count ? name[i] : "",
                    Quantity = i == 0 ? quantity : "",
                    Note = i < note.Count ? note[i] : "",
                }).ToList();
                return new Block(lines, false);
            }
        }
    }

    /// <summary>Насколько можно сжать текст по ширине, чтобы не переносить его (80 %).</summary>
    public const double MinCompression = 0.8;

    /// <summary>
    /// Перенос по словам (и после дефиса). Явный перенос строки в ячейке сохраняется.
    /// Если текст чуть-чуть не помещается и сжатие до 80 % по ширине экономит строку — не переносим, при выводе он сжимается.
    /// Слово длиннее графы остаётся целым и тоже сжимается.
    /// </summary>
    public static List<string> Wrap(string text, double width, Func<string, double, double> measure)
    {
        var normal = WrapAt(text, width, measure);
        var compressed = WrapAt(text, width / MinCompression, measure);
        return compressed.Count < normal.Count ? compressed : normal;
    }

    private static List<string> WrapAt(string text, double width, Func<string, double, double> measure)
    {
        var result = new List<string>();
        foreach (var paragraph in text.Replace("\r", "").Split('\n'))
        {
            var words = SplitWords(paragraph.Trim());
            var line = "";
            foreach (var word in words)
            {
                var candidate = line.Length == 0 ? word.TrimStart() : line + word;
                if (line.Length > 0 && measure(candidate.TrimEnd(), GostLayout.TextHeight) > width)
                {
                    result.Add(line.TrimEnd());
                    line = word.TrimStart();
                }
                else
                {
                    line = candidate;
                }
            }

            if (line.Trim().Length > 0)
            {
                result.Add(line.TrimEnd());
            }
        }

        return result;
    }

    /// <summary>Индексы стандартов: после них не переносим («ГОСТ 7798-70», «ГОСТ Р ИСО 15973-2005» — одной строкой).</summary>
    private static readonly HashSet<string> StandardPrefixes = new(StringComparer.Ordinal)
    {
        "ГОСТ", "Р", "ИСО", "МЭК", "ОСТ", "СТО", "ТУ", "РСТ", "ISO", "IEC", "DIN", "EN", "ASME", "ANSI", "№",
    };

    /// <summary>Слова вместе с пробелом перед ними; после дефиса тоже можно перенести.</summary>
    private static List<string> SplitWords(string text)
    {
        var words = new List<string>();
        var start = 0;
        for (var i = 1; i < text.Length; i++)
        {
            var breakBefore = text[i] == ' ' && text[i - 1] != ' ' && !StandardPrefixes.Contains(text[start..i].Trim().Split(' ')[^1]);
            var breakAfterHyphen = text[i - 1] == '-' && text[i] != ' ' && i >= 2 && char.IsLetter(text[i - 2]);
            if (breakBefore || breakAfterHyphen)
            {
                words.Add(text[start..i]);
                start = i;
            }
        }

        if (start < text.Length)
        {
            words.Add(text[start..]);
        }

        return words;
    }
}
