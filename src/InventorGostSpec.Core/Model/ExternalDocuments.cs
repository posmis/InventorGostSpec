using InventorGostSpec.Core.Sorting;

namespace InventorGostSpec.Core.Model;

/// <summary>
/// Сторонние документы (PDF, Word… — не из Inventor) в «Документации»: обозначение берётся из имени файла
/// «Обозначение-Наименование-Прочее», наименование — тип документа по коду в обозначении.
/// </summary>
public static class ExternalDocuments
{
    /// <summary>
    /// Обозначение и наименование из имени файла. Разделитель — «-» (или « - »);
    /// исполнение в обозначении («АБВГ.301234.001-01ТУ») не отрывается: часть после дефиса, начинающаяся с цифры,
    /// остаётся в обозначении.
    /// </summary>
    public static (string Designation, string Name) ParseFileName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path[(path.LastIndexOfAny(new[] { '\\', '/' }) + 1)..]).Trim();
        var parts = name.Contains(" - ", StringComparison.Ordinal)
            ? name.Split(" - ")
            : name.Split('-');
        parts = parts.Select(p => p.Trim()).ToArray();

        var designation = parts[0];
        var next = 1;
        while (!name.Contains(" - ", StringComparison.Ordinal) && next < parts.Length && parts[next].Length > 0 && char.IsDigit(parts[next][0]))
        {
            designation += "-" + parts[next];
            next++;
        }

        return (designation, next < parts.Length ? parts[next] : "");
    }

    /// <summary>
    /// Значения строки документа: тип документа по коду («ТУ» — «Технические условия»),
    /// без кода — наименование из имени файла. Формат вписывается вручную, количество не пишется.
    /// </summary>
    public static ComponentSnapshot FromFile(string path)
    {
        var (designation, name) = ParseFileName(path);
        var type = DocumentCodes.NameOfDesignation(designation);
        return new ComponentSnapshot
        {
            SectionId = Sections.SectionIds.Documentation,
            Designation = designation,
            Name = type.Length > 0 ? type : name,
        };
    }
}
