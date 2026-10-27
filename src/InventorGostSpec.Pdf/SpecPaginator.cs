using InventorGostSpec.Excel;

namespace InventorGostSpec.Pdf;

public enum LineStyle
{
    Normal,

    /// <summary>Заголовок раздела: по центру графы «Наименование», подчёркнут.</summary>
    Section,

    /// <summary>Заголовок группы «Изделия, устанавливаемые по …»: по центру графы «Наименование», подчёркнут.</summary>
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

    /// <summary>
    /// Свободная строка, добавленная вокруг заголовка по ГОСТ 2.106. В отличие от резервной строки,
    /// не ставится две подряд.
    /// </summary>
    public bool IsSpacer { get; init; }

    /// <summary>Индекс строки файла Excel, из которой получена строка (для разметки листов в Excel).</summary>
    public int SourceRow { get; internal set; } = -1;

    public bool IsBlank =>
        Format.Length == 0 && Zone.Length == 0 && Position.Length == 0 && Designation.Length == 0
        && Name.Length == 0 && Quantity.Length == 0 && Note.Length == 0;

    public static PrintLine Blank => new();

    public static PrintLine Spacer => new() { IsSpacer = true };

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
    public static List<List<PrintLine>> Paginate(IReadOnlyList<SheetRow> rows, Func<string, double, double> measure) =>
        Paginate(rows, measure, new PdfOptions());

    /// <param name="options">Высота шрифта строк и допустимое сужение текста.</param>
    public static List<List<PrintLine>> Paginate(IReadOnlyList<SheetRow> rows, Func<string, double, double> measure, PdfOptions options)
    {
        Func<string, double, List<string>> wrap = (text, width) =>
            Wrap(text, width, measure, options.Sizes.TableRows, options.MinWidthPercent);
        var blocks = rows.Select((row, index) =>
        {
            var block = ToBlock(row, wrap);
            block.Lines.ForEach(line => line.SourceRow = index);
            return block;
        }).ToList();
        var pages = new List<List<PrintLine>> { new() };

        for (var i = 0; i < blocks.Count; i++)
        {
            var page = pages[^1];
            if (blocks[i].PageBreak)
            {
                if (page.Count > 0)
                {
                    pages.Add(new List<PrintLine>());
                }

                continue;
            }

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
                    if (line.IsSpacer)
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

    private sealed record Block(List<PrintLine> Lines, bool KeepWithNext, bool PageBreak = false);

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

    /// <summary>
    /// Свободная строка перед заголовком не повторяется сразу после другой такой же строки (заголовок группы, за ним
    /// заголовок раздела). В начале листа она остаётся: над заголовком всегда свободная строка, поэтому, например,
    /// «Документация» начинается со второй строки. Резервные и пустые строки из файла добавляются к ней:
    /// между разделами — одна свободная строка и ещё по строке на каждый резерв.
    /// </summary>
    private static List<PrintLine> Trim(List<PrintLine> lines, List<PrintLine> page)
    {
        var skip = 0;
        var previousSpacer = page.Count > 0 && page[^1].IsSpacer;
        while (skip < lines.Count && lines[skip].IsSpacer && previousSpacer)
        {
            skip++;
        }

        return lines.Skip(skip).ToList();
    }

    private static Block ToBlock(SheetRow row, Func<string, double, List<string>> wrap)
    {
        var type = row.Type.Length > 0 ? row.Type : row.IsEmpty ? "" : ExcelFormat.RowTypes.Item;
        var nameWidth = GostLayout.TextWidth(GostLayout.NameColumn);
        switch (type)
        {
            case ExcelFormat.RowTypes.Section:
            case ExcelFormat.RowTypes.Group:
            {
                var style = type == ExcelFormat.RowTypes.Section ? LineStyle.Section : LineStyle.Group;
                var lines = new List<PrintLine> { PrintLine.Spacer };
                lines.AddRange(wrap(row.Name, nameWidth).Select(text => new PrintLine { Name = text, Style = style }));
                lines.Add(PrintLine.Spacer);
                return new Block(lines, true);
            }

            // Текстовая строка без текста — пустая строка (так её можно добавить и из окна спецификации).
            case ExcelFormat.RowTypes.Text when row.Name.Trim().Length > 0:
                return new Block(wrap(row.Name, nameWidth).Select(text => new PrintLine { Name = text }).ToList(), false);

            case ExcelFormat.RowTypes.PageBreak:
                return new Block(new List<PrintLine>(), false, PageBreak: true);

            // Резерв — просто свободная строка, номер позиции не печатается.
            case "":
            case ExcelFormat.RowTypes.Text:
            case ExcelFormat.RowTypes.Blank:
            case ExcelFormat.RowTypes.Reserve:
                return new Block(new List<PrintLine> { PrintLine.Blank }, false);

            default:
            {
                var designation = wrap(row.Designation, GostLayout.TextWidth(GostLayout.DesignationColumn));
                var name = wrap(row.Name, nameWidth);
                var note = wrap(row.Note, GostLayout.TextWidth(GostLayout.NoteColumn));
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

    /// <summary>
    /// Перенос по словам (и после дефиса). Явный перенос строки в ячейке сохраняется.
    /// Если текст чуть-чуть не помещается и сжатие по ширине (до <paramref name="minWidthPercent"/> %) экономит строку —
    /// не переносим, при выводе он сжимается.
    /// Слово длиннее графы остаётся целым и тоже сжимается.
    /// </summary>
    /// <param name="capHeight">Высота букв; по умолчанию — шрифт строк таблицы (5).</param>
    /// <param name="minWidthPercent">Допустимое сужение, % ширины; 0 — переносить только по явному переносу строки.</param>
    public static List<string> Wrap(string text, double width, Func<string, double, double> measure,
        double capHeight = 5, int minWidthPercent = PdfOptions.DefaultMinWidthPercent)
    {
        if (minWidthPercent <= 0)
        {
            return text.Replace("\r", "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        }

        var normal = WrapAt(text, width, measure, capHeight);
        var compressed = WrapAt(text, width * 100 / Math.Min(100, minWidthPercent), measure, capHeight);
        return compressed.Count < normal.Count ? compressed : normal;
    }

    private static List<string> WrapAt(string text, double width, Func<string, double, double> measure, double capHeight)
    {
        var result = new List<string>();
        foreach (var paragraph in text.Replace("\r", "").Split('\n'))
        {
            var words = SplitWords(paragraph.Trim());
            var line = "";
            foreach (var word in words)
            {
                var candidate = line.Length == 0 ? word.TrimStart() : line + word;
                if (line.Length > 0 && measure(candidate.TrimEnd(), capHeight) > width)
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
            // Перед отдельно стоящим тире строку не переносим: «DIN 315 -» / «M8 - St - C», а не «DIN 315» / «- M8».
            var dashNext = i + 1 < text.Length && text[i + 1] is '-' or '–' or '—' && (i + 2 == text.Length || text[i + 2] == ' ');
            var breakBefore = text[i] == ' ' && text[i - 1] != ' ' && !dashNext && !StandardPrefixes.Contains(text[start..i].Trim().Split(' ')[^1]);
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
