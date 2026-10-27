namespace InventorGostSpec.Core.Sections;

/// <summary>
/// Виды материалов в последовательности записи в разделе «Материалы» (ГОСТ 2.106-2019).
/// Имя значения записывается в iProperty <see cref="Storage.PropertyNames.MaterialKind"/>.
/// </summary>
public enum MaterialKind
{
    FerrousMetals = 1,
    MagneticMetals,
    NonFerrousMetals,
    CablesWires,
    Plastics,
    PaperTextile,
    Wood,
    RubberLeather,
    MineralCeramicGlass,
    PaintsChemicals,
    Other,
}

public static class MaterialKinds
{
    private static readonly Dictionary<MaterialKind, string> Names = new()
    {
        [MaterialKind.FerrousMetals] = "Металлы черные",
        [MaterialKind.MagneticMetals] = "Металлы магнитоэлектрические и ферромагнитные",
        [MaterialKind.NonFerrousMetals] = "Металлы цветные, благородные и редкие",
        [MaterialKind.CablesWires] = "Кабели, провода и шнуры",
        [MaterialKind.Plastics] = "Пластмассы и пресс-материалы",
        [MaterialKind.PaperTextile] = "Бумажные и текстильные материалы",
        [MaterialKind.Wood] = "Лесоматериалы",
        [MaterialKind.RubberLeather] = "Резиновые и кожевенные материалы",
        [MaterialKind.MineralCeramicGlass] = "Минеральные, керамические и стеклянные материалы",
        [MaterialKind.PaintsChemicals] = "Лаки, краски, нефтепродукты и химикаты",
        [MaterialKind.Other] = "Прочие материалы",
    };

    public static string DisplayName(MaterialKind kind) => Names[kind];

    /// <summary>Разбирает значение iProperty. Пустое или неизвестное значение означает «Прочие материалы».</summary>
    public static MaterialKind Parse(string? value) =>
        Enum.TryParse<MaterialKind>(value?.Trim(), ignoreCase: true, out var kind) && Enum.IsDefined(kind)
            ? kind
            : MaterialKind.Other;
}
