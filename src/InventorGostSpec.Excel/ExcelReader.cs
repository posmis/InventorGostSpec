using System.Globalization;
using ClosedXML.Excel;
using InventorGostSpec.Core.Model;

namespace InventorGostSpec.Excel;

/// <summary>Строка листа «Спецификация» как она записана в файле (после ручной правки).</summary>
public sealed record SheetRow(
    string Type,
    string Format,
    string Zone,
    string Position,
    string Designation,
    string Name,
    string Quantity,
    string Unit,
    string Note)
{
    public bool IsEmpty =>
        new[] { Format, Zone, Position, Designation, Name, Quantity, Unit, Note }.All(v => v.Length == 0);
}

/// <summary>Содержимое файла Excel спецификации: строки, основная надпись и изменения.</summary>
public sealed class SpecificationFile
{
    public List<SheetRow> Rows { get; } = new();

    public TitleBlock TitleBlock { get; set; } = new();
}

/// <summary>
/// Чтение файла Excel в формате <see cref="ExcelFormat"/>, в том числе поправленного вручную.
/// Колонки ищутся по заголовкам, поэтому их можно переставлять.
/// </summary>
public static class ExcelReader
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");

    public static SpecificationFile Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var workbook = new XLWorkbook(stream);
        if (!workbook.TryGetWorksheet(ExcelFormat.SpecificationSheet, out var sheet))
        {
            throw new InvalidDataException($"В файле нет листа «{ExcelFormat.SpecificationSheet}».");
        }

        var file = new SpecificationFile();
        ReadRows(sheet, file.Rows);
        file.TitleBlock = ReadTitleBlock(workbook);
        file.TitleBlock.Changes = ReadChanges(workbook);
        return file;
    }

    /// <summary>
    /// Записи листа «Изменения» — например, чтобы повторная выгрузка в тот же файл не стирала внесённые вручную
    /// изменения. Нет файла или листа — пустой список.
    /// </summary>
    public static List<ChangeRecord> ReadChanges(string path)
    {
        if (!File.Exists(path))
        {
            return new List<ChangeRecord>();
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var workbook = new XLWorkbook(stream);
        return ReadChanges(workbook);
    }

    private static void ReadRows(IXLWorksheet sheet, List<SheetRow> rows)
    {
        var columns = FindColumns(sheet.Row(1), ExcelFormat.Columns);
        var last = sheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var row = 2; row <= last; row++)
        {
            string Get(int index) => columns[index] is { } column ? Text(sheet.Cell(row, column)) : "";

            rows.Add(new SheetRow(
                Get(ExcelFormat.TypeColumn - 1),
                Get(ExcelFormat.FormatColumn - 1),
                Get(ExcelFormat.ZoneColumn - 1),
                Get(ExcelFormat.PositionColumn - 1),
                Get(ExcelFormat.DesignationColumn - 1),
                Get(ExcelFormat.NameColumn - 1),
                Get(ExcelFormat.QuantityColumn - 1),
                Get(ExcelFormat.UnitColumn - 1),
                Get(ExcelFormat.NoteColumn - 1)));
        }

        // Хвост из пустых строк (рамки, оформление) не нужен.
        while (rows.Count > 0 && rows[^1].IsEmpty && rows[^1].Type.Length == 0)
        {
            rows.RemoveAt(rows.Count - 1);
        }
    }

    private static TitleBlock ReadTitleBlock(XLWorkbook workbook)
    {
        var title = new TitleBlock();
        if (!workbook.TryGetWorksheet(ExcelFormat.TitleBlockSheet, out var sheet))
        {
            return title;
        }

        var setters = new Dictionary<string, Action<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [ExcelFormat.TitleFields.Designation] = v => title.Designation = v,
            [ExcelFormat.TitleFields.Name] = v => title.Name = v,
            [ExcelFormat.TitleFields.Organization] = v => title.Organization = v,
            [ExcelFormat.TitleFields.Letter] = v => title.Letter = v,
            [ExcelFormat.TitleFields.Developer] = v => title.Developer = v,
            [ExcelFormat.TitleFields.Checker] = v => title.Checker = v,
            [ExcelFormat.TitleFields.TechControl] = v => title.TechControl = v,
            [ExcelFormat.TitleFields.NormControl] = v => title.NormControl = v,
            [ExcelFormat.TitleFields.Approver] = v => title.Approver = v,
            [ExcelFormat.TitleFields.FirstUse] = v => title.FirstUse = v,
        };

        var last = sheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var row = 2; row <= last; row++)
        {
            if (setters.TryGetValue(Text(sheet.Cell(row, 1)), out var set))
            {
                set(Text(sheet.Cell(row, 2)));
            }
        }

        return title;
    }

    private static List<ChangeRecord> ReadChanges(XLWorkbook workbook)
    {
        var changes = new List<ChangeRecord>();
        if (!workbook.TryGetWorksheet(ExcelFormat.ChangesSheet, out var sheet))
        {
            return changes;
        }

        var columns = FindColumns(sheet.Row(1), ExcelFormat.ChangeColumns);
        var last = sheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var row = 2; row <= last; row++)
        {
            string Get(int column) => columns[column - 1] is { } c ? Text(sheet.Cell(row, c)) : "";

            var change = new ChangeRecord
            {
                Number = Get(ExcelFormat.ChangeNumberColumn),
                Areas = Get(ExcelFormat.ChangeAreasColumn),
                Sheet = Get(ExcelFormat.ChangeSheetColumn),
                Document = Get(ExcelFormat.ChangeDocumentColumn),
                Signature = Get(ExcelFormat.ChangeSignatureColumn),
                Date = Get(ExcelFormat.ChangeDateColumn),
            };
            if (new[] { change.Number, change.Areas, change.Sheet, change.Document, change.Signature, change.Date }.Any(v => v.Length > 0))
            {
                changes.Add(change);
            }
        }

        return changes;
    }

    /// <summary>Номера колонок по заголовкам; если заголовка нет — стандартный номер из формата.</summary>
    private static int?[] FindColumns(IXLRow header, string[] names)
    {
        var found = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in header.CellsUsed())
        {
            found.TryAdd(Text(cell), cell.Address.ColumnNumber);
        }

        return names.Select((name, i) => found.TryGetValue(name, out var column) ? column : found.Count == 0 ? i + 1 : (int?)null).ToArray();
    }

    /// <summary>Значение ячейки текстом: числа — с запятой и без лишних нулей, даты — дд.мм.гггг.</summary>
    private static string Text(IXLCell cell)
    {
        var value = cell.Value;
        var text = value.Type switch
        {
            XLDataType.Number => value.GetNumber().ToString("0.###", RuCulture),
            XLDataType.DateTime => value.GetDateTime().ToString("dd.MM.yyyy", RuCulture),
            XLDataType.Blank => "",
            _ => cell.GetFormattedString(),
        };
        return text.Trim();
    }
}
