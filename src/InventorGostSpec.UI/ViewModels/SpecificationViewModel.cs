using System.Collections.ObjectModel;
using System.Windows.Input;
using InventorGostSpec.Core.Editing;
using InventorGostSpec.Core.Model;
using InventorGostSpec.Core.Positions;
using InventorGostSpec.Core.Storage;
using InventorGostSpec.Core.Update;
using InventorGostSpec.Core.View;

namespace InventorGostSpec.UI.ViewModels;

/// <summary>
/// Окно спецификации. Работает с копией спецификации: в сборку она записывается только командой «Сохранить».
/// </summary>
public sealed class SpecificationViewModel : ObservableObject
{
    private readonly ISpecificationHost _host;
    private readonly IDialogService _dialogs;
    private readonly Action<Action> _post;
    private SpecSettings _settings;
    private SpecificationContext _context;
    private SpecRowViewModel? _selectedRow;
    private bool _isDirty;
    private string _statusText = "";

    /// <param name="isNew">Спецификация только что сформирована и ещё не сохранена: закрытие без сохранения её потеряет.</param>
    /// <param name="post">
    /// Отложенный вызов (после завершения редактирования ячейки). По умолчанию — через контекст синхронизации UI-потока.
    /// </param>
    public SpecificationViewModel(
        ISpecificationHost host,
        IDialogService dialogs,
        Specification specification,
        bool isNew = false,
        Action<Action>? post = null)
    {
        _host = host;
        _dialogs = dialogs;
        _post = post ?? CreateDefaultPost();
        _settings = host.LoadSettings();
        Specification = SpecificationSerializer.Deserialize(SpecificationSerializer.Serialize(specification));
        _context = CreateContext();

        UpdateFromAssemblyCommand = new RelayCommand(UpdateFromAssembly);
        ResetCommand = new RelayCommand(Reset);
        RenumberCommand = new RelayCommand(Renumber);
        AcceptChangesCommand = new RelayCommand(AcceptChanges, () => Specification.Rows.Any(r => r.Status != RowStatus.None));
        AddNoteCommand = new RelayCommand(AddNote, () => SelectedRow is not null);
        AddItemCommand = new RelayCommand(AddItem, () => SelectedRow is not null);
        DeleteRowCommand = new RelayCommand(DeleteRow, () => SelectedRow?.Row is not null);
        MoveUpCommand = new RelayCommand(() => MoveBy(-1), () => SelectedRow?.CanMove == true);
        MoveDownCommand = new RelayCommand(() => MoveBy(+1), () => SelectedRow?.CanMove == true);
        ClearOverrideCommand = new RelayCommand(ClearOverride, p => SelectedRow?.Row is { Kind: RowKind.Component } row && ParseColumn(p) is { } c && row.IsOverridden(c));
        MoveToSectionCommand = new RelayCommand(MoveToSection, p => SelectedRow?.Row is { Kind: not RowKind.Note } && p is string);
        AddSectionCommand = new RelayCommand(AddSection);
        ReservesCommand = new RelayCommand(EditReserves);
        SaveCommand = new RelayCommand(Save, () => IsDirty);
        WritePositionsCommand = new RelayCommand(WritePositions);

        Rebuild();
        _isDirty = isNew;
    }

    /// <summary>
    /// Завершает редактирование ячейки в таблице. Вызывается перед перестроением строк:
    /// таблицу нельзя перестраивать, пока ячейка редактируется.
    /// </summary>
    public Action? CommitEdits { get; set; }

    /// <summary>Запрос начать редактирование ячейки (например, у только что добавленной строки).</summary>
    public event Action<SpecRowViewModel, SpecColumn>? EditRequested;

    public Specification Specification { get; }

    public ObservableCollection<SpecRowViewModel> Rows { get; } = new();

    /// <summary>Разделы для команды «Перенести в раздел».</summary>
    public ObservableCollection<Option> SectionOptions { get; } = new();

    public string Title => $"Спецификация — {_host.DocumentName}{(IsDirty ? " *" : "")}";

    public SpecRowViewModel? SelectedRow
    {
        get => _selectedRow;
        set => SetField(ref _selectedRow, value);
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (SetField(ref _isDirty, value))
            {
                OnPropertyChanged(nameof(Title));
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public bool ShowReserveRows
    {
        get => Specification.ShowReserveRows;
        set
        {
            if (Specification.ShowReserveRows != value)
            {
                Specification.ShowReserveRows = value;
                OnPropertyChanged();
                Changed(rebuild: true);
            }
        }
    }

    public ICommand UpdateFromAssemblyCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand RenumberCommand { get; }
    public ICommand AcceptChangesCommand { get; }
    public ICommand AddNoteCommand { get; }
    public ICommand AddItemCommand { get; }
    public ICommand DeleteRowCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public ICommand ClearOverrideCommand { get; }
    public ICommand MoveToSectionCommand { get; }
    public ICommand AddSectionCommand { get; }
    public ICommand ReservesCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand WritePositionsCommand { get; }

    /// <summary>Вызывается при закрытии окна. <c>false</c> — закрытие отменено.</summary>
    public bool CanClose()
    {
        if (!IsDirty)
        {
            return true;
        }

        var answer = _dialogs.AskSave();
        if (answer == true)
        {
            Save();
        }

        return answer is not null;
    }

    /// <summary>Перетаскивание строки на место другой строки того же раздела.</summary>
    public void MoveRow(string rowId, string targetRowId)
    {
        if (rowId == targetRowId)
        {
            return;
        }

        if (!SpecificationEditor.MoveRow(Specification, rowId, targetRowId))
        {
            _dialogs.Warning("Строку можно перемещать только внутри её раздела. Чтобы перенести в другой раздел, используйте «Перенести в раздел».");
            return;
        }

        Changed(rebuild: true, selectRowId: rowId);
    }

    internal void SetCell(SpecRowViewModel rowViewModel, SpecColumn column, string value)
    {
        var row = rowViewModel.Row!;
        value = value.Trim();

        if (column == SpecColumn.Position && value.Length > 0)
        {
            if (!int.TryParse(value, out var number) || number <= 0)
            {
                _dialogs.Warning("Номер позиции должен быть целым положительным числом.");
                rowViewModel.Refresh();
                return;
            }

            var owner = Specification.Rows.FirstOrDefault(r => r != row && r.Position == number);
            if (owner is not null)
            {
                _dialogs.Warning($"Позиция {number} уже занята: {Describe(owner)}.");
                rowViewModel.Refresh();
                return;
            }
        }

        SpecificationEditor.SetCell(Specification, _context, row, column, value);
        if (column is SpecColumn.Position or SpecColumn.Section)
        {
            // Меняются резервные строки или место строки: перестраиваем таблицу после завершения редактирования.
            Changed(rebuild: false);
            PostRebuild(row.Id);
        }
        else
        {
            Changed(rebuild: false);
            rowViewModel.Refresh();
        }
    }

    private void UpdateFromAssembly()
    {
        var components = _host.ReadComponents();
        var result = SpecificationUpdater.Update(Specification, components, _context, DateTime.UtcNow);
        Changed(rebuild: true);

        if (result.IsInitialBuild)
        {
            StatusText = $"Спецификация сформирована: {Specification.Rows.Count} строк. " + StatusText;
        }
        else if (!result.HasChanges)
        {
            _dialogs.Info("Изменений в составе сборки нет.");
        }

        if (result.Positions.ExhaustedSectionIds.Count > 0)
        {
            var names = result.Positions.ExhaustedSectionIds.Select(id => _context.Catalog.GetOrCreate(id).Name);
            _dialogs.Warning(
                $"Не хватило резервных номеров позиций в разделах: {string.Join(", ", names)}.\n" +
                "Строки остались без номера. Перенумеруйте спецификацию или увеличьте резерв.");
        }
    }

    private void Reset()
    {
        if (_dialogs.Confirm(
                "Сбросить ручной порядок строк?\n\n" +
                "Строки будут отсортированы по ГОСТ 2.106, позиции перенумерованы с учётом резерва. " +
                "Ручные строки, примечания и изменённые значения сохранятся."))
        {
            SpecificationEditor.Reset(Specification, _context);
            Changed(rebuild: true);
        }
    }

    private void Renumber()
    {
        if (_dialogs.Confirm("Перенумеровать позиции в текущем порядке строк с учётом резерва?\n\nНомера на выносках чертежа изменятся после «Позиции в BOM»."))
        {
            PositionAllocator.RenumberAll(Specification, _context);
            Changed(rebuild: true);
        }
    }

    private void AcceptChanges()
    {
        var removed = Specification.Rows.Count(r => r.Kind == RowKind.Component && r.IsRemoved);
        if (removed > 0 && !_dialogs.Confirm($"Снять подсветку изменений и удалить строки компонентов, которых нет в сборке ({removed})?"))
        {
            return;
        }

        SpecificationEditor.AcceptChanges(Specification);
        Changed(rebuild: true);
    }

    private void AddNote()
    {
        var selected = SelectedRow!;
        var row = SpecificationEditor.AddNote(Specification, _context, selected.SectionId, "", selected.RowId);
        Changed(rebuild: true, selectRowId: row.Id);
        RequestEdit(row.Id, SpecColumn.Name);
    }

    private void AddItem()
    {
        var selected = SelectedRow!;
        var values = new ComponentSnapshot { SectionId = selected.SectionId, Quantity = "1" };
        var row = SpecificationEditor.AddManualItem(Specification, _context, values, selected.RowId);
        Changed(rebuild: true, selectRowId: row.Id);
        RequestEdit(row.Id, SpecColumn.Name);
    }

    private void DeleteRow()
    {
        var row = SelectedRow!.Row!;
        if (row.Kind == RowKind.Component && !row.IsRemoved)
        {
            _dialogs.Warning("Строку компонента удалить нельзя: компонент есть в сборке и вернётся при обновлении.");
            return;
        }

        if (!_dialogs.Confirm($"Удалить строку «{Describe(row)}»?"))
        {
            return;
        }

        var index = Rows.IndexOf(SelectedRow);
        SpecificationEditor.DeleteRow(Specification, row.Id);
        Changed(rebuild: true);
        SelectedRow = Rows.Count == 0 ? null : Rows[Math.Min(index, Rows.Count - 1)];
    }

    private void MoveBy(int direction)
    {
        var id = SelectedRow!.RowId!;
        if (SpecificationEditor.MoveRowBy(Specification, id, direction))
        {
            Changed(rebuild: true, selectRowId: id);
        }
    }

    private void ClearOverride(object? parameter)
    {
        if (SelectedRow?.Row is { } row && ParseColumn(parameter) is { } column)
        {
            SpecificationEditor.ClearOverride(Specification, _context, row, column);
            Changed(rebuild: true, selectRowId: row.Id);
        }
    }

    private void MoveToSection(object? parameter)
    {
        if (SelectedRow?.Row is { } row && parameter is string sectionId)
        {
            SpecificationEditor.SetCell(Specification, _context, row, SpecColumn.Section, sectionId);
            Changed(rebuild: true, selectRowId: row.Id);
        }
    }

    private void AddSection()
    {
        var viewModel = new NewSectionViewModel(_context.Catalog.Sections.ToList());
        if (!_dialogs.EditNewSection(viewModel))
        {
            return;
        }

        var section = viewModel.CreateSection();
        if (viewModel.SaveGlobally)
        {
            _settings.CustomSections.Add(section.Clone());
            _host.SaveSettings(_settings);
        }

        // Раздел добавляется и в спецификацию, чтобы он был виден, пока в нём нет строк.
        SpecificationEditor.AddCustomSection(Specification, section);
        _context = CreateContext();
        Changed(rebuild: true);
        SelectedRow = Rows.FirstOrDefault(r => r.IsHeader && r.SectionId == section.Id);
    }

    private void EditReserves()
    {
        var items = _context.Catalog.Sections
            .Where(s => s.HasPositions)
            .Select(s => new ReserveItem(s, _context.GetReserve(s.Id)));
        var viewModel = new ReserveSettingsViewModel(items);
        if (!_dialogs.EditReserves(viewModel))
        {
            return;
        }

        foreach (var item in viewModel.Items)
        {
            Specification.ReserveOverrides[item.SectionId] = item.ToSettings();
            if (viewModel.SaveAsDefault)
            {
                _settings.DefaultReserves[item.SectionId] = item.ToSettings();
            }
        }

        if (viewModel.SaveAsDefault)
        {
            _host.SaveSettings(_settings);
        }

        _context = CreateContext();
        if (viewModel.Renumber)
        {
            PositionAllocator.RenumberAll(Specification, _context);
        }

        Changed(rebuild: true);
    }

    private void Save()
    {
        _host.Save(Specification);
        IsDirty = false;
    }

    private void WritePositions()
    {
        if (Specification.Rows.Any(r => r.Kind == RowKind.Component && !r.IsRemoved && r.Position is null && PositionAllocator.IsNumbered(r, _context)) &&
            !_dialogs.Confirm("У некоторых строк нет номера позиции. Всё равно записать позиции?"))
        {
            return;
        }

        // Сначала сохраняем, чтобы спецификация в сборке совпадала с номерами в BOM.
        Save();
        _dialogs.Info(_host.WritePositions(Specification));
    }

    private void Changed(bool rebuild, string? selectRowId = null)
    {
        IsDirty = true;
        if (rebuild)
        {
            Rebuild(selectRowId);
        }
        else
        {
            UpdateStatus();
        }
    }

    private void Rebuild(string? selectRowId = null)
    {
        CommitEdits?.Invoke();
        var keep = selectRowId ?? SelectedRow?.RowId;
        var keepSection = SelectedRow?.IsHeader == true ? SelectedRow.SectionId : null;

        Rows.Clear();
        foreach (var display in SpecificationViewBuilder.Build(Specification, _context))
        {
            Rows.Add(new SpecRowViewModel(this, display));
        }

        SelectedRow = Rows.FirstOrDefault(r => keep is not null && r.RowId == keep)
                      ?? Rows.FirstOrDefault(r => keepSection is not null && r.IsHeader && r.SectionId == keepSection);

        SectionOptions.Clear();
        foreach (var section in _context.Catalog.Sections)
        {
            SectionOptions.Add(new Option(section.Id, section.Name));
        }

        UpdateStatus();
    }

    private void PostRebuild(string rowId) => _post(() => Rebuild(rowId));

    private static Action<Action> CreateDefaultPost()
    {
        var context = SynchronizationContext.Current;
        return context is null ? action => action() : action => context.Post(_ => action(), null);
    }

    private void RequestEdit(string rowId, SpecColumn column)
    {
        var row = Rows.FirstOrDefault(r => r.RowId == rowId);
        if (row is not null)
        {
            EditRequested?.Invoke(row, column);
        }
    }

    private void UpdateStatus()
    {
        var rows = Specification.Rows;
        var parts = new List<string> { $"Строк: {rows.Count(r => r.Kind != RowKind.Note)}" };
        AddCount(parts, "новых", rows.Count(r => r.Status.HasFlag(RowStatus.New)));
        AddCount(parts, "изменённых", rows.Count(r => r.Status.HasFlag(RowStatus.Changed)));
        AddCount(parts, "удалённых из сборки", rows.Count(r => r.IsRemoved));
        AddCount(parts, "без позиции", rows.Count(r => !r.IsRemoved && r.Position is null && PositionAllocator.IsNumbered(r, _context)));
        if (Specification.LastUpdatedUtc is { } updated)
        {
            parts.Add($"обновлено из сборки: {updated.ToLocalTime():dd.MM.yyyy HH:mm}");
        }

        StatusText = string.Join(" · ", parts);
    }

    private static void AddCount(List<string> parts, string label, int count)
    {
        if (count > 0)
        {
            parts.Add($"{label}: {count}");
        }
    }

    private SpecificationContext CreateContext() => SpecificationContext.Create(_settings, Specification);

    private static SpecColumn? ParseColumn(object? parameter) =>
        parameter is string text && Enum.TryParse<SpecColumn>(text, out var column) ? column : null;

    private static string Describe(SpecRow row)
    {
        var designation = row.Get(SpecColumn.Designation);
        var name = row.Get(SpecColumn.Name);
        var text = string.Join(" ", new[] { designation, name }.Where(s => s.Length > 0));
        return text.Length > 0 ? text : "(пустая строка)";
    }
}
