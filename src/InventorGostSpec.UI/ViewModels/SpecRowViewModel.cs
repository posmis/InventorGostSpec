using InventorGostSpec.Core.Model;
using InventorGostSpec.Core.View;

namespace InventorGostSpec.UI.ViewModels;

public enum SpecRowKind
{
    /// <summary>Заголовок группы «устанавливаемые по …».</summary>
    GroupHeader,

    Header,
    Component,
    ManualItem,
    Note,
    Reserve,
}

/// <summary>Строка таблицы спецификации: заголовок раздела, строка или резервная позиция.</summary>
public sealed class SpecRowViewModel : ObservableObject
{
    /// <summary>Колонки, у которых показывается подсветка переопределения и подсказки.</summary>
    public static readonly SpecColumn[] CellColumns =
    {
        SpecColumn.Format,
        SpecColumn.Zone,
        SpecColumn.Position,
        SpecColumn.Designation,
        SpecColumn.Name,
        SpecColumn.Quantity,
        SpecColumn.Unit,
        SpecColumn.Note,
        SpecColumn.SortGroup,
    };

    private readonly SpecificationViewModel _owner;
    private readonly DisplayRow _display;

    public SpecRowViewModel(SpecificationViewModel owner, DisplayRow display)
    {
        _owner = owner;
        _display = display;
        Kind = display.Kind switch
        {
            DisplayRowKind.GroupHeader => SpecRowKind.GroupHeader,
            DisplayRowKind.SectionHeader => SpecRowKind.Header,
            DisplayRowKind.Reserve => SpecRowKind.Reserve,
            _ => display.Row!.Kind switch
            {
                RowKind.Component => SpecRowKind.Component,
                RowKind.ManualItem => SpecRowKind.ManualItem,
                _ => SpecRowKind.Note,
            },
        };
        ComputeCells();
    }

    public SpecRowKind Kind { get; }

    public SpecRow? Row => _display.Row;

    public string? RowId => Row?.Id;

    public string SectionId => _display.SectionId;

    /// <summary>Группа «устанавливаемые по …»; пусто — основная часть.</summary>
    public string GroupId => _display.GroupId;

    /// <summary>Заголовок раздела или группы.</summary>
    public bool IsHeader => Kind is SpecRowKind.Header or SpecRowKind.GroupHeader;

    public bool IsGroupHeader => Kind == SpecRowKind.GroupHeader;

    public bool IsReserve => Kind == SpecRowKind.Reserve;

    public bool IsNote => Kind == SpecRowKind.Note;

    public bool IsManual => Kind is SpecRowKind.ManualItem or SpecRowKind.Note;

    public bool IsNew => Row?.Status.HasFlag(RowStatus.New) == true;

    public bool IsChanged => Row?.Status.HasFlag(RowStatus.Changed) == true;

    public bool IsRemoved => Row?.IsRemoved == true;

    /// <summary>Файл стороннего документа изменился после добавления — уведомление, связь с файлом остаётся.</summary>
    public bool IsFileChanged => Row?.Status.HasFlag(RowStatus.FileChanged) == true;

    /// <summary>Можно ли перетаскивать строку.</summary>
    public bool CanMove => Row is not null;

    public string Format { get => Get(SpecColumn.Format); set => Set(SpecColumn.Format, value); }

    public string Zone { get => Get(SpecColumn.Zone); set => Set(SpecColumn.Zone, value); }

    public string Position
    {
        get => IsReserve ? _display.ReservedPosition?.ToString() ?? "" : Get(SpecColumn.Position);
        set => Set(SpecColumn.Position, value);
    }

    public string Designation { get => Get(SpecColumn.Designation); set => Set(SpecColumn.Designation, value); }

    public string Name
    {
        get => IsHeader ? _display.SectionName : IsReserve ? "(резерв)" : Get(SpecColumn.Name);
        set
        {
            if (IsGroupHeader)
            {
                _owner.RenameGroup(GroupId, value ?? "");
            }
            else
            {
                Set(SpecColumn.Name, value);
            }
        }
    }

    public string Quantity { get => Get(SpecColumn.Quantity); set => Set(SpecColumn.Quantity, value); }

    public string Unit { get => Get(SpecColumn.Unit); set => Set(SpecColumn.Unit, value); }

    public string Note { get => Get(SpecColumn.Note); set => Set(SpecColumn.Note, value); }

    /// <summary>Группа сортировки (только эта спецификация).</summary>
    public string SortGroup { get => Get(SpecColumn.SortGroup); set => Set(SpecColumn.SortGroup, value); }

    /// <summary>Колонка (имя <see cref="SpecColumn"/>) переопределена вручную.</summary>
    public IReadOnlyDictionary<string, bool> Overridden { get; private set; } = new Dictionary<string, bool>();

    /// <summary>Подсказки к ячейкам: прежнее значение, значение компонента.</summary>
    public IReadOnlyDictionary<string, string?> ToolTips { get; private set; } = new Dictionary<string, string?>();

    public bool CanEdit(SpecColumn column) => Kind switch
    {
        SpecRowKind.GroupHeader => column == SpecColumn.Name,
        SpecRowKind.Header or SpecRowKind.Reserve => false,
        SpecRowKind.Note => column == SpecColumn.Name,

        // Номер позиции — только у строк, которые его получают (не в «Документации»).
        _ => column != SpecColumn.Position || _owner.HasPosition(Row!),
    };

    /// <summary>Обновляет все привязки строки после изменения данных.</summary>
    public void Refresh()
    {
        ComputeCells();
        OnPropertyChanged(string.Empty);
    }

    private string Get(SpecColumn column) => Row is null ? "" : Row.Get(column);

    private void Set(SpecColumn column, string value)
    {
        if (Row is not null && CanEdit(column) && (value ?? "") != Get(column))
        {
            _owner.SetCell(this, column, value ?? "");
        }
    }

    private void ComputeCells()
    {
        var overridden = new Dictionary<string, bool>();
        var toolTips = new Dictionary<string, string?>();
        foreach (var column in CellColumns)
        {
            var key = column.ToString();
            var isOverridden = Row?.IsOverridden(column) == true;
            overridden[key] = isOverridden;

            var tips = new List<string>();
            if (Row is not null && Row.PreviousValues.TryGetValue(column, out var previous))
            {
                tips.Add($"Было: {(previous.Length > 0 ? previous : "(пусто)")}");
            }

            if (isOverridden)
            {
                var source = Row!.Source.Get(column);
                tips.Add($"Изменено вручную. Значение компонента: {(source.Length > 0 ? source : "(пусто)")}");
            }

            if (IsFileChanged && column is SpecColumn.Designation or SpecColumn.Name)
            {
                tips.Add($"Файл документа изменён после добавления в спецификацию:\n{Row!.DocumentFile}\n«Сбросить уведомления» — снять отметку.");
            }

            if (IsRemoved && column == SpecColumn.Name)
            {
                tips.Add("Компонента нет в сборке. Строка удалится при «Принять изменения».");
            }

            toolTips[key] = tips.Count > 0 ? string.Join("\n", tips) : null;
        }

        Overridden = overridden;
        ToolTips = toolTips;
    }
}
