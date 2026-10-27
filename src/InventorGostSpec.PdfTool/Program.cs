using System.Diagnostics;
using System.IO;
using System.Windows;
using InventorGostSpec.Excel;
using InventorGostSpec.Pdf;

namespace InventorGostSpec.PdfTool;

/// <summary>
/// Запуск:
/// <list type="bullet">
/// <item><c>InventorGostSpec.PdfTool.exe</c> — окно (можно перетащить на exe файл Excel);</item>
/// <item><c>InventorGostSpec.PdfTool.exe файл.xlsx --output файл.pdf [--font "GOST type B"] [--italic] [--open]</c> —
/// без окна, для вызова из аддина: код возврата 0 — успех, иначе текст ошибки в stderr.</item>
/// </list>
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Ошибки для аддина — в UTF-8 независимо от кодовой страницы консоли (у WinExe её нет).
        Console.SetError(new StreamWriter(Console.OpenStandardError(), new System.Text.UTF8Encoding(false)) { AutoFlush = true });

        var arguments = Arguments.Parse(args);
        if (arguments.Error is { } error)
        {
            Console.Error.WriteLine(error);
            return 2;
        }

        if (arguments.Output is null)
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            return app.Run(new MainWindow(arguments.Input));
        }

        try
        {
            var settings = ToolSettings.Load();
            var options = new PdfOptions
            {
                Font = arguments.Font ?? (settings.Font.Length > 0 ? settings.Font : null),
                Italic = arguments.Italic || settings.Italic,
            };
            Convert(arguments.Input!, arguments.Output, options);
            if (arguments.Open)
            {
                Process.Start(new ProcessStartInfo(arguments.Output) { UseShellExecute = true });
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(Describe(ex));
            return 1;
        }
    }

    /// <summary>Excel → PDF. Возвращает число листов.</summary>
    public static int Convert(string input, string output, PdfOptions options)
    {
        var file = ExcelReader.Read(input);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        return GostPdfWriter.Write(file, options, output);
    }

    /// <summary>Понятный текст ошибки: занятый файл, нет листа и т. п.</summary>
    public static string Describe(Exception ex) => ex switch
    {
        FileNotFoundException notFound => $"Файл не найден: {notFound.FileName}",
        IOException => $"Не удалось прочитать или записать файл. Возможно, PDF открыт в другой программе.\n{ex.Message}",
        _ => ex.Message,
    };

    private sealed class Arguments
    {
        public string? Input { get; private set; }
        public string? Output { get; private set; }
        public string? Font { get; private set; }
        public bool Italic { get; private set; }
        public bool Open { get; private set; }
        public string? Error { get; private set; }

        public static Arguments Parse(string[] args)
        {
            var result = new Arguments();
            for (var i = 0; i < args.Length; i++)
            {
                string? Next()
                {
                    if (i + 1 < args.Length)
                    {
                        return args[++i];
                    }

                    result.Error = $"Не указано значение параметра {args[i]}.";
                    return null;
                }

                switch (args[i])
                {
                    case "-o" or "--output":
                        result.Output = Next();
                        break;
                    case "--font":
                        result.Font = Next();
                        break;
                    case "--italic":
                        result.Italic = true;
                        break;
                    case "--open":
                        result.Open = true;
                        break;
                    default:
                        if (args[i].StartsWith('-') || result.Input is not null)
                        {
                            result.Error = $"Неизвестный параметр: {args[i]}";
                            return result;
                        }

                        result.Input = args[i];
                        break;
                }

                if (result.Error is not null)
                {
                    return result;
                }
            }

            if (result.Output is not null && result.Input is null)
            {
                result.Error = "Не указан файл Excel.";
            }

            return result;
        }
    }
}
