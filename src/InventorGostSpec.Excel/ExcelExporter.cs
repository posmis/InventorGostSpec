using System.Globalization;
using ClosedXML.Excel;
using InventorGostSpec.Core.Model;
using InventorGostSpec.Core.View;

namespace InventorGostSpec.Excel;

/// <summary>
/// Экспорт спецификации в Excel: лист «Спецификация» (строки с типом), «Основная надпись» и памятка.
/// Файл рассчитан на ручную правку и последующую конвертацию в PDF по ГОСТ.
/// </summary>
public static class ExcelExporter
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");

    // Ширины колонок (в символах) примерно в пропорциях граф спецификации по ГОСТ 2.106.
    private static readonly double[] Widths = { 11, 7, 6, 6, 26, 46, 7, 8, 22 };

    private static readonly XLColor HeaderFill = XLColor.FromHtml("#EEF1F5");
    private static readonly XLColor SectionFill = XLColor.FromHtml("#F7F8FA");
    private static readonly XLColor GroupFill = XLColor.FromHtml("#E1ECF7");
    private static readonly XLColor MutedFont = XLColor.FromHtml("#9CA3AF");

    public static void Export(Specification specification, SpecificationContext context, TitleBlock titleBlock, string path)
    {
        using var workbook = new XLWorkbook();
        FillSpecification(workbook.AddWorksheet(ExcelFormat.SpecificationSheet), specification, context);
        FillTitleBlock(workbook.AddWorksheet(ExcelFormat.TitleBlockSheet), titleBlock);
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
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = HeaderFill;
        header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        header.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        sheet.Row(1).Height = 22;

        var row = 2;
        foreach (var display in SpecificationViewBuilder.Build(specification, context))
        {
            WriteRow(sheet, row, display);
            row++;
        }

        var last = Math.Max(row - 1, 1);
        var table = sheet.Range(1, 1, last, ExcelFormat.Columns.Length);
        table.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        table.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        table.Style.Border.InsideBorderColor = XLColor.FromHtml("#D9DDE3");
        table.Style.Border.OutsideBorderColor = XLColor.FromHtml("#D9DDE3");
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

    private static void WriteRow(IXLWorksheet sheet, int row, DisplayRow display)
    {
        var range = sheet.Range(row, 1, row, ExcelFormat.Columns.Length);
        switch (display.Kind)
        {
            case DisplayRowKind.GroupHeader:
                sheet.Cell(row, ExcelFormat.TypeColumn).Value = ExcelFormat.RowTypes.Group;
                sheet.Cell(row, ExcelFormat.NameColumn).Value = display.SectionName;
                range.Style.Fill.BackgroundColor = GroupFill;
                range.Style.Font.Bold = true;
                break;

            case DisplayRowKind.SectionHeader:
                sheet.Cell(row, ExcelFormat.TypeColumn).Value = ExcelFormat.RowTypes.Section;
                var name = sheet.Cell(row, ExcelFormat.NameColumn);
                name.Value = display.SectionName;
                name.Style.Font.Underline = XLFontUnderlineValues.Single;
                name.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                range.Style.Fill.BackgroundColor = SectionFill;
                range.Style.Font.Bold = true;
                break;

            case DisplayRowKind.Reserve:
                sheet.Cell(row, ExcelFormat.TypeColumn).Value = ExcelFormat.RowTypes.Reserve;
                if (display.ReservedPosition is { } reserved)
                {
                    sheet.Cell(row, ExcelFormat.PositionColumn).Value = reserved;
                }

                range.Style.Font.FontColor = MutedFont;
                break;

            case DisplayRowKind.Row when display.Row is { Kind: RowKind.Note } note:
                sheet.Cell(row, ExcelFormat.TypeColumn).Value = ExcelFormat.RowTypes.Text;
                sheet.Cell(row, ExcelFormat.NameColumn).Value = note.Get(SpecColumn.Name);
                break;

            case DisplayRowKind.Row when display.Row is { } item:
                sheet.Cell(row, ExcelFormat.TypeColumn).Value = ExcelFormat.RowTypes.Item;
                sheet.Cell(row, ExcelFormat.FormatColumn).Value = item.Get(SpecColumn.Format);
                sheet.Cell(row, ExcelFormat.ZoneColumn).Value = item.Get(SpecColumn.Zone);
                if (item.Position is { } position)
                {
                    sheet.Cell(row, ExcelFormat.PositionColumn).Value = position;
                }

                sheet.Cell(row, ExcelFormat.DesignationColumn).Value = item.Get(SpecColumn.Designation);
                sheet.Cell(row, ExcelFormat.NameColumn).Value = item.Get(SpecColumn.Name);
                SetQuantity(sheet.Cell(row, ExcelFormat.QuantityColumn), item.Get(SpecColumn.Quantity));
                sheet.Cell(row, ExcelFormat.UnitColumn).Value = item.Get(SpecColumn.Unit);
                sheet.Cell(row, ExcelFormat.NoteColumn).Value = item.Get(SpecColumn.Note);
                break;
        }

        sheet.Cell(row, ExcelFormat.TypeColumn).Style.Font.FontColor = MutedFont;
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
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = HeaderFill;

        var fields = new (string Field, string Value)[]
        {
            (ExcelFormat.TitleFields.Designation, title.Designation),
            (ExcelFormat.TitleFields.Name, title.Name),
            (ExcelFormat.TitleFields.Organization, title.Organization),
            (ExcelFormat.TitleFields.Letter, title.Letter),
            (ExcelFormat.TitleFields.Developer, title.Developer),
            (ExcelFormat.TitleFields.Checker, title.Checker),
            (ExcelFormat.TitleFields.TechControl, title.TechControl),
            (ExcelFormat.TitleFields.NormControl, title.NormControl),
            (ExcelFormat.TitleFields.Approver, title.Approver),
            (ExcelFormat.TitleFields.FirstUse, title.FirstUse),
        };

        for (var i = 0; i < fields.Length; i++)
        {
            sheet.Cell(i + 2, 1).Value = fields[i].Field;
            sheet.Cell(i + 2, 2).Value = fields[i].Value;
        }

        sheet.Column(1).Width = 18;
        sheet.Column(2).Width = 60;
        var table = sheet.Range(1, 1, fields.Length + 1, 2);
        table.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        table.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        table.Style.Border.InsideBorderColor = XLColor.FromHtml("#D9DDE3");
        table.Style.Border.OutsideBorderColor = XLColor.FromHtml("#D9DDE3");
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
            "  Резерв — пустая строка резервной позиции (номер позиции можно оставить или стереть).",
            "Пустой «Тип строки» у заполненной строки считается «Изделием», у пустой — пустой строкой.",
            "«Ед. изм.» выводится вместе с количеством (например, «2,5 м»); для штук оставьте пустым.",
            "",
            "Лист «Основная надпись» — данные для основной надписи (обозначение, наименование, подписи).",
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
