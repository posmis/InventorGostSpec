using InventorGostSpec.Core.Model;
using InventorGostSpec.Excel;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using static InventorGostSpec.Pdf.GostLayout;

namespace InventorGostSpec.Pdf;

/// <summary>
/// PDF спецификации по ГОСТ 2.106 из файла Excel: первый лист по форме 1 с основной надписью формы 2,
/// последующие — по форме 1а с надписью 2а (ГОСТ 2.104), графы на поле подшивки, изменения в основной надписи.
/// </summary>
public static class GostPdfWriter
{
    /// <summary>Записывает PDF и возвращает число листов.</summary>
    public static int Write(SpecificationFile file, PdfOptions options, string outputPath)
    {
        var text = CreateText(options);
        var pages = SpecPaginator.Paginate(file.Rows, text.Measure, options);

        using var document = new PdfDocument();
        var title = file.TitleBlock;
        document.Info.Title = string.Join(" ", new[] { title.Designation, title.Name }.Where(s => s.Length > 0));
        document.Info.Creator = "InventorGostSpec";

        for (var i = 0; i < pages.Count; i++)
        {
            var page = document.AddPage();
            page.Width = XUnit.FromMillimeter(PageWidth);
            page.Height = XUnit.FromMillimeter(PageHeight);
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.ScaleTransform(72 / 25.4);
            new SheetPainter(gfx, text, title, options, i, pages.Count).Draw(pages[i]);
        }

        document.Save(outputPath);
        return pages.Count;
    }

    /// <summary>Раскладка строк по листам — как в PDF (для разметки листов в Excel).</summary>
    public static List<List<PrintLine>> Paginate(SpecificationFile file, PdfOptions options) =>
        SpecPaginator.Paginate(file.Rows, CreateText(options).Measure, options);

    private static GostText CreateText(PdfOptions options)
    {
        var font = FontLocator.Find(options.Font)
            ?? throw new InvalidOperationException(string.IsNullOrWhiteSpace(options.Font)
                ? "Не найден ни шрифт ГОСТ, ни Arial."
                : $"Шрифт «{options.Font}» не найден. Укажите установленный шрифт TrueType или путь к файлу .ttf.");
        return new GostText(GostFontResolver.Register(font.Path));
    }

    /// <summary>Входит ли лист в графу «Лист» записи изменения («2», «1-3», «1, 4»). Без номеров — только первый лист.</summary>
    public static bool SheetMatches(string sheets, int sheet)
    {
        var any = false;
        foreach (var part in sheets.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var range = part.Split(new[] { '-', '–', '…' }, StringSplitOptions.RemoveEmptyEntries);
            if (range.Length is 1 or 2 && range.All(r => int.TryParse(r, out _)))
            {
                any = true;
                var from = int.Parse(range[0]);
                var to = int.Parse(range[^1]);
                if (sheet >= Math.Min(from, to) && sheet <= Math.Max(from, to))
                {
                    return true;
                }
            }
        }

        return !any && sheet == 1;
    }

    private sealed class SheetPainter
    {
        private static readonly XPen Thick = new(XColors.Black, ThickLine);
        private static readonly XPen Thin = new(XColors.Black, ThinLine);

        // Графы изменений и подписей основной надписи: Изм., Лист, № докум., Подп., Дата.
        private static readonly double[] ChangeX = { 20, 27, 37, 60, 75, 85 };

        private readonly XGraphics _gfx;
        private readonly GostText _text;
        private readonly TitleBlock _title;
        private readonly FontSizes _sizes;
        private readonly int _minWidthPercent;
        private readonly int _index;
        private readonly int _count;

        public SheetPainter(XGraphics gfx, GostText text, TitleBlock title, PdfOptions options, int index, int count)
        {
            _sizes = options.Sizes;
            _minWidthPercent = options.MinWidthPercent;
            _gfx = gfx;
            _text = text;
            _title = title;
            _index = index;
            _count = count;
        }

        private bool First => _index == 0;

        private double TitleTop => FrameBottom - (First ? FirstTitleHeight : NextTitleHeight);

        public void Draw(List<PrintLine> lines)
        {
            DrawTable(lines);
            if (First)
            {
                DrawFirstTitleBlock();
            }
            else
            {
                DrawNextTitleBlock();
            }

            DrawChanges();
            DrawMarginGraphs();
            _gfx.DrawRectangle(Thick, FrameLeft, FrameTop, FrameRight - FrameLeft, FrameBottom - FrameTop);

            _text.Draw(_gfx, "Копировал", 110, 296, _sizes.Margin, 30, TextAlign.Left);
            _text.Draw(_gfx, "Формат А4", 165, 296, _sizes.Margin, 40, TextAlign.Right);
        }

        private void DrawTable(List<PrintLine> lines)
        {
            var headerBottom = FrameTop + HeaderHeight;
            var rows = RowsOnPage(_index);
            var rowHeight = (TitleTop - headerBottom) / rows;

            for (var row = 1; row < rows; row++)
            {
                var y = headerBottom + row * rowHeight;
                _gfx.DrawLine(Thin, FrameLeft, y, FrameRight, y);
            }

            _gfx.DrawLine(Thick, FrameLeft, headerBottom, FrameRight, headerBottom);
            for (var column = 1; column < ColumnWidths.Length; column++)
            {
                var x = ColumnLeft(column);
                _gfx.DrawLine(Thick, x, FrameTop, x, TitleTop);
            }

            for (var column = 0; column < ColumnWidths.Length; column++)
            {
                var titleLines = ColumnTitles[column].Split('\n');
                if (VerticalTitles[column])
                {
                    _text.DrawVertical(_gfx, titleLines, ColumnLeft(column), FrameTop, ColumnWidths[column], HeaderHeight, _sizes.VerticalTitles);
                }
                else
                {
                    _text.DrawLines(_gfx, titleLines, ColumnLeft(column), FrameTop, ColumnWidths[column], HeaderHeight, _sizes.ColumnTitles, TextAlign.Center);
                }
            }

            for (var i = 0; i < lines.Count; i++)
            {
                DrawLine(lines[i], headerBottom + i * rowHeight, rowHeight);
            }
        }

        private void DrawLine(PrintLine line, double y, double height)
        {
            for (var column = 0; column < ColumnWidths.Length; column++)
            {
                var value = line[column];
                if (value.Length == 0)
                {
                    continue;
                }

                var heading = line.Style != LineStyle.Normal;
                var align = column is DesignationColumn or NoteColumn || (column == NameColumn && !heading) ? TextAlign.Left : TextAlign.Center;
                var x = ColumnLeft(column);

                // Весь текст строк — шрифт 5; в узкой графе «Формат» он сжимается по ширине.
                // Заголовки разделов и групп «Изделия, устанавливаемые по …» подчёркиваются (каждая строка).
                var section = line.Style != LineStyle.Normal && column == NameColumn;
                var padding = column == FormatColumn ? NarrowPadding : (double?)null;
                var gap = RowBaselineGap;
                var width = _text.DrawInBox(_gfx, value, x, y, ColumnWidths[column], height, _sizes.TableRows, align, padding, gap);

                if (section)
                {
                    var left = x + (ColumnWidths[column] - width) / 2;
                    var underline = y + height - gap + 0.7;
                    _gfx.DrawLine(Thin, left, underline, left + width, underline);
                }
            }
        }

        /// <summary>Основная надпись по форме 2 (ГОСТ 2.104), 185 × 40 мм.</summary>
        private void DrawFirstTitleBlock()
        {
            var top = TitleTop;
            DrawChangeGrid(top, signatureRows: true);

            // Обозначение документа (графа 2).
            _gfx.DrawLine(Thick, 85, top + 15, FrameRight, top + 15);
            _text.DrawInBox(_gfx, _title.Designation, 85, top, 120, 15, _sizes.Designation, TextAlign.Center);

            // Наименование (графа 1), литера, лист, листов, организация.
            _gfx.DrawLine(Thick, 155, top + 15, 155, FrameBottom);
            // Высота шрифта наименования — из файла (задаёт пользователь), по умолчанию 5.
            // Строк — сколько поместится по высоте графы; лишнее дописывается в последнюю строку со сжатием.
            var nameHeight = _title.NameTextHeight ?? _sizes.Name;
            var nameLines = SpecPaginator.Wrap(_title.Name, 66, _text.Measure, nameHeight, _minWidthPercent);
            var maxLines = Math.Max(1, (int)((25 - 2 - nameHeight) / (nameHeight * LineSpacing)) + 1);
            if (nameLines.Count > maxLines)
            {
                nameLines = nameLines.Take(maxLines - 1).Append(string.Join(" ", nameLines.Skip(maxLines - 1))).ToList();
            }

            _text.DrawLines(_gfx, nameLines, 85, top + 15, 70, 25, nameHeight, TextAlign.Center);

            _gfx.DrawLine(Thick, 155, top + 20, FrameRight, top + 20);
            _gfx.DrawLine(Thick, 155, top + 25, FrameRight, top + 25);
            _gfx.DrawLine(Thick, 170, top + 15, 170, top + 25);
            _gfx.DrawLine(Thick, 185, top + 15, 185, top + 25);
            _gfx.DrawLine(Thin, 160, top + 20, 160, top + 25);
            _gfx.DrawLine(Thin, 165, top + 20, 165, top + 25);
            _text.DrawInBox(_gfx, "Лит.", 155, top + 15, 15, 5, _sizes.TitleBlock, TextAlign.Center);
            _text.DrawInBox(_gfx, "Лист", 170, top + 15, 15, 5, _sizes.TitleBlock, TextAlign.Center);
            _text.DrawInBox(_gfx, "Листов", 185, top + 15, 20, 5, _sizes.TitleBlock, TextAlign.Center);

            var letters = _title.Letter.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < Math.Min(3, letters.Length); i++)
            {
                _text.DrawInBox(_gfx, letters[i], 155 + 5 * i, top + 20, 5, 5, _sizes.TitleBlock, TextAlign.Center);
            }

            // На документе из одного листа графу «Лист» не заполняют.
            if (_count > 1)
            {
                _text.DrawInBox(_gfx, "1", 170, top + 20, 15, 5, _sizes.TitleBlock, TextAlign.Center);
            }

            _text.DrawInBox(_gfx, _count.ToString(), 185, top + 20, 20, 5, _sizes.TitleBlock, TextAlign.Center);
            // Организация — одной строкой (шрифт из настроек, по умолчанию 7), при необходимости сжимается по ширине.
            var organization = string.Join(" ", _title.Organization.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
            _text.DrawLines(_gfx, new[] { organization }, 155, top + 25, 50, 15, _sizes.Organization, TextAlign.Center);
            _gfx.DrawLine(Thick, FrameLeft, top, FrameRight, top);
        }

        /// <summary>Основная надпись по форме 2а (ГОСТ 2.104), 185 × 15 мм.</summary>
        private void DrawNextTitleBlock()
        {
            var top = TitleTop;
            DrawChangeGrid(top, signatureRows: false);
            _text.DrawInBox(_gfx, _title.Designation, 85, top, 110, 15, _sizes.Designation, TextAlign.Center);
            _gfx.DrawLine(Thick, 195, top, 195, FrameBottom);
            _gfx.DrawLine(Thick, 195, top + 7, FrameRight, top + 7);
            _text.DrawInBox(_gfx, "Лист", 195, top, 10, 7, _sizes.TitleBlock, TextAlign.Center);
            _text.DrawInBox(_gfx, (_index + 1).ToString(), 195, top + 7, 10, 8, _sizes.TitleBlock, TextAlign.Center);
            _gfx.DrawLine(Thick, FrameLeft, top, FrameRight, top);
        }

        /// <summary>
        /// Левая часть основной надписи: две строки изменений, заголовок «Изм. | Лист | № докум. | Подп. | Дата»
        /// и (на первом листе) строки подписей.
        /// </summary>
        private void DrawChangeGrid(double top, bool signatureRows)
        {
            var header = top + 10;
            _gfx.DrawLine(Thin, FrameLeft, top + 5, 85, top + 5);
            _gfx.DrawLine(Thin, FrameLeft, header, 85, header);
            _gfx.DrawLine(Thick, FrameLeft, header + 5, 85, header + 5);
            foreach (var x in ChangeX.Skip(1))
            {
                _gfx.DrawLine(Thick, x, top, x, header + 5);
            }

            var titles = new[] { "Изм.", "Лист", "№ докум.", "Подп.", "Дата" };
            for (var i = 0; i < titles.Length; i++)
            {
                _text.DrawInBox(_gfx, titles[i], ChangeX[i], header, ChangeX[i + 1] - ChangeX[i], 5, _sizes.TitleBlock, TextAlign.Center);
            }

            if (!signatureRows)
            {
                return;
            }

            var signatures = new[]
            {
                ("Разраб.", _title.Developer), ("Пров.", _title.Checker), ("Т. контр.", _title.TechControl),
                ("Н. контр.", _title.NormControl), ("Утв.", _title.Approver),
            };
            var y = header + 5;
            foreach (var x in new double[] { 37, 60, 75, 85 })
            {
                _gfx.DrawLine(Thick, x, y, x, FrameBottom);
            }

            for (var i = 0; i < signatures.Length; i++)
            {
                var rowTop = y + 5 * i;
                if (i > 0)
                {
                    _gfx.DrawLine(Thin, FrameLeft, rowTop, 85, rowTop);
                }

                _text.DrawInBox(_gfx, signatures[i].Item1, 20, rowTop, 17, 5, _sizes.TitleBlock, TextAlign.Left);
                _text.DrawInBox(_gfx, signatures[i].Item2, 37, rowTop, 23, 5, _sizes.TitleBlock, TextAlign.Left);
            }
        }

        /// <summary>
        /// Записи изменений в две строки над заголовком, снизу вверх по порядку внесения; если записей больше,
        /// выводятся последние. На первом листе — все изменения документа, на остальных — изменения этого листа
        /// (по графе «Лист»).
        /// </summary>
        private void DrawChanges()
        {
            var changes = _title.Changes.Where(c => First || SheetMatches(c.Sheet, _index + 1)).TakeLast(2).ToList();
            var headerTop = TitleTop + 10;
            for (var i = 0; i < changes.Count; i++)
            {
                var rowTop = headerTop - 5 * (i + 1);
                var change = changes[i];
                var values = new[] { change.Number, change.Sheet, change.Document, change.Signature, change.Date };
                for (var c = 0; c < values.Length; c++)
                {
                    var align = c == 2 ? TextAlign.Left : TextAlign.Center;
                    _text.DrawInBox(_gfx, values[c], ChangeX[c], rowTop, ChangeX[c + 1] - ChangeX[c], 5, _sizes.TitleBlock, align);
                }
            }
        }

        /// <summary>Дополнительные графы на поле подшивки (ГОСТ 2.104, графы 19–23; на первом листе — и «Перв. примен.»).</summary>
        private void DrawMarginGraphs()
        {
            const double left = 8;
            const double labelWidth = 5;
            var graphs = new List<(double Height, string Label, string Value)>
            {
                (25, "Инв. № подл.", ""), (35, "Подп. и дата", ""), (25, "Взам. инв. №", ""),
                (25, "Инв. № дубл.", ""), (35, "Подп. и дата", ""),
            };

            var bottom = FrameBottom;
            foreach (var graph in graphs)
            {
                DrawMarginGraph(left, bottom - graph.Height, graph.Height, labelWidth, graph.Label, graph.Value);
                bottom -= graph.Height;
            }

            if (First)
            {
                DrawMarginGraph(left, FrameTop, 60, labelWidth, "Перв. примен.", _title.FirstUse);
                DrawMarginGraph(left, FrameTop + 60, 60, labelWidth, "Справ. №", "");
            }
        }

        private void DrawMarginGraph(double x, double y, double height, double labelWidth, string label, string value)
        {
            _gfx.DrawRectangle(Thick, x, y, FrameLeft - x, height);
            _gfx.DrawLine(Thick, x + labelWidth, y, x + labelWidth, y + height);
            _text.DrawVertical(_gfx, new[] { label }, x, y, labelWidth, height, _sizes.Margin);
            if (value.Length > 0)
            {
                _text.DrawVertical(_gfx, new[] { value }, x + labelWidth, y, FrameLeft - x - labelWidth, height, _sizes.Margin);
            }
        }
    }
}
