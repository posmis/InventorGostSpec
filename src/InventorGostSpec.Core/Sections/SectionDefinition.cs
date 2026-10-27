namespace InventorGostSpec.Core.Sections;

/// <summary>Описание раздела спецификации.</summary>
public sealed class SectionDefinition
{
    /// <summary>Идентификатор раздела, хранится в iProperties компонентов.</summary>
    public string Id { get; set; } = "";

    /// <summary>Заголовок раздела в спецификации.</summary>
    public string Name { get; set; } = "";

    /// <summary>Порядок раздела. У встроенных разделов 100, 200, ... 800.</summary>
    public int Order { get; set; }

    public SortRule SortRule { get; set; } = SortRule.ByDesignation;

    /// <summary>Получают ли строки раздела номера позиций.</summary>
    public bool HasPositions { get; set; } = true;

    public bool IsBuiltIn { get; set; }

    public SectionDefinition Clone() => (SectionDefinition)MemberwiseClone();
}
