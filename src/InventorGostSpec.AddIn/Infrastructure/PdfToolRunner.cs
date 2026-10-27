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

    /// <summary><c>null</c> — PDF создан, иначе текст ошибки.</summary>
    public static string? Run(string excelPath, string pdfPath)
    {
        var tool = ToolPath;
        if (!System.IO.File.Exists(tool))
        {
            return $"Не найдена утилита PDF:\n{tool}\n\nПересоберите решение или скопируйте папку PdfTool в папку аддина.";
        }

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
