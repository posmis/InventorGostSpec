using InventorGostSpec.Core.Model;
using InventorGostSpec.Core.Sections;
using InventorGostSpec.Core.Storage;

namespace InventorGostSpec.UI.ViewModels;

public sealed record Option(string Id, string Name);

/// <summary>
/// Поле iProperty в окне назначения. Записывается только если пользователь его изменил,
/// поэтому при выборе нескольких компонентов с разными значениями ничего не затирается.
/// </summary>
public sealed class PropertyField : ObservableObject
{
    private string _value;
    private bool _isChanged;

    public PropertyField(string propertyName, string label, string? initialValue, string currentValue = "")
    {
        PropertyName = propertyName;
        Label = label;
        IsMixed = initialValue is null;
        _value = initialValue ?? "";
        CurrentValue = currentValue;
    }

    /// <summary>
    /// Что сейчас идёт в спецификацию, если поле не заполнено (например, разобрано из наименования) —
    /// показывается серым в пустом поле.
    /// </summary>
    public string CurrentValue { get; }

    /// <summary>Показать <see cref="CurrentValue"/>: поле пустое и это не «разные значения».</summary>
    public bool ShowCurrentValue => CurrentValue.Length > 0 && _value.Length == 0 && (!IsMixed || IsChanged);

    public string PropertyName { get; }

    public string Label { get; }

    /// <summary>У выбранных компонентов разные значения.</summary>
    public bool IsMixed { get; }

    public string Value
    {
        get => _value;
        set
        {
            if (SetField(ref _value, value ?? ""))
            {
                IsChanged = true;
                OnPropertyChanged(nameof(ShowCurrentValue));
            }
        }
    }

    public bool IsChanged
    {
        get => _isChanged;
        private set => SetField(ref _isChanged, value);
    }
}

/// <summary>Окно назначения раздела и полей спецификации выбранным компонентам.</summary>
public sealed class AssignSectionViewModel : ObservableObject
{
    /// <summary>Пункт «Автоматически»: свойство раздела удаляется, раздел определяется по правилам.</summary>
    public static readonly Option AutoSection = new("", "Автоматически (по типу компонента)");

    private static readonly Option MixedSection = new("\u0000mixed", "(разные значения)");

    private readonly string? _autoSectionId;
    private Option _selectedSection;
    private Option _selectedMaterialKind;
    private bool _sectionChanged;
    private bool _materialKindChanged;

    /// <param name="sections">Доступные разделы.</param>
    /// <param name="initialValues">
    /// Текущие значения свойств: имя свойства → значение; <c>null</c> — у выбранных компонентов значения различаются.
    /// </param>
    /// <param name="targetDescription">Что выбрано, например «Выбрано компонентов: 3».</param>
    /// <param name="autoSectionId">
    /// Раздел, который компонент получит автоматически (по типу). <c>null</c> — неизвестен
    /// или у выбранных компонентов разный (<paramref name="autoSectionVaries"/>).
    /// </param>
    /// <param name="currentValues">
    /// Значения, которые сейчас идут в спецификацию (см. <see cref="CurrentValues"/>): показываются серым в пустых полях.
    /// </param>
    public AssignSectionViewModel(
        IEnumerable<SectionDefinition> sections,
        IReadOnlyDictionary<string, string?> initialValues,
        string targetDescription,
        string? autoSectionId = null,
        bool autoSectionVaries = false,
        IReadOnlyDictionary<string, string>? currentValues = null)
    {
        currentValues ??= new Dictionary<string, string>();
        string Current(string name) => currentValues.TryGetValue(name, out var value) ? value : "";

        TargetDescription = targetDescription;
        var sectionList = sections.ToList();
        _autoSectionId = autoSectionVaries ? null : autoSectionId;

        // В пункте «Автоматически» сразу видно, какой раздел получится.
        var autoName = _autoSectionId is not null && sectionList.FirstOrDefault(s => s.Id == _autoSectionId) is { } auto
            ? $"Автоматически — {auto.Name}"
            : autoSectionVaries
                ? "Автоматически — по типу каждого компонента"
                : AutoSection.Name;

        Sections = new[] { new Option("", autoName) }
            .Concat(sectionList.Select(s => new Option(s.Id, s.Name)))
            .ToList();
        var section = Initial(initialValues, PropertyNames.Section);
        _selectedSection = section is null
            ? MixedSection
            : Sections.FirstOrDefault(o => string.Equals(o.Id, section, StringComparison.OrdinalIgnoreCase))
              ?? AddUnknown(Sections, section);
        if (ReferenceEquals(_selectedSection, MixedSection))
        {
            Sections.Insert(0, MixedSection);
        }

        MaterialKinds = new[] { new Option("", "(не задан)") }
            .Concat(Enum.GetValues<MaterialKind>().Select(k => new Option(k.ToString(), Core.Sections.MaterialKinds.DisplayName(k))))
            .ToList();
        var kind = Initial(initialValues, PropertyNames.MaterialKind);
        _selectedMaterialKind = kind is null
            ? MixedSection
            : MaterialKinds.FirstOrDefault(o => string.Equals(o.Id, kind, StringComparison.OrdinalIgnoreCase)) ?? MaterialKinds[0];
        if (ReferenceEquals(_selectedMaterialKind, MixedSection))
        {
            MaterialKinds.Insert(0, MixedSection);
        }

        PropertyField Field(string name, string label) => new(name, label, Initial(initialValues, name), Current(name));
        Format = Field(PropertyNames.Format, "Формат");
        Unit = Field(PropertyNames.Unit, "Единица измерения");
        FirstUse = Field(PropertyNames.FirstUse, "Перв. примен.");
        Group = Field(PropertyNames.Group, "Группа изделий");
        SortName = Field(PropertyNames.SortName, "Наименование изделия");
        StandardNumber = Field(PropertyNames.StandardNumber, "Обозначение стандарта");
        Parameters = Field(PropertyNames.Parameters, "Параметры, размеры");
    }

    public string TargetDescription { get; }

    public List<Option> Sections { get; }

    public List<Option> MaterialKinds { get; }

    public PropertyField Format { get; }

    public PropertyField Unit { get; }

    /// <summary>Первичная применяемость — свойство компонента (графа «Перв. примен.» на его чертеже).</summary>
    public PropertyField FirstUse { get; }

    public PropertyField Group { get; }

    public PropertyField SortName { get; }

    public PropertyField StandardNumber { get; }

    public PropertyField Parameters { get; }

    public Option SelectedSection
    {
        get => _selectedSection;
        set
        {
            if (value is not null && SetField(ref _selectedSection, value))
            {
                _sectionChanged = true;
                OnPropertyChanged(nameof(ShowProductFields));
                OnPropertyChanged(nameof(ShowMaterialFields));
            }
        }
    }

    public Option SelectedMaterialKind
    {
        get => _selectedMaterialKind;
        set
        {
            if (value is not null && SetField(ref _selectedMaterialKind, value))
            {
                _materialKindChanged = true;
            }
        }
    }

    /// <summary>Раздел, который получится: выбранный или автоматический. <c>null</c> — разные значения.</summary>
    private string? EffectiveSectionId =>
        ReferenceEquals(SelectedSection, MixedSection) ? null : SelectedSection.Id.Length > 0 ? SelectedSection.Id : _autoSectionId;

    /// <summary>Поля сортировки нужны стандартным и прочим изделиям и материалам (при разных значениях — доступны).</summary>
    public bool ShowProductFields => EffectiveSectionId is null or SectionIds.StandardProducts or SectionIds.OtherProducts or SectionIds.Materials;

    public bool ShowMaterialFields => EffectiveSectionId is null or SectionIds.Materials;

    /// <summary>
    /// Изменения для записи: имя свойства → значение. Пустое значение означает удаление свойства.
    /// </summary>
    public IReadOnlyDictionary<string, string> GetChanges()
    {
        var changes = new Dictionary<string, string>();
        if (_sectionChanged && !ReferenceEquals(SelectedSection, MixedSection))
        {
            changes[PropertyNames.Section] = SelectedSection.Id;
        }

        if (_materialKindChanged && !ReferenceEquals(SelectedMaterialKind, MixedSection))
        {
            changes[PropertyNames.MaterialKind] = SelectedMaterialKind.Id;
        }

        foreach (var field in new[] { Format, Unit, FirstUse, Group, SortName, StandardNumber, Parameters })
        {
            if (field.IsChanged)
            {
                changes[field.PropertyName] = field.Value.Trim();
            }
        }

        return changes;
    }

    /// <summary>
    /// Что сейчас идёт в спецификацию у компонентов (с разбором наименования, форматом А4 у сборочных единиц):
    /// имя свойства → значение. Если у выбранных компонентов значения различаются — значение не показывается.
    /// </summary>
    public static IReadOnlyDictionary<string, string> CurrentValues(IEnumerable<ComponentSnapshot> snapshots)
    {
        var list = snapshots.ToList();
        var result = new Dictionary<string, string>();
        void Add(string name, Func<ComponentSnapshot, string> get)
        {
            var values = list.Select(get).Distinct(StringComparer.Ordinal).Take(2).ToList();
            if (values.Count == 1 && values[0].Length > 0)
            {
                result[name] = values[0];
            }
        }

        Add(PropertyNames.Format, s => s.Format);
        Add(PropertyNames.Unit, s => s.Unit);
        Add(PropertyNames.Group, s => s.Group);
        Add(PropertyNames.SortName, s => s.SortName);
        Add(PropertyNames.StandardNumber, s => s.StandardNumber);
        Add(PropertyNames.Parameters, s => s.Parameters);
        return result;
    }

    private static string? Initial(IReadOnlyDictionary<string, string?> values, string name) =>
        values.TryGetValue(name, out var value) ? value : "";

    private static Option AddUnknown(List<Option> options, string id)
    {
        var option = new Option(id, $"{id} (нет в справочнике)");
        options.Add(option);
        return option;
    }
}
