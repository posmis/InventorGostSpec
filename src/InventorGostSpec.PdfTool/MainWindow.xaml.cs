using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using InventorGostSpec.Pdf;

namespace InventorGostSpec.PdfTool;

/// <summary>Окно утилиты: выбор файла Excel, PDF и шрифта.</summary>
public partial class MainWindow : Window
{
    private const string AutoFont = "Автоматически";
    private readonly PdfSettings _settings = PdfSettings.Load();
    private string _suggestedOutput = "";

    public MainWindow(string? input)
    {
        InitializeComponent();

        var installed = FontLocator.InstalledGostFamilies();
        var bundled = FontLocator.Bundled();
        FontBox.Items.Add(AutoFont);
        if (bundled is not null)
        {
            FontBox.Items.Add(bundled.Family);
        }

        foreach (var family in installed)
        {
            FontBox.Items.Add(family);
        }

        FontBox.Items.Add("Arial");
        FontBox.Text = _settings.Font.Length > 0 ? _settings.Font : AutoFont;
        FontHint.Text = bundled is not null
            ? $"Автоматически — «{bundled.Family}» (поставляется с утилитой)."
            : installed.Count > 0
                ? $"Найдены шрифты ГОСТ: {string.Join(", ", installed)}."
                : "Шрифты ГОСТ не найдены — будет использован Arial. Укажите путь к .ttf.";
        OpenBox.IsChecked = _settings.OpenAfter;
        CreateSizeFields();
        ShowSizes();

        if (input is not null)
        {
            InputBox.Text = Path.GetFullPath(input);
        }
    }

    private void OnInputChanged(object sender, TextChangedEventArgs e)
    {
        // PDF по умолчанию — рядом с Excel, пока пользователь не выбрал другой.
        if (OutputBox.Text.Length == 0 || OutputBox.Text == _suggestedOutput)
        {
            _suggestedOutput = InputBox.Text.Trim().Length > 0 ? Path.ChangeExtension(InputBox.Text.Trim(), ".pdf") : "";
            OutputBox.Text = _suggestedOutput;
        }
    }

    private void OnBrowseInput(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Файл Excel спецификации",
            Filter = "Книга Excel (*.xlsx)|*.xlsx",
        };
        if (dialog.ShowDialog(this) == true)
        {
            InputBox.Text = dialog.FileName;
        }
    }

    private void OnBrowseOutput(object sender, RoutedEventArgs e)
    {
        var current = OutputBox.Text.Trim();
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Сохранить PDF",
            Filter = "Документ PDF (*.pdf)|*.pdf",
            DefaultExt = ".pdf",
            FileName = Path.GetFileName(current),
            InitialDirectory = current.Length > 0 ? Path.GetDirectoryName(current) : null,
        };
        if (dialog.ShowDialog(this) == true)
        {
            OutputBox.Text = dialog.FileName;
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DroppedFile(e) is null ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (DroppedFile(e) is { } file)
        {
            InputBox.Text = file;
        }
    }

    private static string? DroppedFile(DragEventArgs e) =>
        e.Data.GetData(DataFormats.FileDrop) is string[] files
            ? files.FirstOrDefault(f => f.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            : null;

    private void OnCreate(object sender, RoutedEventArgs e)
    {
        var input = InputBox.Text.Trim();
        var output = OutputBox.Text.Trim();
        if (!File.Exists(input))
        {
            ShowStatus("Укажите существующий файл Excel.", error: true);
            return;
        }

        if (output.Length == 0)
        {
            ShowStatus("Укажите файл PDF.", error: true);
            return;
        }

        if (!SaveSettings())
        {
            return;
        }

        try
        {
            Cursor = System.Windows.Input.Cursors.Wait;
            var options = _settings.ToOptions();
            var pages = Program.Convert(input, output, options);
            var usedFont = FontLocator.Find(options.Font)?.Family ?? "";
            ShowStatus($"Готово: {pages} {SheetWord(pages)}, шрифт {usedFont}.\n{output}", error: false);
            if (_settings.OpenAfter)
            {
                Process.Start(new ProcessStartInfo(output) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            ShowStatus(Program.Describe(ex), error: true);
        }
        finally
        {
            Cursor = null;
        }
    }

    private void OnMarkSheets(object sender, RoutedEventArgs e)
    {
        var input = InputBox.Text.Trim();
        if (!File.Exists(input))
        {
            ShowStatus("Укажите существующий файл Excel.", error: true);
            return;
        }

        if (!SaveSettings())
        {
            return;
        }

        try
        {
            Cursor = System.Windows.Input.Cursors.Wait;
            var pages = ExcelSheetMarker.Mark(input, _settings.ToOptions());
            ShowStatus($"Листы размечены в Excel: {pages} {SheetWord(pages)}. Границы — жирная линия, номер — в колонке «Лист PDF».", error: false);
        }
        catch (IOException ex)
        {
            ShowStatus($"Не удалось записать файл Excel. Закройте его в Excel и повторите.\n{ex.Message}", error: true);
        }
        catch (Exception ex)
        {
            ShowStatus(Program.Describe(ex), error: true);
        }
        finally
        {
            Cursor = null;
        }
    }

    /// <summary>Сохраняет настройки окна; <c>false</c> — в размерах ошибка (показана в строке состояния).</summary>
    private bool SaveSettings()
    {
        var sizes = new FontSizes();
        foreach (var (box, _, set) in _sizeFields)
        {
            if (!TryParseNumber(box.Text, out var value) || value <= 0 || value >= 100)
            {
                ShowStatus($"Неверный размер шрифта: «{box.Text}». Укажите число мм, например 3,5.", error: true);
                box.Focus();
                return false;
            }

            set(sizes, value);
        }

        if (!TryParseNumber(MinWidthBox.Text, out var percent) || percent < 0 || percent > 100)
        {
            ShowStatus("Допустимое сужение — число от 0 до 100 %.", error: true);
            MinWidthBox.Focus();
            return false;
        }

        var font = FontBox.Text.Trim();
        _settings.Font = font == AutoFont ? "" : font;
        _settings.OpenAfter = OpenBox.IsChecked == true;
        _settings.Sizes = sizes;
        _settings.MinWidthPercent = (int)Math.Round(percent);
        _settings.Save();
        return true;
    }

    /// <summary>Поля размеров: поле ввода, чтение из настроек, запись в настройки.</summary>
    private readonly List<(TextBox Box, Func<FontSizes, double> Get, Action<FontSizes, double> Set)> _sizeFields = new();

    private void CreateSizeFields()
    {
        void Add(string label, Func<FontSizes, double> get, Action<FontSizes, double> set)
        {
            var panel = new DockPanel { Margin = new Thickness(0, 2, 12, 2) };
            var box = new TextBox { Width = 50 };
            DockPanel.SetDock(box, Dock.Right);
            panel.Children.Add(box);
            panel.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(Color.FromRgb(0x37, 0x41, 0x51)) });
            SizesGrid.Children.Add(panel);
            _sizeFields.Add((box, get, set));
        }

        Add("Строки таблицы", s => s.TableRows, (s, v) => s.TableRows = v);
        Add("Заголовки граф", s => s.ColumnTitles, (s, v) => s.ColumnTitles = v);
        Add("Вертикальные заголовки", s => s.VerticalTitles, (s, v) => s.VerticalTitles = v);
        Add("Обозначение документа", s => s.Designation, (s, v) => s.Designation = v);
        Add("Наименование изделия", s => s.Name, (s, v) => s.Name = v);
        Add("Организация", s => s.Organization, (s, v) => s.Organization = v);
        Add("Основная надпись", s => s.TitleBlock, (s, v) => s.TitleBlock = v);
        Add("Поле подшивки, «Копировал»", s => s.Margin, (s, v) => s.Margin = v);
    }

    private void ShowSizes()
    {
        var sizes = (_settings.Sizes ?? new FontSizes()).Normalized();
        foreach (var (box, get, _) in _sizeFields)
        {
            box.Text = get(sizes).ToString("0.##", Russian);
        }

        MinWidthBox.Text = Math.Clamp(_settings.MinWidthPercent, 0, 100).ToString(Russian);
    }

    private void OnResetSizes(object sender, RoutedEventArgs e)
    {
        _settings.ResetSizes();
        ShowSizes();
        ShowStatus("Размеры шрифтов и сужение — по умолчанию. Сохранятся при создании PDF или разметке листов.", error: false);
    }

    private static readonly System.Globalization.CultureInfo Russian = System.Globalization.CultureInfo.GetCultureInfo("ru-RU");

    private static bool TryParseNumber(string text, out double value) =>
        double.TryParse(text.Trim().Replace('.', ','), System.Globalization.NumberStyles.Float, Russian, out value);

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void ShowStatus(string text, bool error)
    {
        StatusPanel.Visibility = Visibility.Visible;
        StatusText.Text = text;
        StatusText.Foreground = error ? new SolidColorBrush(Color.FromRgb(0xB9, 0x1C, 0x1C)) : new SolidColorBrush(Color.FromRgb(0x16, 0x65, 0x34));
    }

    private static string SheetWord(int count) =>
        (count % 10, count % 100) switch
        {
            (1, not 11) => "лист",
            (2 or 3 or 4, not (12 or 13 or 14)) => "листа",
            _ => "листов",
        };
}
