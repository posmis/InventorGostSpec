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

    /// <summary>Разделы, добавленные только в эту спецификацию.</summary>
    public List<SectionDefinition> CustomSections { get; set; } = new();

    /// <summary>Резервы позиций этой спецификации, важнее глобальных настроек.</summary>
    public Dictionary<string, ReserveSettings> ReserveOverrides { get; set; } = new();

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
}

/// <summary>Разделы и резервы, действующие для конкретной спецификации.</summary>
public sealed class SpecificationContext
{
    private readonly Dictionary<string, ReserveSettings> _reserves;

    private SpecificationContext(SectionCatalog catalog, Dictionary<string, ReserveSettings> reserves)
    {
        Catalog = catalog;
        _reserves = reserves;
    }

    public SectionCatalog Catalog { get; }

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

        return new SpecificationContext(catalog, reserves);
    }

    public ReserveSettings GetReserve(string sectionId) =>
        _reserves.TryGetValue(sectionId, out var reserve) ? reserve : ReserveSettings.None;
}
