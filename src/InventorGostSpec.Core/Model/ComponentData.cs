namespace InventorGostSpec.Core.Model;

public enum ComponentKind
{
    Part,
    Assembly,
    Virtual,

    /// <summary>Документ (например, сборочный чертёж), попадающий в раздел «Документация».</summary>
    Document,
}

/// <summary>
/// Данные компонента первого уровня сборки, прочитанные из Inventor.
/// Все вхождения одного компонента объединены, <see cref="Quantity"/> — их количество.
/// </summary>
public sealed class ComponentData
{
    /// <summary>
    /// Устойчивый ключ компонента внутри сборки: путь к файлу относительно сборки,
    /// для виртуальных компонентов — их имя.
    /// </summary>
    public string Key { get; set; } = "";

    public ComponentKind Kind { get; set; }

    public bool IsContentCenter { get; set; }

    /// <summary>Значение iProperty раздела. Пустое — раздел определяется автоматически.</summary>
    public string? SectionId { get; set; }

    /// <summary>Обозначение (Part Number).</summary>
    public string Designation { get; set; } = "";

    /// <summary>Наименование (Description).</summary>
    public string Name { get; set; } = "";

    public string Format { get; set; } = "";

    public decimal Quantity { get; set; }

    public string Unit { get; set; } = "";

    /// <summary>Однородная группа (для стандартных и прочих изделий).</summary>
    public string Group { get; set; } = "";

    /// <summary>Наименование изделия для сортировки, например «Болт».</summary>
    public string SortName { get; set; } = "";

    /// <summary>Обозначение стандарта, например «ГОСТ 7798-70».</summary>
    public string StandardNumber { get; set; } = "";

    /// <summary>Основные параметры или размеры, например «М12х60».</summary>
    public string Parameters { get; set; } = "";

    /// <summary>Вид материала, имя значения <see cref="Sections.MaterialKind"/>.</summary>
    public string MaterialKind { get; set; } = "";
}
