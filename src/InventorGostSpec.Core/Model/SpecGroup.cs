using InventorGostSpec.Core.Sections;

namespace InventorGostSpec.Core.Model;

/// <summary>
/// Группа составных частей, устанавливаемых по отдельному документу, например
/// «Изделия, устанавливаемые по электромонтажному чертежу АБВГ.301234.001МЭ».
/// Записывается после основной части спецификации со своими разделами; номера позиций продолжаются подряд.
/// </summary>
public sealed class SpecGroup
{
    public string Id { get; set; } = "G" + Guid.NewGuid().ToString("N")[..8];

    /// <summary>Заголовок группы в графе «Наименование».</summary>
    public string Title { get; set; } = "";

    /// <summary>Резерв позиций по разделам группы. Для незаданных разделов — как в основной части.</summary>
    public Dictionary<string, ReserveSettings> Reserves { get; set; } = new();

    /// <summary>Группа начинается с нового листа (в Excel перед её заголовком — строка «Новый лист»).</summary>
    public bool StartsNewPage { get; set; }
}

/// <summary>Раздел, вставленный вручную (в основную часть или в группу): показывается и без строк.</summary>
public sealed class ShownSection
{
    /// <summary>Группа; пусто — основная часть.</summary>
    public string GroupId { get; set; } = "";

    public string SectionId { get; set; } = "";

    public bool Matches(string groupId, string sectionId) =>
        GroupId == groupId && string.Equals(SectionId, sectionId, StringComparison.OrdinalIgnoreCase);
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

    /// <summary>
    /// Раздел показывается и без строк: вставлен вручную или это пользовательский раздел этой спецификации
    /// (в основной части).
    /// </summary>
    public static bool IsShownWhenEmpty(Specification spec, SpecBlock block) =>
        spec.ShownSections.Any(s => s.Matches(block.GroupId, block.Section.Id)) ||
        (block.GroupId.Length == 0 &&
         spec.CustomSections.Any(s => string.Equals(s.Id, block.Section.Id, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Есть ли у блока строки или он показывается и пустым.</summary>
    public static bool IsVisible(Specification spec, SpecBlock block) =>
        spec.Rows.Any(block.Contains) || IsShownWhenEmpty(spec, block);

    public static bool Same(SpecRow a, SpecRow b) =>
        a.GroupId == b.GroupId && string.Equals(a.SectionId, b.SectionId, StringComparison.OrdinalIgnoreCase);
}
