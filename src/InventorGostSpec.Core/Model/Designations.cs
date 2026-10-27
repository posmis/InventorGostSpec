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

    /// <summary>Обозначение сборочного чертежа: «АБВГ.301234.001» и «АБВГ.301234.001СБ» → «АБВГ.301234.001СБ».</summary>
    public static string AssemblyDrawing(string? assemblyDesignation)
    {
        var designation = WithoutAssemblyDrawingCode(assemblyDesignation);
        return designation.Length > 0 ? designation + AssemblyDrawingCode : "";
    }
}
