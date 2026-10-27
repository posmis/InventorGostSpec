using System.Text.Json.Serialization;

namespace InventorGostSpec.Core.Model;

/// <summary>Колонки спецификации (плюс служебные «Раздел» и «Единица»).</summary>
public enum SpecColumn
{
    Section,
    Format,
    Zone,
    Position,
    Designation,
    Name,
    Quantity,
    Unit,
    Note,

    /// <summary>Группа сортировки (служебная колонка, только в этой спецификации).</summary>
    SortGroup,
}

public enum RowKind
{
    /// <summary>Строка компонента сборки.</summary>
    Component,

    /// <summary>Строка, добавленная вручную, с номером позиции (например, изделие без модели).</summary>
    ManualItem,

    /// <summary>Текстовая строка без позиции (пишется в колонке «Наименование»).</summary>
    Note,
}

[Flags]
public enum RowStatus
{
    None = 0,

    /// <summary>Компонент появился при последнем обновлении.</summary>
    New = 1,

    /// <summary>Данные компонента изменились при последнем обновлении.</summary>
    Changed = 2,

    /// <summary>Компонента больше нет в сборке.</summary>
    Removed = 4,
}

public sealed class SpecRow
{
    /// <summary>Колонки, значения которых берутся из компонента и могут быть переопределены.</summary>
    public static readonly IReadOnlyList<SpecColumn> SourceColumns = new[]
    {
        SpecColumn.Section,
        SpecColumn.Format,
        SpecColumn.Designation,
        SpecColumn.Name,
        SpecColumn.Quantity,
        SpecColumn.Unit,
    };

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public RowKind Kind { get; set; }

    /// <summary>Ключ компонента (<see cref="ComponentData.Key"/>), только для строк компонентов.</summary>
    public string? ComponentKey { get; set; }

    public ComponentSnapshot Source { get; set; } = new();

    /// <summary>Ручные значения, заменяющие значения компонента только в этой спецификации.</summary>
    public Dictionary<SpecColumn, string> Overrides { get; set; } = new();

    public int? Position { get; set; }

    public string Zone { get; set; } = "";

    /// <summary>Примечание. Хранится только в спецификации, не в компоненте.</summary>
    public string Note { get; set; } = "";

    public RowStatus Status { get; set; }

    /// <summary>
    /// Группа сортировки, заданная вручную: внутри раздела строки с меньшим числом идут выше,
    /// строки без группы — после всех групп. Хранится только в этой спецификации.
    /// </summary>
    public int? SortGroup { get; set; }

    /// <summary>Группа «устанавливаемые по …» (<see cref="SpecGroup.Id"/>). Пусто — основная часть.</summary>
    public string GroupId { get; set; } = "";

    /// <summary>Значения до последнего обновления для колонок, которые изменились.</summary>
    public Dictionary<SpecColumn, string> PreviousValues { get; set; } = new();

    [JsonIgnore]
    public string SectionId => Get(SpecColumn.Section);

    [JsonIgnore]
    public bool IsRemoved => Status.HasFlag(RowStatus.Removed);

    /// <summary>Может ли строка иметь номер позиции (без учёта раздела).</summary>
    [JsonIgnore]
    public bool CanHavePosition => Kind != RowKind.Note;

    public bool IsOverridden(SpecColumn column) => Overrides.ContainsKey(column);

    /// <summary>Значение ячейки с учётом ручных переопределений.</summary>
    public string Get(SpecColumn column) => column switch
    {
        SpecColumn.Position => Position?.ToString() ?? "",
        SpecColumn.Zone => Zone,
        SpecColumn.Note => Note,
        SpecColumn.SortGroup => SortGroup?.ToString() ?? "",
        _ => Overrides.TryGetValue(column, out var value) ? value : Source.Get(column),
    };
}
