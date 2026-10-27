using InventorGostSpec.Core.Checking;
using InventorGostSpec.Core.Editing;
using InventorGostSpec.Core.Model;
using InventorGostSpec.Core.Positions;
using InventorGostSpec.Core.Sections;
using InventorGostSpec.Core.Storage;
using InventorGostSpec.Core.View;

namespace InventorGostSpec.Core.Tests;

/// <summary>Разделы, вставленные вручную: показываются без строк, заголовком и резервом.</summary>
public class ShownSectionTests
{
    private static (Specification Spec, SpecificationContext Context) Spec(params (string Section, int Count, int Round)[] reserves)
    {
        var spec = new Specification
        {
            Rows =
            {
                TestData.Row(SectionIds.AssemblyUnits, "А.1", "Узел"),
                TestData.Row(SectionIds.StandardProducts, name: "Болт"),
            },
        };
        var context = TestData.Context(spec, reserves);
        PositionAllocator.RenumberAll(spec, context);
        return (spec, context);
    }

    private static List<(DisplayRowKind Kind, string Name, int? Position)> View(Specification spec, SpecificationContext context) =>
        SpecificationViewBuilder.Build(spec, context)
            .Select(r => (r.Kind, r.Row?.Get(SpecColumn.Name) ?? r.SectionName, r.ReservedPosition ?? r.Row?.Position))
            .ToList();

    [Fact]
    public void Empty_documentation_shows_header_and_free_lines()
    {
        var (spec, context) = Spec((SectionIds.Documentation, 2, 0));

        SpecificationEditor.ShowSection(spec, context, "", SectionIds.Documentation);

        Assert.Equal(
            new (DisplayRowKind, string, int?)[]
            {
                (DisplayRowKind.SectionHeader, "Документация", null),
                (DisplayRowKind.Reserve, "Документация", null),
                (DisplayRowKind.Reserve, "Документация", null),
            },
            View(spec, context).Take(3));
    }

    [Fact]
    public void Empty_numbered_section_gets_its_reserve_numbers_after_renumbering()
    {
        // Узел — поз. 1, резерв СЕ 1 (поз. 2); пустые «Детали» с резервом 3 — поз. 3–5; болт — с 6.
        var (spec, context) = Spec((SectionIds.AssemblyUnits, 1, 0), (SectionIds.Parts, 3, 0));
        SpecificationEditor.ShowSection(spec, context, "", SectionIds.Parts);

        // До перенумерации свободных номеров между узлом (1) и болтом (2) нет.
        Assert.Empty(PositionAllocator.GetReservedNumbers(spec, context, SectionIds.Parts));

        PositionAllocator.RenumberAll(spec, context);

        Assert.Equal(new[] { 2 }, PositionAllocator.GetReservedNumbers(spec, context, SectionIds.AssemblyUnits));
        Assert.Equal(new[] { 3, 4, 5 }, PositionAllocator.GetReservedNumbers(spec, context, SectionIds.Parts));
        Assert.Equal(6, TestData.ByName(spec, "Болт").Position);
        Assert.Contains((DisplayRowKind.Reserve, "Детали", 4), View(spec, context));
    }

    [Fact]
    public void Empty_section_at_the_end_and_rounding()
    {
        var (spec, context) = Spec((SectionIds.StandardProducts, 1, 0), (SectionIds.Materials, 2, 10));
        SpecificationEditor.ShowSection(spec, context, "", SectionIds.Materials);

        PositionAllocator.RenumberAll(spec, context);

        // Болт — 2, его резерв — 3, материалы — с 10 (округление), два номера.
        Assert.Equal(new[] { 10, 11 }, PositionAllocator.GetReservedNumbers(spec, context, SectionIds.Materials));
    }

    [Fact]
    public void New_row_in_inserted_section_takes_its_reserve_and_others_skip_it()
    {
        var (spec, context) = Spec((SectionIds.AssemblyUnits, 1, 0), (SectionIds.Parts, 3, 0));
        SpecificationEditor.ShowSection(spec, context, "", SectionIds.Parts);
        PositionAllocator.RenumberAll(spec, context);

        var part = SpecificationEditor.AddManualItem(spec, context, new ComponentSnapshot { SectionId = SectionIds.Parts, Name = "Корпус" });
        var unit = SpecificationEditor.AddManualItem(spec, context, new ComponentSnapshot { SectionId = SectionIds.AssemblyUnits, Name = "Узел 2" });

        Assert.Equal(3, part.Position);
        Assert.Equal(2, unit.Position);
        Assert.Equal(new[] { 4, 5 }, PositionAllocator.GetReservedNumbers(spec, context, SectionIds.Parts));
    }

    [Fact]
    public void Row_of_later_section_does_not_take_reserve_of_empty_inserted_section()
    {
        var (spec, context) = Spec((SectionIds.AssemblyUnits, 0, 0), (SectionIds.Parts, 2, 0), (SectionIds.StandardProducts, 0, 0));
        spec.Rows.RemoveAll(r => r.SectionId == SectionIds.StandardProducts);
        SpecificationEditor.ShowSection(spec, context, "", SectionIds.Parts);
        PositionAllocator.RenumberAll(spec, context);

        var bolt = SpecificationEditor.AddManualItem(spec, context, new ComponentSnapshot { SectionId = SectionIds.StandardProducts, Name = "Болт" });

        Assert.Equal(4, bolt.Position);
    }

    [Fact]
    public void Inserted_section_in_group_and_hiding()
    {
        var (spec, context) = Spec();
        var group = SpecificationEditor.AddGroup(spec, "По МЭ");

        SpecificationEditor.ShowSection(spec, context, group.Id, SectionIds.OtherProducts);
        SpecificationEditor.ShowSection(spec, context, group.Id, SectionIds.OtherProducts);

        Assert.Single(spec.ShownSections);
        Assert.Contains(SpecificationViewBuilder.Build(spec, context), r => r.GroupId == group.Id && r.SectionId == SectionIds.OtherProducts);

        Assert.True(SpecificationEditor.HideSection(spec, group.Id, SectionIds.OtherProducts));
        Assert.False(SpecificationEditor.HideSection(spec, group.Id, SectionIds.OtherProducts));
        Assert.DoesNotContain(SpecificationViewBuilder.Build(spec, context), r => r.GroupId == group.Id && r.SectionId == SectionIds.OtherProducts);
    }

    [Fact]
    public void Inserted_sections_of_removed_group_are_forgotten_and_survive_serialization()
    {
        var (spec, context) = Spec();
        var group = SpecificationEditor.AddGroup(spec, "По МЭ");
        SpecificationEditor.ShowSection(spec, context, group.Id, SectionIds.OtherProducts);
        SpecificationEditor.ShowSection(spec, context, "", SectionIds.Kits);

        var copy = SpecificationSerializer.Deserialize(SpecificationSerializer.Serialize(spec));
        Assert.Equal(2, copy.ShownSections.Count);

        SpecificationEditor.RemoveGroup(spec, context, group.Id);
        SpecificationEditor.NormalizeGroups(spec);
        Assert.Equal(SectionIds.Kits, Assert.Single(spec.ShownSections).SectionId);

        Assert.Throws<ArgumentException>(() => SpecificationEditor.ShowSection(spec, context, "нет группы", SectionIds.Kits));
        Assert.Throws<ArgumentException>(() => SpecificationEditor.ShowSection(spec, context, "", " "));
    }

    [Theory]
    [InlineData("АБВГ.301234.001", true)]
    [InlineData("АБВГ.301234.001СБ", true)]
    [InlineData("абвг.301234.001сб", true)]
    [InlineData("АБВГ.301234.001CБ", true)]
    [InlineData("АБВГ.301234.001 СБ Привод", true)]
    [InlineData("АБВГ.301234.001_Привод", true)]
    [InlineData("АБВГ.301234.0010", false)]
    [InlineData("АБВГ.301234.001-01", false)]
    [InlineData("АБВГ.301234.00", false)]
    public void Drawing_file_name_matches_assembly_designation(string fileName, bool expected)
    {
        Assert.Equal(expected, Designations.IsFileOfDocument(fileName, "АБВГ.301234.001"));
        Assert.False(Designations.IsFileOfDocument(fileName, ""));
    }

    [Fact]
    public void Check_warns_when_documentation_is_missing()
    {
        var (spec, context) = Spec();
        var components = new[] { new ComponentData { Key = "a.ipt", Kind = ComponentKind.Part, SectionId = SectionIds.Parts } };

        Assert.Contains(SpecificationChecker.Check(spec, context, new CheckInput { Components = components }),
            i => i.Message.Contains("Нет раздела «Документация»"));

        SpecificationEditor.AddManualItem(spec, context, new ComponentSnapshot { SectionId = SectionIds.Documentation, Designation = "А.000СБ", Name = "Сборочный чертеж" });
        Assert.DoesNotContain(SpecificationChecker.Check(spec, context, new CheckInput { Components = components }),
            i => i.Message.Contains("Документация"));
    }
}
