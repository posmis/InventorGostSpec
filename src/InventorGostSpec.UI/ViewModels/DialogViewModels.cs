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
        // У разделов без позиций («Документация») резерв — свободные строки без номера.
        SectionName = section.HasPositions ? section.Name : $"{section.Name} (свободные строки)";
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

/// <summary>
/// Основная надпись в окне спецификации: фамилии, организация и «Перв. примен.» записываются в iProperties сборки,
/// литера, шрифт наименования и изменения — в спецификацию.
/// </summary>
public sealed class TitleBlockViewModel : ObservableObject
{
    private static readonly System.Globalization.CultureInfo Russian = System.Globalization.CultureInfo.GetCultureInfo("ru-RU");

    public TitleBlockViewModel(TitleBlock title)
    {
        Designation = title.Designation;
        Name = title.Name;
        Developer = title.Developer;
        Checker = title.Checker;
        NormControl = title.NormControl;
        Approver = title.Approver;
        Organization = title.Organization;
        FirstUse = title.FirstUse;
        Letter = title.Letter;
        NameTextHeight = title.NameTextHeight?.ToString("0.##", Russian) ?? "";
        Changes = new System.Collections.ObjectModel.ObservableCollection<ChangeRecord>(title.Changes.Select(c => c.Clone()));
    }

    /// <summary>Обозначение и наименование — из iProperties сборки (Part Number, Description), здесь только для справки.</summary>
    public string Designation { get; }

    public string Name { get; }

    public string Developer { get; set; }

    public string Checker { get; set; }

    public string NormControl { get; set; }

    public string Approver { get; set; }

    public string Organization { get; set; }

    public string FirstUse { get; set; }

    public string Letter { get; set; }

    /// <summary>Высота шрифта наименования, мм; пусто — из настроек утилиты PDF.</summary>
    public string NameTextHeight { get; set; }

    /// <summary>Изменения по порядку внесения (в основной надписи — последние две).</summary>
    public System.Collections.ObjectModel.ObservableCollection<ChangeRecord> Changes { get; }

    public string? Validate() =>
        NameTextHeight.Trim().Length > 0 && ParseHeight() is null
            ? "Шрифт наименования — число мм (2,5; 3,5; 5; 7; 10) или пусто."
            : null;

    /// <summary>Фамилии, организация, «Перв. примен.» — для записи в iProperties сборки.</summary>
    public TitleBlock ToProperties() => new()
    {
        Developer = Developer.Trim(),
        Checker = Checker.Trim(),
        NormControl = NormControl.Trim(),
        Approver = Approver.Trim(),
        Organization = Organization.Trim(),
        FirstUse = FirstUse.Trim(),
    };

    /// <summary>Литера, шрифт и изменения — для хранения в спецификации.</summary>
    public SpecTitleData ToTitleData() => new()
    {
        IsSet = true,
        Letter = Letter.Trim(),
        NameTextHeight = ParseHeight(),
        Changes = Changes.Where(c => !c.IsEmpty).Select(Trimmed).ToList(),
    };

    private double? ParseHeight() =>
        double.TryParse(NameTextHeight.Trim().Replace('.', ','), System.Globalization.NumberStyles.Float, Russian, out var value) && value > 0 && value < 100
            ? value
            : null;

    private static ChangeRecord Trimmed(ChangeRecord c) => new()
    {
        Number = (c.Number ?? "").Trim(),
        Areas = (c.Areas ?? "").Trim(),
        Sheet = (c.Sheet ?? "").Trim(),
        Document = (c.Document ?? "").Trim(),
        Signature = (c.Signature ?? "").Trim(),
        Date = (c.Date ?? "").Trim(),
    };
}
