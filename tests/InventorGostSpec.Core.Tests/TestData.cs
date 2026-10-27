using InventorGostSpec.Core.Model;
using InventorGostSpec.Core.Sections;

namespace InventorGostSpec.Core.Tests;

internal static class TestData
{
    public static readonly DateTime Now = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);

    public static ComponentData Part(string designation, string name, decimal quantity = 1) => new()
    {
        Key = designation + ".ipt",
        Kind = ComponentKind.Part,
        Designation = designation,
        Name = name,
        Quantity = quantity,
    };

    public static ComponentData Assembly(string designation, string name, decimal quantity = 1) => new()
    {
        Key = designation + ".iam",
        Kind = ComponentKind.Assembly,
        Designation = designation,
        Name = name,
        Quantity = quantity,
    };

    public static ComponentData Standard(string name, decimal quantity = 1) => new()
    {
        Key = name + ".ipt",
        Kind = ComponentKind.Part,
        IsContentCenter = true,
        Name = name,
        Quantity = quantity,
    };

    public static ComponentData Material(string name, MaterialKind kind) => new()
    {
        Key = name,
        Kind = ComponentKind.Virtual,
        SectionId = SectionIds.Materials,
        Name = name,
        MaterialKind = kind.ToString(),
        Quantity = 1,
    };

    public static SpecRow Row(string sectionId, string designation = "", string name = "") => new()
    {
        Kind = RowKind.ManualItem,
        Source = new ComponentSnapshot { SectionId = sectionId, Designation = designation, Name = name },
    };

    public static SpecificationContext Context(Specification spec, params (string Section, int Count, int Round)[] reserves)
    {
        var settings = new SpecSettings();
        foreach (var (section, count, round) in reserves)
        {
            settings.DefaultReserves[section] = new ReserveSettings { Count = count, RoundStartTo = round };
        }

        return SpecificationContext.Create(settings, spec);
    }

    public static IEnumerable<string> Names(Specification spec) => spec.Rows.Select(r => r.Get(SpecColumn.Name));

    public static SpecRow ByName(Specification spec, string name) => spec.Rows.Single(r => r.Get(SpecColumn.Name) == name);
}
