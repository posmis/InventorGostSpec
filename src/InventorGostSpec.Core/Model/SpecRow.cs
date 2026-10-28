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

    /// <summary>
    /// Составная часть другого исполнения (сборки-исполнения): показывается в «Переменных данных для исполнений»
    /// под этим исполнением, только для чтения. Данные — из сборки-исполнения при обновлении.
    /// </summary>
    VersionItem,
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

    /// <summary>Файл стороннего документа изменился после добавления (или после последнего «Сбросить уведомления»).</summary>
    FileChanged = 8,
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
    /// Группа сортировки (по умолчанию 0): внутри раздела строки с меньшим числом идут выше.
    /// Хранится только в этой спецификации.
    /// </summary>
    public int SortGroup { get; set; }

    /// <summary>Группа «устанавливаемые по …» (<see cref="SpecGroup.Id"/>). Пусто — основная часть.</summary>
    public string GroupId { get; set; } = "";

    /// <summary>
    /// Файл стороннего документа (PDF и т. п.) у ручной строки «Документации» — путь относительно папки сборки
    /// (или полный, если файл на другом диске). Пусто — строка не связана с файлом.
    /// </summary>
    public string DocumentFile { get; set; } = "";

    /// <summary>SHA-256 файла <see cref="DocumentFile"/> на момент добавления или сброса уведомлений.</summary>
    public string DocumentHash { get; set; } = "";

    /// <summary>
    /// Номер позиции берётся у той же составной части (того же файла) в постоянных данных или в предыдущих
    /// исполнениях (ГОСТ 2.113, п. 5.5.1 г). Такие строки не нумеруются сами и не занимают резерв.
    /// </summary>
    public bool InheritsPosition { get; set; }

    /// <summary>
    /// Строка исполнения заменяет составную часть с этим ключом (ПКМ «Заменяет поз. …»): получает её номер позиции,
    /// в том числе после перенумерации.
    /// </summary>
    public string? ReplacesKey { get; set; }

    /// <summary>
    /// Строку перенесли между постоянными данными и переменными данными основного исполнения вручную —
    /// сравнение состава исполнений её больше не переносит.
    /// </summary>
    public bool ManualVersionPlacement { get; set; }

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
        SpecColumn.SortGroup => Kind == RowKind.Note ? "" : SortGroup.ToString(),
        _ => Overrides.TryGetValue(column, out var value) ? value : Source.Get(column),
    };
}
