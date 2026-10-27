namespace InventorGostSpec.Core.Sections;

/// <summary>Упорядоченный набор разделов: встроенные по ГОСТ и пользовательские.</summary>
public sealed class SectionCatalog
{
    private const int UnknownSectionOrder = 10_000;

    private readonly List<SectionDefinition> _sections = new();

    public SectionCatalog(IEnumerable<SectionDefinition> sections)
    {
        foreach (var section in sections)
        {
            Add(section);
        }
    }

    public IReadOnlyList<SectionDefinition> Sections => _sections;

    /// <summary>Каталог только со встроенными разделами ГОСТ 2.106.</summary>
    public static SectionCatalog CreateDefault() => new(CreateBuiltIn());

    public static IReadOnlyList<SectionDefinition> CreateBuiltIn() => new[]
    {
        BuiltIn(SectionIds.Documentation, "Документация", 100, SortRule.Documentation, hasPositions: false),
        BuiltIn(SectionIds.Complexes, "Комплексы", 200, SortRule.ByDesignation),
        BuiltIn(SectionIds.AssemblyUnits, "Сборочные единицы", 300, SortRule.ByDesignation),
        BuiltIn(SectionIds.Parts, "Детали", 400, SortRule.ByDesignation),
        BuiltIn(SectionIds.StandardProducts, "Стандартные изделия", 500, SortRule.StandardProducts),
        BuiltIn(SectionIds.OtherProducts, "Прочие изделия", 600, SortRule.OtherProducts),
        BuiltIn(SectionIds.Materials, "Материалы", 700, SortRule.Materials),
        BuiltIn(SectionIds.Kits, "Комплекты", 800, SortRule.ByDesignation),
    };

    public SectionDefinition? Find(string id) =>
        _sections.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Возвращает раздел по идентификатору. Неизвестный идентификатор (например, раздел удалили
    /// из настроек, а в iProperties он остался) добавляется в конец, чтобы строки не потерялись.
    /// </summary>
    public SectionDefinition GetOrCreate(string id)
    {
        var section = Find(id);
        if (section is not null)
        {
            return section;
        }

        section = new SectionDefinition
        {
            Id = id,
            Name = id,
            Order = UnknownSectionOrder + _sections.Count,
            SortRule = SortRule.ByDesignation,
        };
        Add(section);
        return section;
    }

    /// <summary>Индекс раздела в порядке следования. Неизвестные разделы добавляются в конец.</summary>
    public int IndexOf(string id) => _sections.IndexOf(GetOrCreate(id));

    private void Add(SectionDefinition section)
    {
        if (string.IsNullOrWhiteSpace(section.Id))
        {
            throw new ArgumentException("Section id must not be empty.", nameof(section));
        }

        var existing = Find(section.Id);
        if (existing is not null)
        {
            // Встроенные разделы переопределять нельзя, пользовательские заменяются более поздними.
            if (existing.IsBuiltIn)
            {
                return;
            }

            _sections.Remove(existing);
        }

        var index = _sections.FindIndex(s => s.Order > section.Order);
        _sections.Insert(index < 0 ? _sections.Count : index, section);
    }

    private static SectionDefinition BuiltIn(string id, string name, int order, SortRule rule, bool hasPositions = true) =>
        new()
        {
            Id = id,
            Name = name,
            Order = order,
            SortRule = rule,
            HasPositions = hasPositions,
            IsBuiltIn = true,
        };
}
