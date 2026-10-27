using InventorGostSpec.Core.Model;
using InventorGostSpec.Core.Sections;

namespace InventorGostSpec.UI.ViewModels;

/// <summary>Новый раздел спецификации.</summary>
public sealed class NewSectionViewModel : ObservableObject
{
    private string _name = "";

    public NewSectionViewModel(IReadOnlyList<SectionDefinition> existing)
    {
        Existing = existing;
        PlaceAfterOptions = existing.Select(s => new Option(s.Id, s.Name)).ToList();
        PlaceAfter = PlaceAfterOptions.LastOrDefault();
        SortRules = new List<Option>
        {
            new(Core.Sections.SortRule.ByDesignation.ToString(), "По обозначению"),
            new(Core.Sections.SortRule.ByName.ToString(), "По наименованию"),
            new(Core.Sections.SortRule.OtherProducts.ToString(), "Как прочие изделия (группа, наименование, параметры)"),
            new(Core.Sections.SortRule.StandardProducts.ToString(), "Как стандартные изделия"),
            new(Core.Sections.SortRule.Materials.ToString(), "Как материалы"),
            new(Core.Sections.SortRule.Documentation.ToString(), "Как документация"),
        };
        SortRule = SortRules[0];
    }

    public IReadOnlyList<SectionDefinition> Existing { get; }

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value ?? "");
    }

    public List<Option> PlaceAfterOptions { get; }

    public Option? PlaceAfter { get; set; }

    public List<Option> SortRules { get; }

    public Option SortRule { get; set; }

    public bool HasPositions { get; set; } = true;

    /// <summary>Добавить раздел в общий справочник (доступен во всех сборках и в iProperties).</summary>
    public bool SaveGlobally { get; set; }

    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            return "Введите название раздела.";
        }

        return Existing.Any(s => string.Equals(s.Name, Name.Trim(), StringComparison.CurrentCultureIgnoreCase))
            ? "Раздел с таким названием уже есть."
            : null;
    }

    /// <summary>Раздел с порядком между выбранным и следующим за ним.</summary>
    public SectionDefinition CreateSection()
    {
        var index = Existing.ToList().FindIndex(s => s.Id == PlaceAfter?.Id);
        var before = index >= 0 ? Existing[index].Order : 0;
        var after = index + 1 < Existing.Count ? Existing[index + 1].Order : before + 200;
        var order = after - before > 1 ? before + (after - before) / 2 : before + 1;

        return new SectionDefinition
        {
            Id = "Custom_" + Guid.NewGuid().ToString("N")[..8],
            Name = Name.Trim(),
            Order = order,
            SortRule = Enum.Parse<Core.Sections.SortRule>(SortRule.Id),
            HasPositions = HasPositions,
        };
    }
}

public sealed class ReserveItem : ObservableObject
{
    private int _count;
    private int _roundStartTo;

    public ReserveItem(SectionDefinition section, ReserveSettings settings)
    {
        SectionId = section.Id;
        SectionName = section.Name;
        _count = settings.Count;
        _roundStartTo = settings.RoundStartTo;
    }

    public string SectionId { get; }

    public string SectionName { get; }

    /// <summary>Сколько номеров позиций оставить свободными после раздела.</summary>
    public int Count
    {
        get => _count;
        set => SetField(ref _count, Math.Max(0, value));
    }

    /// <summary>Округлять номер первой позиции раздела до кратного (0 — не округлять).</summary>
    public int RoundStartTo
    {
        get => _roundStartTo;
        set => SetField(ref _roundStartTo, Math.Max(0, value));
    }

    public ReserveSettings ToSettings() => new() { Count = Count, RoundStartTo = RoundStartTo };
}

/// <summary>Резерв позиций одной большой части спецификации: основной части или группы.</summary>
public sealed class ReservePart
{
    public ReservePart(string groupId, string title, IEnumerable<ReserveItem> items)
    {
        GroupId = groupId;
        Title = title;
        Items = items.ToList();
    }

    /// <summary>Пусто — основная часть.</summary>
    public string GroupId { get; }

    public string Title { get; }

    public List<ReserveItem> Items { get; }

    public bool IsGroup => GroupId.Length > 0;
}

/// <summary>Резерв номеров позиций по разделам, отдельно для основной части и каждой группы.</summary>
public sealed class ReserveSettingsViewModel : ObservableObject
{
    private ReservePart _selectedPart;

    public ReserveSettingsViewModel(IEnumerable<ReserveItem> items)
        : this(new[] { new ReservePart("", "Основная часть", items) })
    {
    }

    public ReserveSettingsViewModel(IEnumerable<ReservePart> parts)
    {
        Parts = parts.ToList();
        _selectedPart = Parts[0];
    }

    public List<ReservePart> Parts { get; }

    public ReservePart SelectedPart
    {
        get => _selectedPart;
        set
        {
            if (value is not null && SetField(ref _selectedPart, value))
            {
                OnPropertyChanged(nameof(Items));
            }
        }
    }

    /// <summary>Разделы выбранной части.</summary>
    public List<ReserveItem> Items => SelectedPart.Items;

    /// <summary>Сохранить резерв основной части как значения по умолчанию для новых спецификаций.</summary>
    public bool SaveAsDefault { get; set; }

    /// <summary>Перенумеровать позиции с новым резервом.</summary>
    public bool Renumber { get; set; } = true;
}

/// <summary>Группа «устанавливаемые по …».</summary>
public sealed class GroupViewModel : ObservableObject
{
    private string _title = "Изделия, устанавливаемые по электромонтажному чертежу ";

    public string Title
    {
        get => _title;
        set => SetField(ref _title, value ?? "");
    }

    public string? Validate() => string.IsNullOrWhiteSpace(Title) ? "Введите заголовок группы." : null;
}
