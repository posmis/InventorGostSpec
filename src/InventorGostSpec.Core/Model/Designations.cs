using System.Text.RegularExpressions;

namespace InventorGostSpec.Core.Model;

/// <summary>Работа с обозначениями.</summary>
public static class Designations
{
    /// <summary>Код сборочного чертежа.</summary>
    public const string AssemblyDrawingCode = "СБ";

    /// <summary>
    /// Обозначение без кода сборочного чертежа: «АБВГ.301234.001СБ» → «АБВГ.301234.001».
    /// Используется только для спецификации: в файлах сборок «СБ» остаётся, чтобы чертежи получали правильное обозначение.
    /// </summary>
    public static string WithoutAssemblyDrawingCode(string? designation)
    {
        if (string.IsNullOrWhiteSpace(designation))
        {
            return "";
        }

        var trimmed = designation.Trim();
        return Regex.IsMatch(trimmed, @"[СC]Б$", RegexOptions.CultureInvariant)
            ? trimmed[..^2].TrimEnd()
            : trimmed;
    }

    /// <summary>
    /// Имя файла относится к документу с этим обозначением (или именем): начинается с него, а дальше конец, пробел, «_»
    /// или код «СБ». Другой номер не подходит: «…001» — не «…0010» и не исполнение «…001-01».
    /// </summary>
    public static bool IsFileOfDocument(string fileName, string designation)
    {
        designation = designation.Trim();
        if (designation.Length == 0 || !fileName.StartsWith(designation, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = fileName[designation.Length..];
        return rest.Length == 0 || rest[0] is ' ' or '_' || Regex.IsMatch(rest, @"^[СC]Б", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Базовое обозначение — без порядкового номера исполнения: «АБВГ.301234.001-01» → «АБВГ.301234.001»,
    /// «АБВГ.10.10.000-01» → «АБВГ.10.10.000». Без номера исполнения — само обозначение.
    /// </summary>
    public static string WithoutVersionNumber(string? designation)
    {
        var trimmed = (designation ?? "").Trim();
        var match = Regex.Match(trimmed, @"^(.+?)\s*[-–]\s*\d{2,3}$", RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value : trimmed;
    }

    /// <summary>Обозначение сборочного чертежа: «АБВГ.301234.001» и «АБВГ.301234.001СБ» → «АБВГ.301234.001СБ».</summary>
    public static string AssemblyDrawing(string? assemblyDesignation)
    {
        var designation = WithoutAssemblyDrawingCode(assemblyDesignation);
        return designation.Length > 0 ? designation + AssemblyDrawingCode : "";
    }
}
