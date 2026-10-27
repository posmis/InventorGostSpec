using System.Diagnostics;

namespace InventorGostSpec.AddIn.Infrastructure;

/// <summary>
/// Вызов внешней утилиты PDF (InventorGostSpec.PdfTool.exe из папки PdfTool рядом с аддином).
/// Аддин связан с ней только форматом Excel и командной строкой.
/// </summary>
internal static class PdfToolRunner
{
    private const int TimeoutMs = 120_000;

    public static string ToolPath =>
        System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(PdfToolRunner).Assembly.Location) ?? "", "PdfTool", "InventorGostSpec.PdfTool.exe");

    /// <summary>Окно утилиты (настройки и вывод PDF), не дожидаясь его закрытия. <c>null</c> — открыто, иначе текст ошибки.</summary>
    public static string? OpenWindow(string? excelPath)
    {
        if (MissingTool() is { } missing)
        {
            return missing;
        }

        var start = new ProcessStartInfo(ToolPath) { UseShellExecute = false };
        if (excelPath is not null)
        {
            start.ArgumentList.Add(excelPath);
        }

        try
        {
            Process.Start(start)?.Dispose();
            return null;
        }
        catch (Exception ex)
        {
            Log.Error("PdfTool", ex);
            return ex.Message;
        }
    }

    private static string? MissingTool() =>
        System.IO.File.Exists(ToolPath)
            ? null
            : $"Не найдена утилита PDF:\n{ToolPath}\n\nПересоберите решение или скопируйте папку PdfTool в папку аддина.";

    /// <summary><c>null</c> — PDF создан, иначе текст ошибки.</summary>
    public static string? Run(string excelPath, string pdfPath)
    {
        if (MissingTool() is { } missing)
        {
            return missing;
        }

        var tool = ToolPath;

        var start = new ProcessStartInfo(tool)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        start.ArgumentList.Add(excelPath);
        start.ArgumentList.Add("--output");
        start.ArgumentList.Add(pdfPath);

        try
        {
            using var process = Process.Start(start)!;
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(TimeoutMs))
            {
                process.Kill();
                return "Утилита PDF не ответила за 2 минуты.";
            }

            return process.ExitCode == 0 ? null : error.Result.Trim() is { Length: > 0 } text ? text : $"Код ошибки {process.ExitCode}.";
        }
        catch (Exception ex)
        {
            Log.Error("PdfTool", ex);
            return ex.Message;
        }
    }
}
