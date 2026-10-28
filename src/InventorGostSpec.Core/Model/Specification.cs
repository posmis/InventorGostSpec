using InventorGostSpec.Core.Sections;

namespace InventorGostSpec.Core.Model;

/// <summary>Резерв номеров позиций после раздела.</summary>
public sealed class ReserveSettings
{
    public static ReserveSettings None => new();

    /// <summary>Сколько номеров позиций оставить свободными после раздела.</summary>
    public int Count { get; set; }

    /// <summary>
    /// Если больше 1, номер первой позиции раздела округляется вверх до кратного этому значению
    /// (например, 10: детали начинаются с 10, 20, ...). Первый раздел всегда начинается с 1.
    /// </summary>
    public int RoundStartTo { get; set; }

    public ReserveSettings Clone() => (ReserveSettings)MemberwiseClone();
}

/// <summary>Спецификация сборки. Хранится внутри файла сборки.</summary>
public sealed class Specification
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>Строки в порядке отображения, сгруппированные по разделам.</summary>
    public List<SpecRow> Rows { get; set; } = new();

    /// <summary>Группы составных частей, устанавливаемых по отдельным документам, в порядке записи.</summary>
    public List<SpecGroup> Groups { get; set; } = new();

    /// <summary>Разделы, добавленные только в эту спецификацию.</summary>
    public List<SectionDefinition> CustomSections { get; set; } = new();

    /// <summary>
    /// Разделы, вставленные вручную и показываемые даже без строк (заголовок и резервные строки),
    /// например «Документация», когда чертежа ещё нет.
    /// </summary>
    public List<ShownSection> ShownSections { get; set; } = new();

    /// <summary>
    /// Разделы (в основной части или в группе), которые начинаются с нового листа. По умолчанию — ни один.
    /// Группа целиком с нового листа — <see cref="SpecGroup.StartsNewPage"/>.
    /// </summary>
    public List<ShownSection> NewPageSections { get; set; } = new();

    /// <summary>Начинается ли с нового листа заголовок раздела или группы (<paramref name="sectionId"/> пусто — группа).</summary>
    public bool StartsNewPage(string groupId, string sectionId) =>
        sectionId.Length == 0
            ? Groups.FirstOrDefault(g => g.Id == groupId)?.StartsNewPage == true
            : NewPageSections.Any(s => s.Matches(groupId, sectionId));

    /// <summary>Включает или выключает «с нового листа» у раздела или группы (<paramref name="sectionId"/> пусто — группа).</summary>
    public void SetStartsNewPage(string groupId, string sectionId, bool value)
    {
        if (sectionId.Length == 0)
        {
            if (Groups.FirstOrDefault(g => g.Id == groupId) is { } group)
            {
                group.StartsNewPage = value;
            }

            return;
        }

        NewPageSections.RemoveAll(s => s.Matches(groupId, sectionId));
        if (value)
        {
            NewPageSections.Add(new ShownSection { GroupId = groupId, SectionId = sectionId });
        }
    }

    /// <summary>Литера, шрифт наименования и изменения для основной надписи (вводятся в окне спецификации).</summary>
    public SpecTitleData Title { get; set; } = new();

    /// <summary>Резервы позиций этой спецификации, важнее глобальных настроек.</summary>
    public Dictionary<string, ReserveSettings> ReserveOverrides { get; set; } = new();

    /// <summary>
    /// Раздел «Переменные данные для исполнений» показывается и без исполнений (галочка в окне): основное исполнение
    /// с заголовком-обозначением, в него можно переносить строки. Без галочки раздел появляется с первым исполнением.
    /// </summary>
    public bool ShowVariableData { get; set; }

    /// <summary>Показывать ли пустые строки резервных позиций.</summary>
    public bool ShowReserveRows { get; set; } = true;

    /// <summary>Время последнего обновления из сборки. Пусто — спецификация ещё не формировалась.</summary>
    public DateTime? LastUpdatedUtc { get; set; }

    public SpecRow? FindRow(string rowId) => Rows.FirstOrDefault(r => r.Id == rowId);
}

/// <summary>Глобальные настройки (хранятся в %AppData%).</summary>
public sealed class SpecSettings
{
    public List<SectionDefinition> CustomSections { get; set; } = new();

    public Dictionary<string, ReserveSettings> DefaultReserves { get; set; } = new();

    /// <summary>
    /// Пользовательское свойство детали с примечанием для команды «Примечание из детали».
    /// Если его нет — берётся стандартное «Комментарии» (Comments).
    /// </summary>
    public string NotePropertyName { get; set; } = "Примечание";

    /// <summary>
    /// Что писать в графе «Формат» у детали без чертежа (только у деталей): «БЧ» (по умолчанию) или пусто.
    /// </summary>
    public string PartWithoutDrawingFormat { get; set; } = SheetFormats.NoDrawing;

    /// <summary>Формат детали без чертежа по настройке: «БЧ» или пусто.</summary>
    public string NoDrawingFormat() => PartWithoutDrawingFormat == "" ? "" : SheetFormats.NoDrawing;
}

/// <summary>Разделы и резервы, действующие для конкретной спецификации.</summary>
public sealed class SpecificationContext
{
    private readonly Dictionary<string, ReserveSettings> _reserves;
    private readonly List<SpecGroup> _groups;

    private SpecificationContext(SectionCatalog catalog, Dictionary<string, ReserveSettings> reserves, List<SpecGroup> groups)
    {
        Catalog = catalog;
        _reserves = reserves;
        _groups = groups;
    }

    public SectionCatalog Catalog { get; }

    /// <summary>Группы спецификации (живой список: добавленные группы видны сразу).</summary>
    public IReadOnlyList<SpecGroup> Groups => _groups;

    public SpecGroup? FindGroup(string? groupId) =>
        string.IsNullOrEmpty(groupId) ? null : _groups.FirstOrDefault(g => g.Id == groupId);

    /// <summary>0 — основная часть, 1… — группы по порядку.</summary>
    public int GroupIndex(string? groupId)
    {
        if (string.IsNullOrEmpty(groupId))
        {
            return 0;
        }

        var index = _groups.FindIndex(g => g.Id == groupId);
        return index < 0 ? 0 : index + 1;
    }

    /// <summary>Резерв раздела в группе; если в группе не задан — как в основной части.</summary>
    public ReserveSettings GetReserve(string? groupId, string sectionId) =>
        FindGroup(groupId)?.Reserves.TryGetValue(sectionId, out var reserve) == true ? reserve : GetReserve(sectionId);

    public static SpecificationContext Create(SpecSettings? settings, Specification specification)
    {
        settings ??= new SpecSettings();

        var catalog = new SectionCatalog(
            SectionCatalog.CreateBuiltIn()
                .Concat(settings.CustomSections.Select(s => s.Clone()))
                .Concat(specification.CustomSections.Select(s => s.Clone())));

        var reserves = new Dictionary<string, ReserveSettings>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, reserve) in settings.DefaultReserves)
        {
            reserves[id] = reserve.Clone();
        }

        foreach (var (id, reserve) in specification.ReserveOverrides)
        {
            reserves[id] = reserve.Clone();
        }

        return new SpecificationContext(catalog, reserves, specification.Groups);
    }

    /// <summary>Резерв раздела основной части.</summary>
    public ReserveSettings GetReserve(string sectionId) =>
        _reserves.TryGetValue(sectionId, out var reserve) ? reserve : ReserveSettings.None;
}
