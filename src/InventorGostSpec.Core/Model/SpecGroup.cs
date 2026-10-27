using InventorGostSpec.Core.Sections;

namespace InventorGostSpec.Core.Model;

/// <summary>
/// Группа составных частей, устанавливаемых по отдельному документу, например
/// «Изделия, устанавливаемые по электромонтажному чертежу АБВГ.301234.001МЭ».
/// Записывается после основной части спецификации со своими разделами; номера позиций продолжаются.
/// </summary>
public sealed class SpecGroup
{
    public string Id { get; set; } = "G" + Guid.NewGuid().ToString("N")[..8];

    /// <summary>Заголовок группы в графе «Наименование».</summary>
    public string Title { get; set; } = "";

    /// <summary>Номер первой позиции группы (например, 101). Пусто — сразу после основной части с учётом резерва.</summary>
    public int? FirstPosition { get; set; }

    /// <summary>Резерв позиций по разделам группы. Для незаданных разделов — как в основной части.</summary>
    public Dictionary<string, ReserveSettings> Reserves { get; set; } = new();
}

/// <summary>Блок строк спецификации: раздел основной части или раздел группы.</summary>
/// <param name="GroupId">Пусто — основная часть.</param>
public sealed record SpecBlock(string GroupId, SectionDefinition Section)
{
    public bool Contains(SpecRow row) =>
        row.GroupId == GroupId && string.Equals(row.SectionId, Section.Id, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Порядок блоков: разделы основной части, затем разделы каждой группы.</summary>
public static class SpecBlocks
{
    public static IReadOnlyList<SpecBlock> All(Specification spec, SpecificationContext context)
    {
        // Регистрируем разделы строк, которых нет в каталоге, чтобы строки не пропали.
        foreach (var row in spec.Rows)
        {
            context.Catalog.GetOrCreate(row.SectionId);
        }

        return new[] { "" }
            .Concat(context.Groups.Select(g => g.Id))
            .SelectMany(groupId => context.Catalog.Sections.Select(section => new SpecBlock(groupId, section)))
            .ToList();
    }

    /// <summary>Порядковый номер блока строки (для сортировки).</summary>
    public static int Order(SpecRow row, SpecificationContext context) =>
        context.GroupIndex(row.GroupId) * 100_000 + context.Catalog.IndexOf(row.SectionId);

    public static bool Same(SpecRow a, SpecRow b) =>
        a.GroupId == b.GroupId && string.Equals(a.SectionId, b.SectionId, StringComparison.OrdinalIgnoreCase);
}
