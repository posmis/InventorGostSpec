namespace InventorGostSpec.Core.Sections;

/// <summary>
/// Идентификаторы встроенных разделов спецификации по ГОСТ 2.106-2019.
/// Эти значения записываются в iProperty <see cref="Storage.PropertyNames.Section"/>.
/// </summary>
public static class SectionIds
{
    public const string Documentation = "Documentation";
    public const string Complexes = "Complexes";
    public const string AssemblyUnits = "AssemblyUnits";
    public const string Parts = "Parts";
    public const string StandardProducts = "StandardProducts";
    public const string OtherProducts = "OtherProducts";
    public const string Materials = "Materials";
    public const string Kits = "Kits";
}
