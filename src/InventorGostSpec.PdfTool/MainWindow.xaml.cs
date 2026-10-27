using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using InventorGostSpec.Pdf;

namespace InventorGostSpec.PdfTool;

/// <summary>Окно утилиты: выбор файла Excel, PDF и шрифта.</summary>
public partial class MainWindow : Window
{
    private const string AutoFont = "Автоматически";
    private readonly ToolSettings _settings = ToolSettings.Load();
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

        if (input is not null)
        {
            InputBox.Text = Path.GetFullPath(input);
        }
    }

    private void OnInputChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
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

        var font = FontBox.Text.Trim();
        _settings.Font = font == AutoFont ? "" : font;
        _settings.OpenAfter = OpenBox.IsChecked == true;
        _settings.Save();

        try
        {
            Cursor = System.Windows.Input.Cursors.Wait;
            var options = new PdfOptions { Font = _settings.Font.Length > 0 ? _settings.Font : null };
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
