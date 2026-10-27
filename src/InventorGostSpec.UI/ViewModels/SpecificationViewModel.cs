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
    private string _searchText = "";
    private List<SpecRowViewModel> _selectedRows = new();

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
        DeleteRowCommand = new RelayCommand(DeleteRow, () => TargetRows.Any());
        MoveUpCommand = new RelayCommand(() => MoveBy(-1), () => IsSingleSelection && SelectedRow?.CanMove == true);
        MoveDownCommand = new RelayCommand(() => MoveBy(+1), () => IsSingleSelection && SelectedRow?.CanMove == true);
        ClearOverrideCommand = new RelayCommand(ClearOverride, p => ParseColumn(p) is { } c && TargetRows.Any(r => r.Kind == RowKind.Component && r.IsOverridden(c)));
        MoveToSectionCommand = new RelayCommand(MoveToSection, p => p is string && TargetRows.Any(r => r.Kind != RowKind.Note));
        AddSectionCommand = new RelayCommand(AddSection);
        AddGroupCommand = new RelayCommand(AddGroup);
        DeleteGroupCommand = new RelayCommand(DeleteGroup, () => SelectedRow?.IsGroupHeader == true);
        MoveToGroupCommand = new RelayCommand(MoveToGroup, p => p is string && TargetRows.Any());
        ReservesCommand = new RelayCommand(EditReserves);
        SaveCommand = new RelayCommand(Save, () => IsDirty);
        WritePositionsCommand = new RelayCommand(WritePositions);
        EditComponentCommand = new RelayCommand(EditComponent, () => TargetRows.Any(CanEditComponent));
        NextChangeCommand = new RelayCommand(GoToNextChange, () => Specification.Rows.Any(r => r.Status != RowStatus.None));
        ClearSearchCommand = new RelayCommand(() => SearchText = "", () => IsSearching);

        Rebuild();
        _isDirty = isNew;
    }

    /// <summary>
    /// Завершает редактирование ячейки в таблице. Вызывается перед перестроением строк:
    /// таблицу нельзя перестраивать, пока ячейка редактируется.
    /// </summary>
    public Action? CommitEdits { get; set; }

    /// <summary>Таблица сейчас будет перестроена (окно запоминает положение прокрутки).</summary>
    public event Action? Rebuilding;

    /// <summary>
    /// Таблица перестроена. Аргумент — строка, к которой нужно прокрутить (операция касалась её);
    /// <c>null</c> — сохранить прежнее положение прокрутки.
    /// </summary>
    public event Action<SpecRowViewModel?>? Rebuilt;

    /// <summary>После перестроения таблицы — восстановить множественный выбор строк.</summary>
    public event Action<IReadOnlyList<SpecRowViewModel>>? SelectionRestored;

    /// <summary>Выбранные строки (Ctrl/Shift). Пусто — действует только <see cref="SelectedRow"/>.</summary>
    public IReadOnlyList<SpecRowViewModel> SelectedRows => _selectedRows;

    /// <summary>Вызывается окном при изменении выбора в таблице.</summary>
    public void SetSelectedRows(IEnumerable<SpecRowViewModel> rows)
    {
        _selectedRows = rows.OrderBy(r => Rows.IndexOf(r)).ToList();
        UpdateStatus();
    }

    /// <summary>Запрос начать редактирование ячейки (например, у только что добавленной строки).</summary>
    public event Action<SpecRowViewModel, SpecColumn>? EditRequested;

    public Specification Specification { get; }

    public ObservableCollection<SpecRowViewModel> Rows { get; } = new();

    /// <summary>Разделы для команды «Перенести в раздел».</summary>
    public ObservableCollection<Option> SectionOptions { get; } = new();

    /// <summary>Основная часть и группы для команды «Перенести в группу».</summary>
    public ObservableCollection<Option> GroupOptions { get; } = new();

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
    public ICommand AddGroupCommand { get; }
    public ICommand DeleteGroupCommand { get; }
    public ICommand MoveToGroupCommand { get; }
    public ICommand ReservesCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand WritePositionsCommand { get; }
    public ICommand NextChangeCommand { get; }

    /// <summary>Свойства компонента (раздел, формат, поля сортировки) — записываются в его iProperties.</summary>
    public ICommand EditComponentCommand { get; }
    public ICommand ClearSearchCommand { get; }

    /// <summary>Поиск по обозначению, наименованию, примечанию и позиции. Показываются только найденные строки.</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value ?? ""))
            {
                OnPropertyChanged(nameof(IsSearching));
                Rebuild();
            }
        }
    }

    public bool IsSearching => SearchText.Trim().Length > 0;

    /// <summary>В спецификации нет строк (подсказка «нажмите Обновить из сборки»).</summary>
    public bool IsEmpty => Specification.Rows.Count == 0;

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

        if (result.Positions.ExhaustedBlocks.Count > 0)
        {
            var names = result.Positions.ExhaustedBlocks.Select(b =>
                _context.FindGroup(b.GroupId) is { } group ? $"{b.Section.Name} ({group.Title})" : b.Section.Name);
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
        var row = SpecificationEditor.AddNote(Specification, _context, SectionFor(selected), "", selected.RowId, selected.GroupId);
        Changed(rebuild: true, selectRowId: row.Id);
        RequestEdit(row.Id, SpecColumn.Name);
    }

    private void AddItem()
    {
        var selected = SelectedRow!;
        var values = new ComponentSnapshot { SectionId = SectionFor(selected), Quantity = "1" };
        var row = SpecificationEditor.AddManualItem(Specification, _context, values, selected.RowId, selected.GroupId);
        Changed(rebuild: true, selectRowId: row.Id);
        RequestEdit(row.Id, SpecColumn.Name);
    }

    private void DeleteRow()
    {
        var targets = TargetRows.ToList();
        var deletable = targets.Where(r => r.Kind != RowKind.Component || r.IsRemoved).ToList();
        if (deletable.Count == 0)
        {
            _dialogs.Warning("Строку компонента удалить нельзя: компонент есть в сборке и вернётся при обновлении.");
            return;
        }

        var question = deletable.Count == 1 ? $"Удалить строку «{Describe(deletable[0])}»?" : $"Удалить строки: {deletable.Count}?";
        if (deletable.Count < targets.Count)
        {
            question += $"\n\nСтроки компонентов, которые есть в сборке, не удаляются (пропущено: {targets.Count - deletable.Count}).";
        }

        if (!_dialogs.Confirm(question))
        {
            return;
        }

        var index = SelectedRow is null ? 0 : Rows.IndexOf(SelectedRow);
        foreach (var row in deletable)
        {
            SpecificationEditor.DeleteRow(Specification, row.Id);
        }

        _selectedRows.Clear();
        Changed(rebuild: true);
        SelectedRow = Rows.Count == 0 ? null : Rows[Math.Clamp(index, 0, Rows.Count - 1)];
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
        if (ParseColumn(parameter) is not { } column)
        {
            return;
        }

        foreach (var row in TargetRows.Where(r => r.Kind == RowKind.Component).ToList())
        {
            SpecificationEditor.ClearOverride(Specification, _context, row, column);
        }

        Changed(rebuild: true);
    }

    private void MoveToSection(object? parameter)
    {
        if (parameter is not string sectionId)
        {
            return;
        }

        // В порядке таблицы: строки встают в конец нового раздела в том же порядке.
        foreach (var row in TargetRows.Where(r => r.Kind != RowKind.Note).ToList())
        {
            SpecificationEditor.SetCell(Specification, _context, row, SpecColumn.Section, sectionId);
        }

        Changed(rebuild: true);
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
        var sections = _context.Catalog.Sections.Where(s => s.HasPositions).ToList();
        var parts = new[] { new ReservePart("", "Основная часть", sections.Select(s => new ReserveItem(s, _context.GetReserve(s.Id)))) }
            .Concat(_context.Groups.Select(g => new ReservePart(
                g.Id,
                g.Title,
                sections.Select(s => new ReserveItem(s, _context.GetReserve(g.Id, s.Id))))));
        var viewModel = new ReserveSettingsViewModel(parts);
        if (SelectedRow is { GroupId.Length: > 0 } selected)
        {
            // Открываем резерв той части, в которой стоит выбранная строка.
            viewModel.SelectedPart = viewModel.Parts.FirstOrDefault(p => p.GroupId == selected.GroupId) ?? viewModel.Parts[0];
        }

        if (!_dialogs.EditReserves(viewModel))
        {
            return;
        }

        foreach (var part in viewModel.Parts)
        {
            var group = _context.FindGroup(part.GroupId);
            foreach (var item in part.Items)
            {
                if (group is not null)
                {
                    group.Reserves[item.SectionId] = item.ToSettings();
                    continue;
                }

                Specification.ReserveOverrides[item.SectionId] = item.ToSettings();
                if (viewModel.SaveAsDefault)
                {
                    _settings.DefaultReserves[item.SectionId] = item.ToSettings();
                }
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

    internal void RenameGroup(string groupId, string title)
    {
        title = title.Trim();
        if (_context.FindGroup(groupId) is { } group && title.Length > 0 && title != group.Title)
        {
            group.Title = title;
            Changed(rebuild: false);
            PostRebuild(groupId);
        }
    }

    private void AddGroup()
    {
        var viewModel = new GroupViewModel();
        if (!_dialogs.EditGroup(viewModel))
        {
            return;
        }

        var group = SpecificationEditor.AddGroup(Specification, viewModel.Title);
        Changed(rebuild: true);
        SelectedRow = Rows.FirstOrDefault(r => r.IsGroupHeader && r.GroupId == group.Id);
    }

    private void DeleteGroup()
    {
        var header = SelectedRow!;
        if (_context.FindGroup(header.GroupId) is not { } group ||
            !_dialogs.Confirm($"Удалить группу «{group.Title}»?\n\nЕё строки перейдут в основную часть и получат новые номера позиций."))
        {
            return;
        }

        SpecificationEditor.RemoveGroup(Specification, _context, group.Id);
        Changed(rebuild: true);
    }

    private void MoveToGroup(object? parameter)
    {
        if (parameter is not string groupId)
        {
            return;
        }

        foreach (var row in TargetRows.Where(r => r.GroupId != groupId).ToList())
        {
            SpecificationEditor.MoveToGroup(Specification, _context, row, groupId);
        }

        Changed(rebuild: true);
    }

    /// <summary>Строки, к которым применяется команда: выбранные (Ctrl/Shift) или текущая.</summary>
    private IEnumerable<SpecRow> TargetRows =>
        (_selectedRows.Count > 0 ? _selectedRows : SelectedRow is null ? Enumerable.Empty<SpecRowViewModel>() : new[] { SelectedRow })
        .Select(r => r.Row)
        .OfType<SpecRow>();

    private bool IsSingleSelection => _selectedRows.Count <= 1;

    /// <summary>Раздел для новой строки: раздел выбранной строки, у заголовка группы — «Прочие изделия».</summary>
    private static string SectionFor(SpecRowViewModel selected) =>
        selected.IsGroupHeader ? Core.Sections.SectionIds.OtherProducts : selected.SectionId;

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
        var keepHeader = SelectedRow?.IsHeader == true ? SelectedRow : null;
        var keepMany = selectRowId is null
            ? _selectedRows.Select(r => r.RowId).OfType<string>().ToHashSet()
            : new HashSet<string>();

        Rebuilding?.Invoke();
        Rows.Clear();
        foreach (var display in Filter(SpecificationViewBuilder.Build(Specification, _context)))
        {
            Rows.Add(new SpecRowViewModel(this, display));
        }

        OnPropertyChanged(nameof(IsEmpty));

        SelectedRow = Rows.FirstOrDefault(r => keep is not null && r.RowId == keep)
                      ?? Rows.FirstOrDefault(r => keepHeader is not null && r.Kind == keepHeader.Kind &&
                                                  r.GroupId == keepHeader.GroupId && r.SectionId == keepHeader.SectionId);

        // Множественный выбор переживает перестроение (например, после переноса нескольких строк).
        _selectedRows = keepMany.Count > 1 ? Rows.Where(r => r.RowId is not null && keepMany.Contains(r.RowId)).ToList() : new();
        if (_selectedRows.Count > 1)
        {
            if (SelectedRow is null || !_selectedRows.Contains(SelectedRow))
            {
                SelectedRow = _selectedRows[0];
            }

            SelectionRestored?.Invoke(_selectedRows);
        }

        SectionOptions.Clear();
        foreach (var section in _context.Catalog.Sections)
        {
            SectionOptions.Add(new Option(section.Id, section.Name));
        }

        GroupOptions.Clear();
        GroupOptions.Add(new Option("", "Основная часть"));
        foreach (var group in _context.Groups)
        {
            GroupOptions.Add(new Option(group.Id, group.Title));
        }

        UpdateStatus();
        Rebuilt?.Invoke(selectRowId is null ? null : SelectedRow);
    }

    /// <summary>
    /// При поиске оставляет найденные строки и заголовки их разделов и групп; резервные строки скрываются.
    /// </summary>
    private IEnumerable<DisplayRow> Filter(IReadOnlyList<DisplayRow> rows)
    {
        var text = SearchText.Trim();
        if (text.Length == 0)
        {
            return rows;
        }

        var matches = rows.Where(r => r.Row is not null && Matches(r.Row, text)).ToList();
        return rows.Where(r => r.Kind switch
        {
            DisplayRowKind.Row => matches.Contains(r),
            DisplayRowKind.SectionHeader => matches.Any(m => m.GroupId == r.GroupId && m.SectionId == r.SectionId),
            DisplayRowKind.GroupHeader => matches.Any(m => m.GroupId == r.GroupId),
            _ => false,
        });
    }

    private static bool Matches(SpecRow row, string text) =>
        new[] { SpecColumn.Designation, SpecColumn.Name, SpecColumn.Note, SpecColumn.Position, SpecColumn.Format, SpecColumn.Zone }
            .Any(c => row.Get(c).Replace('ё', 'е').Replace('Ё', 'Е')
                .Contains(text.Replace('ё', 'е').Replace('Ё', 'Е'), StringComparison.CurrentCultureIgnoreCase));

    /// <summary>У строки есть компонент в сборке, которому можно задать свойства (не документ и не удалённый).</summary>
    private static bool CanEditComponent(SpecRow? row) =>
        row is { Kind: RowKind.Component, IsRemoved: false, ComponentKey: { } key } && !key.StartsWith("drawing:", StringComparison.Ordinal);

    private void EditComponent()
    {
        var rows = TargetRows.Where(CanEditComponent).ToList();
        if (rows.Count == 0)
        {
            return;
        }

        var current = new List<IReadOnlyDictionary<string, string?>>();
        foreach (var row in rows)
        {
            if (_host.ReadComponentProperties(row.ComponentKey!) is not { } values)
            {
                _dialogs.Warning($"Компонент «{Describe(row)}» не найден в сборке. Обновите спецификацию из сборки.");
                return;
            }

            current.Add(values);
        }

        // Общие значения; если у выбранных компонентов они различаются — null («разные значения»).
        var initial = current.SelectMany(v => v.Keys).Distinct().ToDictionary(
            name => name,
            name =>
            {
                var distinct = current.Select(v => v.TryGetValue(name, out var value) ? value ?? "" : "").Distinct().ToList();
                return distinct.Count == 1 ? distinct[0] : null;
            });

        // Только общий справочник разделов: раздел записывается в файл детали и должен быть понятен в других сборках.
        var sections = SpecificationContext.Create(_settings, new Specification()).Catalog.Sections;
        var title = rows.Count == 1
            ? Describe(rows[0])
            : $"Выбрано компонентов: {rows.Count}. Меняются только изменённые поля.";
        var autoSections = rows.Select(r => _host.GetAutoSectionId(r.ComponentKey!)).Distinct().ToList();
        var viewModel = new AssignSectionViewModel(
            sections,
            initial,
            $"{title}\nСвойства записываются в файлы компонентов и действуют во всех сборках.",
            autoSections.FirstOrDefault(),
            autoSectionVaries: autoSections.Count > 1);
        if (!_dialogs.EditComponentProperties(viewModel))
        {
            return;
        }

        var changes = viewModel.GetChanges();
        if (changes.Count == 0)
        {
            return;
        }

        foreach (var row in rows)
        {
            var data = _host.WriteComponentProperties(row.ComponentKey!, changes);
            SpecificationEditor.RefreshComponent(Specification, _context, row, data);
        }

        Changed(rebuild: true, selectRowId: rows.Count == 1 ? rows[0].Id : null);
    }

    /// <summary>Выделяет следующую подсвеченную строку (новую, изменённую или удалённую), по кругу.</summary>
    private void GoToNextChange()
    {
        var changed = Rows.Where(r => r.IsNew || r.IsChanged || r.IsRemoved).ToList();
        if (changed.Count == 0)
        {
            _dialogs.Info(IsSearching
                ? "Среди найденных строк изменений нет."
                : "Изменений нет.");
            return;
        }

        var current = SelectedRow is null ? -1 : Rows.IndexOf(SelectedRow);
        SelectedRow = changed.FirstOrDefault(r => Rows.IndexOf(r) > current) ?? changed[0];
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

        if (IsSearching)
        {
            parts.Insert(0, $"Найдено: {Rows.Count(r => r.Row is not null)}");
        }

        if (_selectedRows.Count > 1)
        {
            parts.Insert(0, $"Выбрано: {_selectedRows.Count(r => r.Row is not null)}");
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
