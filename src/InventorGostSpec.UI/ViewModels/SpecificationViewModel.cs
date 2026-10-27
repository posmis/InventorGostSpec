using System.Collections.ObjectModel;
using System.Windows.Input;
using InventorGostSpec.Core.Checking;
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
    /// <summary>Сколько шагов можно отменить.</summary>
    public const int UndoLimit = 100;

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

    // Отмена: снимки спецификации (JSON) до и после каждого изменения.
    private readonly List<string> _undo = new();
    private readonly List<string> _redo = new();
    private string _current;
    private string? _saved;

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
        _current = SpecificationSerializer.Serialize(Specification);
        _saved = isNew ? null : _current;

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
        ExportExcelCommand = new RelayCommand(ExportExcel, () => !IsEmpty);
        ExportPdfCommand = new RelayCommand(ExportPdf, () => !IsEmpty);
        PdfSettingsCommand = new RelayCommand(OpenPdfSettings);
        InsertComponentNoteCommand = new RelayCommand(InsertComponentNote, () => TargetRows.Any(CanEditComponent));
        SetSortGroupCommand = new RelayCommand(SetSortGroup, p => p is string && TargetRows.Any(r => r.Kind != RowKind.Note));
        EditComponentCommand = new RelayCommand(EditComponent, () => TargetRows.Any(CanEditComponent));
        NextChangeCommand = new RelayCommand(GoToNextChange, () => Specification.Rows.Any(r => r.Status != RowStatus.None));
        ClearSearchCommand = new RelayCommand(() => SearchText = "", () => IsSearching);
        UndoCommand = new RelayCommand(Undo, () => _undo.Count > 0);
        RedoCommand = new RelayCommand(Redo, () => _redo.Count > 0);
        CheckCommand = new RelayCommand(Check, () => !IsEmpty);
        InsertSectionCommand = new RelayCommand(InsertSection, p => p is string id && InsertableSections().Any(o => o.Id == id));
        HideSectionCommand = new RelayCommand(HideSection, () => SelectedEmptyInsertedSection() is not null);
        DrawingFormatsCommand = new RelayCommand(FindDrawingFormats, () => Specification.Rows.Any(IsPartWithModel));
        SetDocumentTypeCommand = new RelayCommand(SetDocumentType, p => p is string && TargetRows.Any(IsDocumentRow));
        ResetSectionsCommand = new RelayCommand(ResetSections, p => ResetSectionRows(p).Any());
        ShowComponentFileCommand = new RelayCommand(ShowComponentFile, () => SelectedComponent() is not null && !IsDocumentSelected);
        ShowDocumentFileCommand = new RelayCommand(ShowDocumentFile, () => IsDocumentSelected && (SelectedDocumentFile() is not null || SelectedComponent() is not null));
        AddExternalDocumentCommand = new RelayCommand(AddExternalDocument, () => CanAddExternalDocument);
        ShowDrawingFileCommand = new RelayCommand(ShowDrawingFile, () => SelectedComponent() is { } row && SpecificationEditor.IsModelRow(row) && !IsDocumentSelected);

        CheckDocumentFiles();
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
        OnPropertyChanged(nameof(CanSetDocumentType));
        OnPropertyChanged(nameof(CanAddExternalDocument));
        OnPropertyChanged(nameof(IsDocumentSelected));
        OnPropertyChanged(nameof(IsNotDocumentSelected));
    }

    /// <summary>Пункт «Тип документа» — только для строк раздела «Документация».</summary>
    public bool CanSetDocumentType => TargetRows.Any(IsDocumentRow);

    /// <summary>Запрос начать редактирование ячейки (например, у только что добавленной строки).</summary>
    public event Action<SpecRowViewModel, SpecColumn>? EditRequested;

    public Specification Specification { get; private set; }

    public ObservableCollection<SpecRowViewModel> Rows { get; } = new();

    /// <summary>Разделы для команды «Перенести в раздел».</summary>
    public ObservableCollection<Option> SectionOptions { get; } = new();

    /// <summary>Основная часть и группы для команды «Перенести в группу».</summary>
    public ObservableCollection<Option> GroupOptions { get; } = new();

    public string Title => $"Спецификация — {_host.DocumentName}{(IsDirty ? " *" : "")}";

    public SpecRowViewModel? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (SetField(ref _selectedRow, value))
            {
                OnPropertyChanged(nameof(CanSetDocumentType));
                OnPropertyChanged(nameof(CanAddExternalDocument));
                OnPropertyChanged(nameof(IsDocumentSelected));
                OnPropertyChanged(nameof(IsNotDocumentSelected));
            }
        }
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

    /// <summary>Экспорт в Excel для ручной правки и последующего вывода в PDF по ГОСТ.</summary>
    public ICommand ExportExcelCommand { get; }

    /// <summary>PDF по ГОСТ: выгрузка во временный Excel и вызов утилиты PDF.</summary>
    public ICommand ExportPdfCommand { get; }

    /// <summary>Настройки утилиты PDF: шрифт, размеры шрифтов, сужение текста.</summary>
    public ICommand PdfSettingsCommand { get; }

    /// <summary>Вставить в графу «Примечание» выбранных строк примечание из свойств их деталей.</summary>
    public ICommand InsertComponentNoteCommand { get; }

    /// <summary>Группа сортировки выбранных строк: параметр — число (по умолчанию у всех строк 0).</summary>
    public ICommand SetSortGroupCommand { get; }

    /// <summary>Варианты для подменю «Группа сортировки».</summary>
    public IReadOnlyList<Option> SortGroupOptions { get; } =
        Enumerable.Range(0, 10).Select(i => new Option(i.ToString(), i.ToString())).ToList();

    /// <summary>Свойства компонента (раздел, формат, поля сортировки) — записываются в его iProperties.</summary>
    public ICommand EditComponentCommand { get; }
    public ICommand ClearSearchCommand { get; }

    /// <summary>
    /// Отменить последнее изменение (Ctrl+Z). Отменяется изменение спецификации; свойства, уже записанные
    /// в файлы деталей командой «Свойства», и общие настройки не откатываются.
    /// </summary>
    public ICommand UndoCommand { get; }

    /// <summary>Вернуть отменённое изменение (Ctrl+Y).</summary>
    public ICommand RedoCommand { get; }

    /// <summary>Проверка перед выпуском: позиции, обязательные графы, повторы, расхождения с BOM.</summary>
    public ICommand CheckCommand { get; }

    /// <summary>
    /// Вставить раздел (параметр — id) в часть выбранной строки (основную или группу): показывается заголовком
    /// и резервными строками, пока в нём нет строк.
    /// </summary>
    public ICommand InsertSectionCommand { get; }

    /// <summary>Убрать пустой вставленный раздел (выбран его заголовок).</summary>
    public ICommand HideSectionCommand { get; }

    /// <summary>
    /// Форматы чертежей деталей (раздел «Детали»): выбранных строк или всех деталей. Форматы всех листов чертежа
    /// записываются в iProperty SPEC_Format детали.
    /// </summary>
    public ICommand DrawingFormatsCommand { get; }

    /// <summary>
    /// Разделы по умолчанию: у компонентов удаляется SPEC_Section и ручной перенос — раздел определяется по типу.
    /// Стандартные изделия не трогаются. Параметр «selected» (контекстное меню) — только выбранные строки,
    /// без параметра (панель) — выбранные, если их несколько, иначе все компоненты.
    /// </summary>
    public ICommand ResetSectionsCommand { get; }

    /// <summary>Показать файл компонента выбранной строки в Проводнике.</summary>
    public ICommand ShowComponentFileCommand { get; }

    /// <summary>Папка документа строки «Документации»: найденного чертежа или стороннего файла.</summary>
    public ICommand ShowDocumentFileCommand { get; }

    /// <summary>
    /// Выбрана строка «Документации»: в меню один пункт «Показать документ в папке»
    /// вместо пунктов файла компонента и чертежа.
    /// </summary>
    public bool IsDocumentSelected => SelectedRow?.Row is { } row && IsDocumentRow(row);

    public bool IsNotDocumentSelected => !IsDocumentSelected;

    /// <summary>Сторонний документ (PDF и т. п.) в «Документацию» — из меню строк этого раздела.</summary>
    public ICommand AddExternalDocumentCommand { get; }

    /// <summary>Выбрана строка или заголовок раздела «Документация».</summary>
    public bool CanAddExternalDocument =>
        SelectedRow is { IsGroupHeader: false } selected && ComponentSnapshot.IsDocument(selected.SectionId);

    /// <summary>Найти чертёж компонента выбранной строки и показать его в Проводнике.</summary>
    public ICommand ShowDrawingFileCommand { get; }

    /// <summary>Тип документа (параметр — код «СБ», «ТУ»…) выбранных строк «Документации»: код в обозначении и наименование.</summary>
    public ICommand SetDocumentTypeCommand { get; }

    /// <summary>Пункты меню «Тип документа»: «СБ — Сборочный чертеж».</summary>
    public IReadOnlyList<Option> DocumentTypeOptions { get; } = Core.Sorting.DocumentCodes.Common
        .Select(code => new Option(code, $"{code} — {Core.Sorting.DocumentCodes.NameOf(code)}"))
        .ToList();

    /// <summary>Резерв, который получает вставленный раздел, если у него резерва нет.</summary>
    public const int InsertedSectionReserve = 3;

    /// <summary>Разделы, которых нет в части выбранной строки (основной или группе), — для меню «Раздел».</summary>
    public IReadOnlyList<Option> InsertableSections()
    {
        var groupId = SelectedRow?.GroupId ?? "";
        return _context.Catalog.Sections
            .Where(s => !SpecBlocks.IsVisible(Specification, new SpecBlock(groupId, s)))
            .Select(s => new Option(s.Id, s.Name))
            .ToList();
    }

    /// <summary>Подсказки кнопок отмены: что будет отменено.</summary>
    public string UndoToolTip => _undo.Count > 0 ? $"Отменить (Ctrl+Z). Можно отменить шагов: {_undo.Count}" : "Отменить (Ctrl+Z): нечего отменять";

    public string RedoToolTip => _redo.Count > 0 ? $"Вернуть (Ctrl+Y). Можно вернуть шагов: {_redo.Count}" : "Вернуть (Ctrl+Y): нечего возвращать";

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

    /// <summary>Получает ли строка номер позиции (не текст и раздел с позициями).</summary>
    internal bool HasPosition(SpecRow row) => PositionAllocator.IsNumbered(row, _context);

    internal void SetCell(SpecRowViewModel rowViewModel, SpecColumn column, string value)
    {
        var row = rowViewModel.Row!;
        value = value.Trim();

        if (column == SpecColumn.SortGroup && value.Length > 0 && !(int.TryParse(value, out var group) && group >= 0))
        {
            _dialogs.Warning("Группа сортировки — целое число от 0 (по умолчанию 0): чем меньше, тем выше в разделе.");
            rowViewModel.Refresh();
            return;
        }

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
        CheckDocumentFiles();
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
        if (removed > 0 && !_dialogs.Confirm($"Снять все уведомления и удалить строки компонентов, которых нет в сборке ({removed})?"))
        {
            return;
        }

        // «Файл изменён»: запоминается текущее содержимое файлов; сами ссылки не меняются.
        TryHostAction(() => ExternalDocuments.AcceptFiles(Specification, _host.AssemblyFolder));
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
        // У документов количество не пишется.
        var section = SectionFor(selected);
        var values = new ComponentSnapshot { SectionId = section, Quantity = ComponentSnapshot.IsDocument(section) ? "" : "1" };
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

    /// <summary>Строка компонента-модели, кроме стандартных изделий (их раздел не сбрасывается).</summary>
    private static bool CanResetSection(SpecRow row) =>
        CanEditComponent(row) && SpecificationEditor.IsModelRow(row) &&
        !IsStandard(row.SectionId) && !IsStandard(row.Source.SectionId);

    private static bool IsStandard(string? sectionId) =>
        string.Equals(sectionId, Core.Sections.SectionIds.StandardProducts, StringComparison.OrdinalIgnoreCase);

    private IEnumerable<SpecRow> ResetSectionRows(object? parameter) =>
        (parameter is "selected" || _selectedRows.Count > 1 ? TargetRows : Specification.Rows).Where(CanResetSection);

    private void ResetSections(object? parameter)
    {
        CommitEdits?.Invoke();
        var rows = ResetSectionRows(parameter).ToList();
        if (rows.Count == 0)
        {
            _dialogs.Info("Среди выбранных строк нет компонентов, у которых можно сбросить раздел (стандартные изделия не трогаются).");
            return;
        }

        var scope = parameter is "selected" || _selectedRows.Count > 1 ? "выбранных компонентов" : "всех компонентов спецификации";
        if (!_dialogs.Confirm(
                $"Сбросить разделы {scope} ({rows.Count}) к разделам по умолчанию?\n\n" +
                "Свойство SPEC_Section удаляется из файлов компонентов, ручной перенос в спецификации снимается — " +
                "раздел определяется по типу: сборка — «Сборочные единицы», деталь — «Детали», виртуальный компонент — " +
                "«Прочие изделия». Стандартные изделия не трогаются. Изменятся файлы компонентов — сохраните сборку."))
        {
            return;
        }

        var written = 0;
        var failed = new List<string>();
        foreach (var row in rows)
        {
            try
            {
                // Изделия Content Center по типу — стандартные: их не трогаем, даже если перенесены в другой раздел.
                if (IsStandard(_host.GetAutoSectionId(row.ComponentKey!)))
                {
                    continue;
                }

                SpecificationEditor.ClearOverride(Specification, _context, row, SpecColumn.Section);
                var properties = _host.ReadComponentProperties(row.ComponentKey!);
                if (properties is null)
                {
                    continue;
                }

                var section = properties.TryGetValue(PropertyNames.Section, out var value) ? value ?? "" : "";
                if (section.Trim().Length == 0 || IsStandard(section.Trim()))
                {
                    continue;
                }

                var data = _host.WriteComponentProperties(row.ComponentKey!, new Dictionary<string, string> { [PropertyNames.Section] = "" });
                SpecificationEditor.RefreshComponent(Specification, _context, row, data);
                written++;
            }
            catch (Exception ex)
            {
                _host.ReportError(ex);
                failed.Add($"{Describe(row)}: {ex.Message}");
            }
        }

        Changed(rebuild: true);
        var report = $"Разделы сброшены. Изменено файлов: {written} из {rows.Count} (у остальных раздел и так не был задан).";
        if (failed.Count > 0)
        {
            report += $"\n\nНе удалось записать ({failed.Count}):\n" + string.Join("\n", failed.Take(10));
        }

        _dialogs.Info(report);
    }

    /// <summary>Выбранная строка компонента, который есть в сборке.</summary>
    private SpecRow? SelectedComponent() =>
        SelectedRow?.Row is { Kind: RowKind.Component, IsRemoved: false, ComponentKey: not null } row ? row : null;

    /// <summary>Полный путь стороннего документа у выбранной строки; <c>null</c> — строка не связана с файлом.</summary>
    private string? SelectedDocumentFile() =>
        SelectedRow?.Row is { Kind: RowKind.ManualItem, DocumentFile.Length: > 0 } row
            ? System.IO.Path.GetFullPath(System.IO.Path.Combine(_host.AssemblyFolder, row.DocumentFile))
            : null;

    private void AddExternalDocument()
    {
        var folder = _host.AssemblyFolder;
        var files = _dialogs.AskDocumentFiles(folder);
        if (files is null || files.Count == 0)
        {
            return;
        }

        var existing = Specification.Rows
            .Where(r => r.DocumentFile.Length > 0)
            .Select(r => System.IO.Path.GetFullPath(System.IO.Path.Combine(folder, r.DocumentFile)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var afterRowId = SelectedRow?.RowId;
        var skipped = new List<string>();
        SpecRow? last = null;
        foreach (var file in files)
        {
            if (!existing.Add(System.IO.Path.GetFullPath(file)))
            {
                skipped.Add(System.IO.Path.GetFileName(file));
                continue;
            }

            last = SpecificationEditor.AddExternalDocument(Specification, _context, StoredPath(folder, file), afterRowId);
            last.DocumentHash = ExternalDocuments.Hash(file);
            afterRowId = last.Id;
        }

        if (last is not null)
        {
            // Формат стороннего документа неизвестен — вписывается вручную.
            Changed(rebuild: true, selectRowId: last.Id);
            RequestEdit(last.Id, SpecColumn.Format);
        }

        if (skipped.Count > 0)
        {
            _dialogs.Info("Эти документы уже есть в спецификации:\n" + string.Join("\n", skipped));
        }
    }

    /// <summary>
    /// Путь документа для хранения: относительно папки сборки (на другом ПК с той же структурой папок, например в облаке,
    /// он найдётся), на другом диске — полный.
    /// </summary>
    private static string StoredPath(string folder, string file) =>
        string.Equals(System.IO.Path.GetPathRoot(folder), System.IO.Path.GetPathRoot(file), StringComparison.OrdinalIgnoreCase)
            ? System.IO.Path.GetRelativePath(folder, file)
            : file;

    private void ShowDocumentFile()
    {
        var document = SelectedDocumentFile()
            ?? (SelectedComponent() is { } row ? TryHost<string>(() => _host.GetComponentFile(row.ComponentKey!)) : null);
        if (document is null || !System.IO.File.Exists(document))
        {
            _dialogs.Warning(document is null ? "Строка документа не связана с файлом." : $"Файл документа не найден:\n{document}");
            return;
        }

        _dialogs.ShowInFolder(document);
    }

    /// <summary>Отмечает сторонние документы, файлы которых изменились (уведомление, ссылки не меняются).</summary>
    private void CheckDocumentFiles() =>
        TryHostAction(() => ExternalDocuments.CheckFiles(Specification, _host.AssemblyFolder));

    private void ShowComponentFile()
    {
        if (SelectedComponent() is not { } row)
        {
            return;
        }

        var file = TryHost<string>(() => _host.GetComponentFile(row.ComponentKey!));
        if (file is null || !System.IO.File.Exists(file))
        {
            _dialogs.Warning($"Файл компонента «{Describe(row)}» не найден{(file is null ? " в сборке" : $":\n{file}")}.");
            return;
        }

        _dialogs.ShowInFolder(file);
    }

    private void ShowDrawingFile()
    {
        if (SelectedComponent() is not { } row)
        {
            return;
        }

        var found = TryHost(() => _host.FindDrawingFormats(new[] { row.ComponentKey! }));
        if (found is null || !found.TryGetValue(row.ComponentKey!, out var drawing) || drawing.DrawingPath is null)
        {
            _dialogs.Warning(
                $"Чертёж компонента «{Describe(row)}» не найден в папках проекта.\n\n" +
                "Чертёж ищется по имени (как у модели или по обозначению) и по содержимому; подробности — в журнале аддина.");
            return;
        }

        _dialogs.ShowInFolder(drawing.DrawingPath);
    }

    private static bool IsDocumentRow(SpecRow row) => row.Kind != RowKind.Note && ComponentSnapshot.IsDocument(row.SectionId);

    private void SetDocumentType(object? parameter)
    {
        if (parameter is not string code)
        {
            return;
        }

        foreach (var row in TargetRows.Where(IsDocumentRow).ToList())
        {
            SpecificationEditor.SetDocumentType(Specification, _context, row, code);
        }

        Changed(rebuild: true);
    }

    /// <summary>Строка детали, у которой есть модель в сборке.</summary>
    private static bool IsPartWithModel(SpecRow row) =>
        CanEditComponent(row) && string.Equals(row.SectionId, Core.Sections.SectionIds.Parts, StringComparison.OrdinalIgnoreCase);

    private void FindDrawingFormats()
    {
        CommitEdits?.Invoke();
        var rows = (_selectedRows.Count > 1 ? TargetRows : Specification.Rows).Where(IsPartWithModel).ToList();
        if (rows.Count == 0)
        {
            _dialogs.Info("Среди выбранных строк нет деталей (раздел «Детали»).");
            return;
        }

        var scope = _selectedRows.Count > 1 ? "выбранных деталей" : "всех деталей спецификации";
        if (!_dialogs.Confirm(
                $"Найти чертежи {scope} ({rows.Count}) и записать форматы их листов в свойства деталей (SPEC_Format)?\n\n" +
                "Несколько форматов записываются через запятую в порядке уменьшения («А1, А2х3, А2»), " +
                "детали без чертежа — «БЧ» (если формат не был задан). " +
                "Изменятся файлы деталей — после этого сохраните сборку. Детали без чертежа не меняются."))
        {
            return;
        }

        IReadOnlyDictionary<string, DrawingFormat> found;
        try
        {
            found = _host.FindDrawingFormats(rows.Select(r => r.ComponentKey!).ToList());
        }
        catch (Exception ex)
        {
            _host.ReportError(ex);
            _dialogs.Warning($"Не удалось найти чертежи: {ex.Message}");
            return;
        }

        var written = new List<string>();
        var same = 0;
        var noDrawing = new List<string>();
        var keptWithoutDrawing = new List<string>();
        var unknown = new List<string>();
        var failed = new List<string>();
        foreach (var row in rows)
        {
            string format;
            if (!found.TryGetValue(row.ComponentKey!, out var result) || result.DrawingPath is null)
            {
                // Чертежа нет — «БЧ» (ГОСТ 2.106), но формат, вписанный вручную, не затираем.
                if (row.Source.Format.Length > 0 && row.Source.Format != SheetFormats.NoDrawing)
                {
                    keptWithoutDrawing.Add($"{Describe(row)}: {row.Source.Format}");
                    continue;
                }

                noDrawing.Add(Describe(row));
                format = SheetFormats.NoDrawing;
            }
            else if (result.Format.Length == 0)
            {
                unknown.Add($"{Describe(row)} ({System.IO.Path.GetFileName(result.DrawingPath)})");
                continue;
            }
            else
            {
                format = result.Format;
            }

            if (format == row.Source.Format)
            {
                same++;
                continue;
            }

            try
            {
                var data = _host.WriteComponentProperties(row.ComponentKey!, new Dictionary<string, string> { [PropertyNames.Format] = format });
                SpecificationEditor.RefreshComponent(Specification, _context, row, data);
                written.Add($"{Describe(row)}: {format}");
            }
            catch (Exception ex)
            {
                _host.ReportError(ex);
                failed.Add($"{Describe(row)}: {ex.Message}");
            }
        }

        Changed(rebuild: true);

        static string List(IReadOnlyList<string> items) =>
            string.Join("\n", items.Take(10).Select(i => "• " + i)) + (items.Count > 10 ? $"\n… и ещё {items.Count - 10}" : "");

        var report = $"Записано форматов: {written.Count}. Без изменений: {same}.";
        if (written.Count > 0)
        {
            report += "\n" + List(written);
        }

        if (noDrawing.Count > 0)
        {
            report += $"\n\nЧертёж не найден — формат «БЧ» ({noDrawing.Count}):\n" + List(noDrawing);
        }

        if (keptWithoutDrawing.Count > 0)
        {
            report += $"\n\nЧертёж не найден, оставлен формат, заданный раньше ({keptWithoutDrawing.Count}):\n" + List(keptWithoutDrawing);
        }

        if (unknown.Count > 0)
        {
            report += $"\n\nФормат листов не распознан ({unknown.Count}):\n" + List(unknown);
        }

        if (failed.Count > 0)
        {
            report += $"\n\nНе удалось записать ({failed.Count}):\n" + List(failed);
        }

        var overridden = rows.Count(r => r.IsOverridden(SpecColumn.Format));
        if (overridden > 0)
        {
            report += $"\n\nУ {overridden} строк формат изменён вручную в спецификации — показывается ручное значение " +
                      "(«Вернуть значение из компонента» в контекстном меню).";
        }

        _dialogs.Info(report);
    }

    private void InsertSection(object? parameter)
    {
        if (parameter is not string sectionId)
        {
            return;
        }

        var groupId = SelectedRow?.GroupId ?? "";
        SpecificationEditor.ShowSection(Specification, _context, groupId, sectionId);

        // Вставленный раздел — это его резервные строки: если резерва нет, задаём его в этой спецификации.
        var section = _context.Catalog.GetOrCreate(sectionId);
        var hint = "";
        if (_context.GetReserve(groupId, sectionId).Count <= 0)
        {
            var reserve = new ReserveSettings { Count = InsertedSectionReserve, RoundStartTo = _context.GetReserve(groupId, sectionId).RoundStartTo };
            if (_context.FindGroup(groupId) is { } group)
            {
                group.Reserves[section.Id] = reserve;
            }
            else
            {
                Specification.ReserveOverrides[section.Id] = reserve;
            }

            _context = CreateContext();
            hint = $" Резерв раздела: {InsertedSectionReserve} (изменить — «Резерв»).";
        }

        ShowReserveRows = true;
        Changed(rebuild: true);
        SelectedRow = Rows.FirstOrDefault(r => r.Kind == SpecRowKind.Header && r.GroupId == groupId && r.SectionId == section.Id);

        if (section.HasPositions && PositionAllocator.GetReservedNumbers(Specification, _context, groupId, section.Id).Count == 0)
        {
            hint += " Свободных номеров под резерв нет — они появятся после «Перенумеровать».";
        }

        StatusText = $"Вставлен раздел «{section.Name}».{hint}";
    }

    private void HideSection()
    {
        if (SelectedEmptyInsertedSection() is { } header)
        {
            SpecificationEditor.HideSection(Specification, header.GroupId, header.SectionId);
            Changed(rebuild: true);
        }
    }

    /// <summary>Выбран заголовок вставленного раздела, в котором нет строк.</summary>
    private SpecRowViewModel? SelectedEmptyInsertedSection() =>
        SelectedRow is { Kind: SpecRowKind.Header } header &&
        Specification.ShownSections.Any(s => s.Matches(header.GroupId, header.SectionId)) &&
        !Specification.Rows.Any(r => r.GroupId == header.GroupId && string.Equals(r.SectionId, header.SectionId, StringComparison.OrdinalIgnoreCase))
            ? header
            : null;

    private void EditReserves()
    {
        var sections = _context.Catalog.Sections.ToList();
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

    /// <summary>
    /// Экспорт текущего состояния окна (в том числе несохранённого) в Excel.
    /// Резервные строки выгружаются, если включено «Показывать резерв».
    /// Лист «Изменения» переносится из заменяемого файла.
    /// </summary>
    private void ExportExcel()
    {
        CommitEdits?.Invoke();
        var info = _host.GetExportInfo();
        var path = _dialogs.AskExportPath(info.DefaultPath);
        if (path is null)
        {
            return;
        }

        try
        {
            KeepManualTitleData(info.TitleBlock, path);

            Excel.ExcelExporter.Export(Specification, _context, info.TitleBlock, path);
        }
        catch (System.IO.IOException ex)
        {
            _dialogs.Warning($"Не удалось записать файл. Возможно, он открыт в Excel.\n\n{ex.Message}");
            return;
        }

        var sheets = MarkSheets(path);
        if (_dialogs.Confirm($"Спецификация выгружена:\n{path}\n{sheets}\n\nОткрыть файл?"))
        {
            _dialogs.OpenFile(path);
        }
    }

    /// <summary>
    /// PDF по ГОСТ из текущего состояния окна. Таблица изменений берётся из файла Excel по умолчанию
    /// (рядом со сборкой), если он есть: там её ведут вручную.
    /// </summary>
    private void ExportPdf()
    {
        CommitEdits?.Invoke();

        // PDF — документ для выпуска: явные ошибки (нет позиции, повтор позиции, нет наименования) показываем заранее.
        var errors = SpecificationChecker.Check(Specification, _context).Where(i => i.Severity == IssueSeverity.Error).ToList();
        if (errors.Count > 0 && !_dialogs.Confirm(
                $"В спецификации ошибки: {errors.Count}.\n\n" +
                string.Join("\n", errors.Take(5).Select(e => "• " + e.Message)) +
                (errors.Count > 5 ? "\n…" : "") +
                "\n\nВсё равно создать PDF? Подробно — кнопка «Проверка»."))
        {
            return;
        }

        var info = _host.GetExportInfo();
        var path = _dialogs.AskPdfPath(System.IO.Path.ChangeExtension(info.DefaultPath, ".pdf"));
        if (path is null)
        {
            return;
        }

        try
        {
            KeepManualTitleData(info.TitleBlock, info.DefaultPath);
        }
        catch (System.IO.IOException)
        {
            // Файл Excel недоступен — PDF без изменений.
        }

        var excel = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"InventorGostSpec-{Guid.NewGuid():N}.xlsx");
        try
        {
            Excel.ExcelExporter.Export(Specification, _context, info.TitleBlock, excel);
            if (_host.BuildPdf(excel, path) is { } error)
            {
                _dialogs.Warning($"PDF не создан.\n\n{error}");
                return;
            }
        }
        catch (System.IO.IOException ex)
        {
            _dialogs.Warning($"Не удалось записать временный файл.\n\n{ex.Message}");
            return;
        }
        finally
        {
            try
            {
                System.IO.File.Delete(excel);
            }
            catch (Exception)
            {
                // Временный файл удалит система.
            }
        }

        if (_dialogs.Confirm($"PDF создан:\n{path}\n\nОткрыть файл?"))
        {
            _dialogs.OpenFile(path);
        }
    }

    private void OpenPdfSettings()
    {
        var excel = _host.GetExportInfo().DefaultPath;
        if (_host.OpenPdfSettings(System.IO.File.Exists(excel) ? excel : null) is { } error)
        {
            _dialogs.Warning(error);
        }
    }

    /// <summary>
    /// Разметка листов PDF в выгруженном файле (жирная линия на границе листов, колонка «Лист PDF»)
    /// с теми же настройками шрифта, что у утилиты PDF. Возвращает строку для сообщения.
    /// </summary>
    private static string MarkSheets(string path)
    {
        try
        {
            var count = Pdf.ExcelSheetMarker.Mark(path, Pdf.PdfSettings.Load().ToOptions());
            return $"Листов PDF: {count} (границы листов отмечены жирной линией).";
        }
        catch (Exception ex)
        {
            return $"Листы не размечены: {ex.Message}";
        }
    }

    /// <summary>
    /// Данные, которые ведут вручную в файле Excel (изменения, высота шрифта наименования), переносятся из прежнего
    /// файла. Файл недоступен — исключение IOException; не книга Excel — ничего не переносится.
    /// </summary>
    private static void KeepManualTitleData(TitleBlock title, string path)
    {
        TitleBlock? previous;
        try
        {
            previous = Excel.ExcelReader.ReadManualTitleData(path);
        }
        catch (Exception ex) when (ex is not System.IO.IOException)
        {
            return;
        }

        if (previous is null)
        {
            return;
        }

        if (title.Changes.Count == 0)
        {
            title.Changes = previous.Changes;
        }

        title.NameTextHeight ??= previous.NameTextHeight;

        // «Перв. примен.» берётся из свойства сборки; не задано — остаётся вписанное в Excel.
        if (title.FirstUse.Length == 0)
        {
            title.FirstUse = previous.FirstUse;
        }
    }

    /// <summary>
    /// Примечание из свойств детали → графа «Примечание» (только в этой спецификации).
    /// Если у строки уже другое примечание, сначала спрашиваем.
    /// </summary>
    private void InsertComponentNote()
    {
        var notes = new List<(SpecRow Row, string Note)>();
        var missing = 0;
        foreach (var row in TargetRows.Where(CanEditComponent).ToList())
        {
            var note = _host.ReadComponentNote(row.ComponentKey!)?.Trim() ?? "";
            if (note.Length == 0)
            {
                missing++;
                continue;
            }

            notes.Add((row, note));
        }

        if (notes.Count == 0)
        {
            _dialogs.Info("У выбранных деталей нет примечания в свойствах.");
            return;
        }

        var replaced = notes.Count(n => n.Row.Note.Length > 0 && n.Row.Note != n.Note);
        if (replaced > 0 && !_dialogs.Confirm($"Заменить уже заполненное примечание ({replaced})?"))
        {
            return;
        }

        foreach (var (row, note) in notes)
        {
            row.Note = note;
        }

        Changed(rebuild: true);
        if (missing > 0)
        {
            _dialogs.Info($"Вставлено примечаний: {notes.Count}. Без примечания в свойствах: {missing}.");
        }
    }

    /// <summary>
    /// Задаёт группу сортировки выбранным строкам. Порядок применяется при «По ГОСТ»
    /// (ручной порядок строк сам по себе не меняется).
    /// </summary>
    private void SetSortGroup(object? parameter)
    {
        if (parameter is not string value)
        {
            return;
        }

        foreach (var row in TargetRows.Where(r => r.Kind != RowKind.Note).ToList())
        {
            SpecificationEditor.SetCell(Specification, _context, row, SpecColumn.SortGroup, value);
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
        TakeSnapshot();
        _host.Save(Specification);
        _saved = _current;
        IsDirty = false;
    }

    private void Undo() => Step(_undo, _redo);

    private void Redo() => Step(_redo, _undo);

    /// <summary>Переход к снимку из <paramref name="from"/>; текущее состояние уходит в <paramref name="to"/>.</summary>
    private void Step(List<string> from, List<string> to)
    {
        CommitEdits?.Invoke();
        TakeSnapshot();
        if (from.Count == 0)
        {
            return;
        }

        to.Add(_current);
        _current = from[^1];
        from.RemoveAt(from.Count - 1);
        Specification = SpecificationSerializer.Deserialize(_current);
        _context = CreateContext();
        IsDirty = _current != _saved;
        OnPropertyChanged(nameof(ShowReserveRows));
        Rebuild();
        OnUndoChanged();
    }

    /// <summary>
    /// Запоминает текущее состояние как шаг отмены, если оно изменилось с прошлого снимка.
    /// Возвращает, было ли изменение.
    /// </summary>
    private bool TakeSnapshot()
    {
        var snapshot = SpecificationSerializer.Serialize(Specification);
        if (snapshot == _current)
        {
            return false;
        }

        _undo.Add(_current);
        if (_undo.Count > UndoLimit)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
        _current = snapshot;
        OnUndoChanged();
        return true;
    }

    private void OnUndoChanged()
    {
        OnPropertyChanged(nameof(UndoToolTip));
        OnPropertyChanged(nameof(RedoToolTip));
    }

    private void Check()
    {
        CommitEdits?.Invoke();
        var input = new CheckInput
        {
            Components = TryHost(_host.ReadComponents),
            BomItemNumbers = TryHost(_host.ReadBomItemNumbers),
            TitleBlock = TryHost(() => _host.GetExportInfo().TitleBlock),
        };
        var issues = SpecificationChecker.Check(Specification, _context, input);
        if (issues.Count == 0)
        {
            _dialogs.Info("Замечаний нет.");
            return;
        }

        if (_dialogs.ShowCheckResults(new CheckViewModel(issues)) is { } rowId)
        {
            GoToRow(rowId);
        }
    }

    /// <summary>Данные из Inventor для проверки; ошибка чтения — проверка без них (ошибка в журнал).</summary>
    private T? TryHost<T>(Func<T?> read) where T : class
    {
        try
        {
            return read();
        }
        catch (Exception ex)
        {
            _host.ReportError(ex);
            return null;
        }
    }

    /// <summary>Действие с файлами (папка сборки из Inventor); ошибка — в журнал, работа окна продолжается.</summary>
    private void TryHostAction(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _host.ReportError(ex);
        }
    }

    /// <summary>Выделяет строку и прокручивает к ней; если её скрывает поиск — поиск сбрасывается.</summary>
    private void GoToRow(string rowId)
    {
        if (IsSearching && Rows.All(r => r.RowId != rowId))
        {
            _searchText = "";
            OnPropertyChanged(nameof(SearchText));
            OnPropertyChanged(nameof(IsSearching));
        }

        _selectedRows.Clear();
        Rebuild(rowId);
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
        TakeSnapshot();
        IsDirty = _current != _saved;
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
        // «Документация» — не раздел модели: в неё попадают только чертежи и ручные строки.
        var sections = SpecificationContext.Create(_settings, new Specification()).Catalog.Sections
            .Where(s => !SectionResolver.IsDocumentation(s.Id))
            .ToList();
        var title = rows.Count == 1
            ? Describe(rows[0])
            : $"Выбрано компонентов: {rows.Count}. Меняются только изменённые поля.";
        var autoSections = rows.Select(r => _host.GetAutoSectionId(r.ComponentKey!)).Distinct().ToList();
        var viewModel = new AssignSectionViewModel(
            sections,
            initial,
            $"{title}\nСвойства записываются в файлы компонентов и действуют во всех сборках.",
            autoSections.FirstOrDefault(),
            autoSectionVaries: autoSections.Count > 1,
            currentValues: AssignSectionViewModel.CurrentValues(rows.Select(r => r.Source)));
        if (!_dialogs.EditComponentProperties(viewModel))
        {
            return;
        }

        var changes = viewModel.GetChanges();
        if (changes.Count == 0)
        {
            return;
        }

        // Ошибка записи одного компонента (файл только для чтения, занят) не прерывает остальные.
        var failed = new List<string>();
        foreach (var row in rows)
        {
            try
            {
                var data = _host.WriteComponentProperties(row.ComponentKey!, changes);
                SpecificationEditor.RefreshComponent(Specification, _context, row, data);
            }
            catch (Exception ex)
            {
                _host.ReportError(ex);
                failed.Add($"{Describe(row)}: {ex.Message}");
            }
        }

        Changed(rebuild: true, selectRowId: rows.Count == 1 ? rows[0].Id : null);
        if (failed.Count > 0)
        {
            _dialogs.Warning($"Не удалось записать свойства ({failed.Count} из {rows.Count}):\n" + string.Join("\n", failed.Take(10)));
        }
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
        AddCount(parts, "изменённых файлов документов", rows.Count(r => r.Status.HasFlag(RowStatus.FileChanged)));
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
