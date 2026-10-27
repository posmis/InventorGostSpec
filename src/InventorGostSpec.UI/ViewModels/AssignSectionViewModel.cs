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

    public PropertyField(string propertyName, string label, string? initialValue)
    {
        PropertyName = propertyName;
        Label = label;
        IsMixed = initialValue is null;
        _value = initialValue ?? "";
    }

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

    private Option _selectedSection;
    private Option _selectedMaterialKind;
    private bool _sectionChanged;
    private bool _materialKindChanged;

    /// <param name="sections">Доступные разделы.</param>
    /// <param name="initialValues">
    /// Текущие значения свойств: имя свойства → значение; <c>null</c> — у выбранных компонентов значения различаются.
    /// </param>
    /// <param name="targetDescription">Что выбрано, например «Выбрано компонентов: 3».</param>
    public AssignSectionViewModel(
        IEnumerable<SectionDefinition> sections,
        IReadOnlyDictionary<string, string?> initialValues,
        string targetDescription)
    {
        TargetDescription = targetDescription;

        Sections = new[] { AutoSection }
            .Concat(sections.Select(s => new Option(s.Id, s.Name)))
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

        Format = new PropertyField(PropertyNames.Format, "Формат", Initial(initialValues, PropertyNames.Format));
        Unit = new PropertyField(PropertyNames.Unit, "Единица измерения", Initial(initialValues, PropertyNames.Unit));
        Group = new PropertyField(PropertyNames.Group, "Группа изделий", Initial(initialValues, PropertyNames.Group));
        SortName = new PropertyField(PropertyNames.SortName, "Наименование изделия", Initial(initialValues, PropertyNames.SortName));
        StandardNumber = new PropertyField(PropertyNames.StandardNumber, "Обозначение стандарта", Initial(initialValues, PropertyNames.StandardNumber));
        Parameters = new PropertyField(PropertyNames.Parameters, "Параметры, размеры", Initial(initialValues, PropertyNames.Parameters));
    }

    public string TargetDescription { get; }

    public List<Option> Sections { get; }

    public List<Option> MaterialKinds { get; }

    public PropertyField Format { get; }

    public PropertyField Unit { get; }

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

    /// <summary>Поля сортировки нужны стандартным и прочим изделиям.</summary>
    public bool ShowProductFields => SelectedSection.Id is SectionIds.StandardProducts or SectionIds.OtherProducts or SectionIds.Materials
                                     || ReferenceEquals(SelectedSection, MixedSection);

    public bool ShowMaterialFields => SelectedSection.Id is SectionIds.Materials || ReferenceEquals(SelectedSection, MixedSection);

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

        foreach (var field in new[] { Format, Unit, Group, SortName, StandardNumber, Parameters })
        {
            if (field.IsChanged)
            {
                changes[field.PropertyName] = field.Value.Trim();
            }
        }

        return changes;
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
