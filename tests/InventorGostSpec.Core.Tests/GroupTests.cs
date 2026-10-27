using InventorGostSpec.Core.Editing;
using InventorGostSpec.Core.Model;
using InventorGostSpec.Core.Positions;
using InventorGostSpec.Core.Sections;
using InventorGostSpec.Core.Storage;
using InventorGostSpec.Core.Update;
using InventorGostSpec.Core.View;

namespace InventorGostSpec.Core.Tests;

public class GroupTests
{
    private static List<ComponentData> Components() => new()
    {
        TestData.Assembly("А.100", "Шкаф"),
        TestData.Part("А.001", "Корпус"),
        TestData.Part("А.002", "Крышка"),
        TestData.Part("А.003", "Шина"),
        TestData.Standard("Винт М4х10 ГОСТ 17473-80", 8),
        TestData.Standard("Болт М8х20 ГОСТ 7798-70", 4),
        new() { Key = "virtual:Клемма", Kind = ComponentKind.Virtual, Name = "Клемма WAGO 2002-1201", Quantity = 10 },
    };

    /// <summary>Основная часть + группа МЭ: шина, винты и клеммы устанавливаются по электромонтажному чертежу.</summary>
    private static (Specification Spec, SpecificationContext Context, SpecGroup Group) Build()
    {
        var spec = new Specification();
        var context = TestData.Context(spec, (SectionIds.AssemblyUnits, 1, 0), (SectionIds.Parts, 1, 0));
        SpecificationUpdater.Update(spec, Components(), context, TestData.Now);

        var group = SpecificationEditor.AddGroup(spec, "Изделия, устанавливаемые по электромонтажному чертежу А.000МЭ");
        foreach (var name in new[] { "Шина", "Винт М4х10 ГОСТ 17473-80", "Клемма WAGO 2002-1201" })
        {
            SpecificationEditor.MoveToGroup(spec, context, TestData.ByName(spec, name), group.Id);
        }

        return (spec, context, group);
    }

    [Fact]
    public void Group_rows_follow_main_part_with_their_own_sections()
    {
        var (spec, context, group) = Build();
        PositionAllocator.RenumberAll(spec, context);

        var view = SpecificationViewBuilder.Build(spec, context);

        Assert.Equal(
            new[]
            {
                "Сборочные единицы", "Шкаф", "(резерв 2)",
                "Детали", "Корпус", "Крышка", "(резерв 5)",
                "Стандартные изделия", "Болт М8х20 ГОСТ 7798-70",
                group.Title,
                "Детали", "Шина", "(резерв 8)",
                "Стандартные изделия", "Винт М4х10 ГОСТ 17473-80",
                "Прочие изделия", "Клемма WAGO 2002-1201",
            },
            view.Select(Describe));
        Assert.All(view.SkipWhile(r => r.Kind != DisplayRowKind.GroupHeader), r => Assert.Equal(group.Id, r.GroupId));
    }

    [Fact]
    public void Positions_continue_after_main_part()
    {
        var (spec, context, _) = Build();

        PositionAllocator.RenumberAll(spec, context);

        // Основная часть: СЕ 1, резерв 2; детали 3–4, резерв 5; стандартные 6.
        // Группа (свой резерв не задан — как в основной части): детали 7, резерв 8; стандартные 9; прочие 10.
        Assert.Equal(new int?[] { 1, 3, 4, 6, 7, 9, 10 }, spec.Rows.Select(r => r.Position));
    }

    [Fact]
    public void Group_has_own_reserve()
    {
        var (spec, context, group) = Build();
        group.Reserves[SectionIds.Parts] = new ReserveSettings { Count = 3 };

        PositionAllocator.RenumberAll(spec, context);

        // Группа продолжает нумерацию подряд: шина 7, резерв группы 8–10, винт 11, клемма 12.
        Assert.Equal(new int?[] { 1, 3, 4, 6, 7, 11, 12 }, spec.Rows.Select(r => r.Position));
        Assert.Equal(new[] { 8, 9, 10 }, PositionAllocator.GetReservedNumbers(spec, context, group.Id, SectionIds.Parts));
    }

    [Fact]
    public void Moving_to_group_assigns_position_in_group_and_keeps_other_numbers()
    {
        var (spec, context, group) = Build();

        var shina = TestData.ByName(spec, "Шина");
        Assert.Equal(10, shina.Position);
        Assert.Equal(group.Id, shina.GroupId);
        // Выданные номера основной части не сдвигаются: освободившиеся номера уходят в резерв.
        Assert.Equal(new int?[] { 1, 3, 4, 7 }, spec.Rows.Where(r => r.GroupId == "").Select(r => r.Position));
        Assert.Equal(new[] { 2 }, PositionAllocator.GetReservedNumbers(spec, context, SectionIds.AssemblyUnits));
    }

    [Fact]
    public void Group_is_kept_on_update_and_reset()
    {
        var (spec, context, group) = Build();
        var components = Components();
        components.Single(c => c.Name == "Шина").SectionId = SectionIds.OtherProducts;
        components.Add(TestData.Part("А.004", "Планка"));

        SpecificationUpdater.Update(spec, components, context, TestData.Now);
        SpecificationEditor.Reset(spec, context);

        Assert.Equal(group.Id, TestData.ByName(spec, "Шина").GroupId);
        Assert.Equal("", TestData.ByName(spec, "Планка").GroupId);
        Assert.Equal(
            new[] { "Шкаф", "Корпус", "Крышка", "Планка", "Болт М8х20 ГОСТ 7798-70", "Винт М4х10 ГОСТ 17473-80", "Клемма WAGO 2002-1201", "Шина" },
            TestData.Names(spec));
    }

    [Fact]
    public void Moves_stay_inside_group_block()
    {
        var (spec, _, _) = Build();

        Assert.False(SpecificationEditor.MoveRow(spec, TestData.ByName(spec, "Шина").Id, TestData.ByName(spec, "Корпус").Id));
        Assert.False(SpecificationEditor.MoveRowBy(spec, TestData.ByName(spec, "Шина").Id, -1));
    }

    [Fact]
    public void Removing_group_returns_rows_to_main_part()
    {
        var (spec, context, group) = Build();

        SpecificationEditor.RemoveGroup(spec, context, group.Id);

        Assert.Empty(spec.Groups);
        Assert.All(spec.Rows, r => Assert.Equal("", r.GroupId));
        Assert.Equal(new[] { "Шкаф", "Корпус", "Крышка", "Шина" }, TestData.Names(spec).Take(4));
        Assert.All(spec.Rows, r => Assert.NotNull(r.Position));
    }

    [Fact]
    public void Manual_rows_can_be_added_to_group()
    {
        var (spec, context, group) = Build();
        PositionAllocator.RenumberAll(spec, context);

        var item = SpecificationEditor.AddManualItem(
            spec, context, new ComponentSnapshot { SectionId = SectionIds.Materials, Name = "Провод ПуГВ 1х1,5", Quantity = "5", Unit = "м" }, groupId: group.Id);

        Assert.Equal("Провод ПуГВ 1х1,5", TestData.Names(spec).Last());
        Assert.Equal(group.Id, item.GroupId);
        Assert.Equal(11, item.Position);
    }

    [Fact]
    public void Groups_survive_serialization()
    {
        var (spec, _, group) = Build();
        group.Reserves[SectionIds.Parts] = new ReserveSettings { Count = 3 };

        var copy = SpecificationSerializer.Deserialize(SpecificationSerializer.Serialize(spec));

        Assert.Equal(group.Title, copy.Groups.Single().Title);
        Assert.Equal(3, copy.Groups[0].Reserves[SectionIds.Parts].Count);
        Assert.Equal(group.Id, copy.Rows.Single(r => r.Get(SpecColumn.Name) == "Шина").GroupId);
    }

    private static string Describe(DisplayRow row) => row.Kind switch
    {
        DisplayRowKind.Row => row.Row!.Get(SpecColumn.Name),
        DisplayRowKind.Reserve => $"(резерв {row.ReservedPosition})",
        _ => row.SectionName,
    };
}
