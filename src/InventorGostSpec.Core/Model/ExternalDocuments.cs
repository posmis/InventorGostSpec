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

    /// <summary>SHA-256 файла; файла нет или он недоступен — пусто.</summary>
    public static string Hash(string fullPath)
    {
        try
        {
            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return "";
        }
    }

    /// <summary>Полный путь файла документа строки (путь хранится относительно папки сборки).</summary>
    public static string FullPath(SpecRow row, string assemblyFolder) =>
        Path.GetFullPath(Path.Combine(assemblyFolder, row.DocumentFile));

    /// <summary>
    /// Отмечает строки, у которых файл документа изменился (<see cref="RowStatus.FileChanged"/>).
    /// Связь с файлом не меняется — это только уведомление. У строк без сохранённого хэша (добавлены
    /// до появления проверки) хэш запоминается. Файла нет — строка не отмечается. Возвращает число изменённых.
    /// </summary>
    public static int CheckFiles(Specification spec, string assemblyFolder)
    {
        var changed = 0;
        foreach (var row in spec.Rows.Where(r => r.DocumentFile.Length > 0))
        {
            var hash = Hash(FullPath(row, assemblyFolder));
            if (hash.Length == 0)
            {
                continue;
            }

            if (row.DocumentHash.Length == 0)
            {
                row.DocumentHash = hash;
            }
            else if (!string.Equals(row.DocumentHash, hash, StringComparison.OrdinalIgnoreCase))
            {
                row.Status |= RowStatus.FileChanged;
                changed++;
            }
            else
            {
                row.Status &= ~RowStatus.FileChanged;
            }
        }

        return changed;
    }

    /// <summary>Сброс уведомления: запоминается текущее содержимое файлов отмеченных строк.</summary>
    public static void AcceptFiles(Specification spec, string assemblyFolder)
    {
        foreach (var row in spec.Rows.Where(r => r.Status.HasFlag(RowStatus.FileChanged)))
        {
            if (Hash(FullPath(row, assemblyFolder)) is { Length: > 0 } hash)
            {
                row.DocumentHash = hash;
            }
        }
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
