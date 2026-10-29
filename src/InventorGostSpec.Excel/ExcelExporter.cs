using System.Globalization;
using ClosedXML.Excel;
using InventorGostSpec.Core.Model;
using InventorGostSpec.Core.View;

namespace InventorGostSpec.Excel;

/// <summary>
/// Экспорт спецификации в Excel: лист «Спецификация» (строки с типом), «Основная надпись», «Изменения» и памятка.
/// Файл рассчитан на ручную правку и последующую конвертацию в PDF по ГОСТ.
/// </summary>
public static class ExcelExporter
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");

    // Ширины колонок (в символах) примерно в пропорциях граф спецификации по ГОСТ 2.106.
    private static readonly double[] Widths = { 11, 7, 6, 6, 26, 46, 7, 8, 22 };

    private static readonly XLColor SectionFill = XLColor.FromHtml("#F7F8FA");
    private static readonly XLColor GroupFill = XLColor.FromHtml("#E1ECF7");
    private static readonly XLColor MutedFont = XLColor.FromHtml("#9CA3AF");

    public static void Export(Specification specification, SpecificationContext context, TitleBlock titleBlock, string path)
    {
        using var workbook = new XLWorkbook();
        FillSpecification(workbook.AddWorksheet(ExcelFormat.SpecificationSheet), specification, context);
        FillTitleBlock(workbook.AddWorksheet(ExcelFormat.TitleBlockSheet), titleBlock);
        FillChanges(workbook.AddWorksheet(ExcelFormat.ChangesSheet), titleBlock.Changes);
        FillHelp(workbook.AddWorksheet(ExcelFormat.HelpSheet));
        workbook.Worksheet(1).SetTabActive();
        workbook.SaveAs(path);
    }

    private static void FillSpecification(IXLWorksheet sheet, Specification specification, SpecificationContext context)
    {
        for (var i = 0; i < ExcelFormat.Columns.Length; i++)
        {
            var cell = sheet.Cell(1, i + 1);
            cell.Value = ExcelFormat.Columns[i];
            sheet.Column(i + 1).Width = Widths[i];
        }

        var header = sheet.Range(1, 1, 1, ExcelFormat.Columns.Length);
        ExcelStyle.Header(header);
        header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        header.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        sheet.Row(1).Height = 22;

        var row = 2;
        foreach (var (display, values) in Rows(specification, context))
        {
            WriteRow(sheet, row, display, values);
            row++;
        }

        var last = Math.Max(row - 1, 1);
        var table = sheet.Range(1, 1, last, ExcelFormat.Columns.Length);
        ExcelStyle.Grid(table);
        table.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        sheet.Range(2, 1, Math.Max(last, 2), ExcelFormat.Columns.Length).Style.Alignment.WrapText = true;

        foreach (var column in new[] { ExcelFormat.FormatColumn, ExcelFormat.ZoneColumn, ExcelFormat.PositionColumn, ExcelFormat.QuantityColumn, ExcelFormat.UnitColumn })
        {
            sheet.Column(column).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        // Тип строки выбирается из списка — по нему утилита PDF оформляет строки.
        var validation = sheet.Range(2, ExcelFormat.TypeColumn, 2000, ExcelFormat.TypeColumn).CreateDataValidation();
        validation.List($"\"{string.Join(",", ExcelFormat.RowTypes.All)}\"", true);
        validation.IgnoreBlanks = true;
        validation.ErrorTitle = "Тип строки";
        validation.ErrorMessage = "Выберите тип из списка: " + string.Join(", ", ExcelFormat.RowTypes.All) + ".";

        sheet.SheetView.FreezeRows(1);
        sheet.PageSetup.PageOrientation = XLPageOrientation.Portrait;
        sheet.PageSetup.PaperSize = XLPaperSize.A4Paper;
        sheet.PageSetup.FitToPages(1, 0);
        sheet.PageSetup.SetRowsToRepeatAtTop(1, 1);
    }

    /// <summary>
    /// Строки листа «Спецификация» — те же, что пишутся в файл: по ним окно спецификации показывает границы листов PDF
    /// (<c>Display</c> — строка окна; <c>null</c> — служебная строка «Новый лист» перед разделом или группой с нового листа).
    /// </summary>
    public static List<(DisplayRow? Display, SheetRow Values)> Rows(Specification specification, SpecificationContext context) =>
        Rows(specification, SpecificationViewBuilder.Build(specification, context));

    /// <param name="displays">Строки окна, уже построенные <see cref="SpecificationViewBuilder"/>.</param>
    public static List<(DisplayRow? Display, SheetRow Values)> Rows(Specification specification, IEnumerable<DisplayRow> displays)
    {
        var result = new List<(DisplayRow?, SheetRow)>();
        foreach (var display in displays)
        {
            // Подпись уровня «Общие компоненты» есть только в окне.
            if (display.Kind == DisplayRowKind.CommonHeader)
            {
                continue;
            }

            var newPage = display.Kind switch
            {
                DisplayRowKind.GroupHeader => specification.StartsNewPage(display.GroupId, ""),
                DisplayRowKind.SectionHeader => specification.StartsNewPage(display.GroupId, display.SectionId),
                _ => false,
            };
            if (newPage)
            {
                result.Add((null, new SheetRow(ExcelFormat.RowTypes.PageBreak, "", "", "", "", "", "", "", "")));
            }

            result.Add((display, ToSheetRow(display)));
        }

        return result;
    }

    /// <summary>Значения строки файла Excel для строки окна (как их прочитает утилита PDF).</summary>
    public static SheetRow ToSheetRow(DisplayRow display)
    {
        switch (display.Kind)
        {
            case DisplayRowKind.GroupHeader:
            case DisplayRowKind.VersionsHeader:
                return new SheetRow(ExcelFormat.RowTypes.Group, "", "", "", "", display.SectionName, "", "", "");
            case DisplayRowKind.Notice:
                return new SheetRow(ExcelFormat.RowTypes.Text, "", "", "", "", display.SectionName, "", "", "");
            case DisplayRowKind.SectionHeader:
                return new SheetRow(ExcelFormat.RowTypes.Section, "", "", "", "", display.SectionName, "", "", "");
            case DisplayRowKind.Reserve:
                return new SheetRow(ExcelFormat.RowTypes.Reserve, "", "", display.ReservedPosition?.ToString() ?? "", "", "", "", "", "");
            case DisplayRowKind.Row when display.Row is { Kind: RowKind.Note } note:
                return new SheetRow(ExcelFormat.RowTypes.Text, "", "", "", "", note.Get(SpecColumn.Name), "", "", "");
            case DisplayRowKind.Row when display.Row is { } item:
                // Несколько форматов или дополнительный формат — «*)» в графе «Формат», форматы — в «Примечании».
                var (format, itemNote) = SheetFormats.ForColumns(item.Get(SpecColumn.Format), item.Get(SpecColumn.Note));
                return new SheetRow(
                    ExcelFormat.RowTypes.Item,
                    format,
                    item.Get(SpecColumn.Zone),
                    item.Position?.ToString() ?? "",
                    item.Get(SpecColumn.Designation),
                    item.Get(SpecColumn.Name),
                    // У документов количество не пишется (ГОСТ 2.106).
                    ComponentSnapshot.IsDocument(item.SectionId) ? "" : item.Get(SpecColumn.Quantity),
                    item.Get(SpecColumn.Unit),
                    itemNote);
            default:
                return new SheetRow("", "", "", "", "", "", "", "", "");
        }
    }

    /// <summary>Записывает значения строки (<see cref="ToSheetRow"/>) и оформляет её по виду строки окна.</summary>
    private static void WriteRow(IXLWorksheet sheet, int row, DisplayRow? display, SheetRow values)
    {
        sheet.Cell(row, ExcelFormat.TypeColumn).Value = values.Type;
        sheet.Cell(row, ExcelFormat.FormatColumn).Value = values.Format;
        sheet.Cell(row, ExcelFormat.ZoneColumn).Value = values.Zone;
        if (int.TryParse(values.Position, out var position))
        {
            sheet.Cell(row, ExcelFormat.PositionColumn).Value = position;
        }

        sheet.Cell(row, ExcelFormat.DesignationColumn).Value = values.Designation;
        sheet.Cell(row, ExcelFormat.NameColumn).Value = values.Name;
        if (values.Quantity.Length > 0)
        {
            SetQuantity(sheet.Cell(row, ExcelFormat.QuantityColumn), values.Quantity);
        }

        sheet.Cell(row, ExcelFormat.UnitColumn).Value = values.Unit;
        sheet.Cell(row, ExcelFormat.NoteColumn).Value = values.Note;
        sheet.Cell(row, ExcelFormat.TypeColumn).Style.Font.FontColor = MutedFont;

        var range = sheet.Range(row, 1, row, ExcelFormat.Columns.Length);
        switch (display?.Kind)
        {
            case DisplayRowKind.GroupHeader:
            case DisplayRowKind.VersionsHeader:
                range.Style.Fill.BackgroundColor = GroupFill;
                range.Style.Font.Bold = true;
                break;

            case DisplayRowKind.SectionHeader:
                var name = sheet.Cell(row, ExcelFormat.NameColumn);
                name.Style.Font.Underline = XLFontUnderlineValues.Single;
                name.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                range.Style.Fill.BackgroundColor = SectionFill;
                range.Style.Font.Bold = true;
                break;

            case DisplayRowKind.Reserve:
                range.Style.Font.FontColor = MutedFont;
                break;
        }
    }

    /// <summary>Количество — числом, если это число (удобно править и считать), иначе текстом («по месту»).</summary>
    private static void SetQuantity(IXLCell cell, string quantity)
    {
        if (decimal.TryParse(quantity, NumberStyles.Number, RuCulture, out var number))
        {
            cell.Value = number;
            cell.Style.NumberFormat.Format = "0.###";
        }
        else
        {
            cell.Value = quantity;
        }
    }

    private static void FillTitleBlock(IXLWorksheet sheet, TitleBlock title)
    {
        sheet.Cell(1, 1).Value = "Поле";
        sheet.Cell(1, 2).Value = "Значение";
        var header = sheet.Range(1, 1, 1, 2);
        ExcelStyle.Header(header);

        var fields = new (string Field, string Value)[]
        {
            (ExcelFormat.TitleFields.Designation, title.Designation),
            (ExcelFormat.TitleFields.Name, title.Name),
            (ExcelFormat.TitleFields.Organization, title.Organization),
            (ExcelFormat.TitleFields.Letter, title.Letter),
            (ExcelFormat.TitleFields.Developer, title.Developer),
            (ExcelFormat.TitleFields.Checker, title.Checker),
            (ExcelFormat.TitleFields.NormControl, title.NormControl),
            (ExcelFormat.TitleFields.Approver, title.Approver),
            (ExcelFormat.TitleFields.FirstUse, title.FirstUse),
        };

        for (var i = 0; i < fields.Length; i++)
        {
            sheet.Cell(i + 2, 1).Value = fields[i].Field;
            sheet.Cell(i + 2, 2).Value = fields[i].Value;
        }

        // Высота шрифта наименования для этого документа — числом; пусто — из настроек утилиты PDF.
        var heightRow = fields.Length + 2;
        sheet.Cell(heightRow, 1).Value = ExcelFormat.TitleFields.NameHeight;
        var height = sheet.Cell(heightRow, 2);
        if (title.NameTextHeight is { } nameHeight)
        {
            height.Value = nameHeight;
        }

        height.Style.NumberFormat.Format = "0.#";
        height.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

        sheet.Column(1).Width = 24;
        sheet.Column(2).Width = 60;
        var table = sheet.Range(1, 1, heightRow, 2);
        ExcelStyle.Grid(table);
    }

    // Пустых строк под записи изменений (с рамкой и текстовым форматом), сверх уже внесённых.
    private const int SpareChangeRows = 10;

    private static void FillChanges(IXLWorksheet sheet, IReadOnlyList<ChangeRecord> changes)
    {
        var widths = new double[] { 7, 9, 12, 24, 18, 12 };
        for (var i = 0; i < ExcelFormat.ChangeColumns.Length; i++)
        {
            sheet.Cell(1, i + 1).Value = ExcelFormat.ChangeColumns[i];
            sheet.Column(i + 1).Width = widths[i];
        }

        var header = sheet.Range(1, 1, 1, ExcelFormat.ChangeColumns.Length);
        ExcelStyle.Header(header);
        header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        var last = changes.Count + SpareChangeRows + 1;
        var body = sheet.Range(2, 1, last, ExcelFormat.ChangeColumns.Length);

        // Всё — текстом: иначе Excel превратит «1-3» или «05.25» в дату, а «01» — в число.
        body.Style.NumberFormat.Format = "@";
        body.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        for (var i = 0; i < changes.Count; i++)
        {
            var change = changes[i];
            var values = new[] { change.Number, change.Areas, change.Sheet, change.Document, change.Signature, change.Date };
            for (var c = 0; c < values.Length; c++)
            {
                sheet.Cell(i + 2, c + 1).SetValue(values[c]);
            }
        }

        var table = sheet.Range(1, 1, last, ExcelFormat.ChangeColumns.Length);
        ExcelStyle.Grid(table);
        sheet.SheetView.FreezeRows(1);
    }

    private static void FillHelp(IXLWorksheet sheet)
    {
        var lines = new[]
        {
            "Как заполнять",
            "",
            "Лист «Спецификация» — строки спецификации в порядке записи. Их можно править, добавлять, удалять и переставлять.",
            "Колонка «Тип строки» определяет оформление при выводе в PDF по ГОСТ:",
            "  Группа — заголовок группы «Изделия, устанавливаемые по …» (текст в «Наименовании»);",
            "  Раздел — заголовок раздела: «Документация», «Детали» и т. д. (в «Наименовании», будет подчёркнут);",
            "  Изделие — обычная строка: формат, зона, позиция, обозначение, наименование, количество, примечание;",
            "  Текст — строка текста без позиции (в «Наименовании»);",
            "  Резерв — свободная строка резервной позиции (номер позиции для справки, в PDF не выводится);",
            "  Пустая — пустая строка, добавленная вручную: например, чтобы сдвинуть запись на следующий лист;",
            "  Новый лист — следующие строки начинаются с нового листа PDF (сама строка не выводится).",
            "Колонка «Лист PDF» и жирные линии показывают, где кончается лист PDF. После ручной правки обновите разметку",
            "кнопкой «Разметить листы в Excel» в утилите PDF (файл Excel должен быть закрыт).",
            "Пустой «Тип строки» у заполненной строки считается «Изделием», у пустой — пустой строкой.",
            "Ручной перенос строки — Alt+Enter внутри ячейки: в PDF текст перенесётся в этом месте (Обозначение, Наименование,",
            "Примечание; на листе «Основная надпись» — Наименование). Без него длинный текст сужается по ширине",
            "(по умолчанию до 80 %, задаётся в утилите PDF), а если не помещается и так — переносится по словам.",
            "«Ед. изм.» выводится вместе с количеством (например, «2,5 м»); для штук оставьте пустым.",
            "",
            "Лист «Основная надпись» — данные для основной надписи (обозначение, наименование, подписи).",
            "  «Шрифт наименования, мм» — высота шрифта наименования изделия для этого документа (2,5; 3,5; 5; 7; 10);",
            "  пусто — из настроек утилиты PDF. Остальные размеры шрифтов и допустимое сужение текста задаются в утилите PDF.",
            "",
            "Лист «Изменения» — графы изменений основной надписи (ГОСТ 2.503), одна строка — одна запись, по порядку внесения:",
            "  Изм. — порядковый номер изменения; Кол. уч. — количество изменённых участков на листе;",
            "  Лист — номер изменённого листа; № докум. — обозначение извещения об изменении; Подп. и Дата — кто и когда внёс.",
            "При выводе в PDF все записи попадают в основную надпись первого листа, а также указанных в «Лист» листов;",
            "запись без номеров листов («Лист» пусто или «Все») — изменение всего документа, она на всех листах;",
            "если записей больше, чем граф в основной надписи, выводятся последние. Ячейки — текстовые, вводите как есть.",
            "",
            "Файл создан аддином InventorGostSpec. Изменения в нём не возвращаются в сборку Inventor.",
        };

        for (var i = 0; i < lines.Length; i++)
        {
            sheet.Cell(i + 1, 1).Value = lines[i];
        }

        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 14;
        sheet.Column(1).Width = 120;
    }
}
