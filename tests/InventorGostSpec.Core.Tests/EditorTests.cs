using InventorGostSpec.Core.Editing;
using InventorGostSpec.Core.Model;
using InventorGostSpec.Core.Sections;
using InventorGostSpec.Core.Storage;
using InventorGostSpec.Core.Update;
using InventorGostSpec.Core.View;

namespace InventorGostSpec.Core.Tests;

public class EditorTests
{
    private static (Specification Spec, SpecificationContext Context) Build()
    {
        var spec = new Specification();
        var context = TestData.Context(spec, (SectionIds.Parts, 2, 0));
        SpecificationUpdater.Update(
            spec,
            new[]
            {
                TestData.Part("А.001", "Первая"),
                TestData.Part("А.002", "Вторая"),
                TestData.Part("А.003", "Третья"),
                TestData.Standard("Болт М8х20 ГОСТ 7798-70"),
            },
            context,
            TestData.Now);
        return (spec, context);
    }

    [Fact]
    public void Move_reorders_positions_within_section()
    {
        var (spec, _) = Build();

        Assert.True(SpecificationEditor.MoveRow(spec, TestData.ByName(spec, "Третья").Id, TestData.ByName(spec, "Первая").Id));

        Assert.Equal(new[] { "Третья", "Первая", "Вторая", "Болт М8х20 ГОСТ 7798-70" }, TestData.Names(spec));
        Assert.Equal(new int?[] { 1, 2, 3, 6 }, spec.Rows.Select(r => r.Position));
    }

    [Fact]
    public void Move_down_places_row_after_target()
    {
        var (spec, _) = Build();

        SpecificationEditor.MoveRow(spec, TestData.ByName(spec, "Первая").Id, TestData.ByName(spec, "Вторая").Id);

        Assert.Equal(new[] { "Вторая", "Первая", "Третья" }, TestData.Names(spec).Take(3));
    }

    [Fact]
    public void Move_to_another_section_is_rejected()
    {
        var (spec, _) = Build();

        Assert.False(SpecificationEditor.MoveRow(spec, TestData.ByName(spec, "Первая").Id, TestData.ByName(spec, "Болт М8х20 ГОСТ 7798-70").Id));
        Assert.False(SpecificationEditor.MoveRowBy(spec, TestData.ByName(spec, "Третья").Id, +1));
        Assert.True(SpecificationEditor.MoveRowBy(spec, TestData.ByName(spec, "Третья").Id, -1));
        Assert.Equal(new[] { "Первая", "Третья", "Вторая" }, TestData.Names(spec).Take(3));
    }

    [Fact]
    public void Reset_restores_gost_order_and_keeps_notes_after_their_row()
    {
        var (spec, context) = Build();
        var third = TestData.ByName(spec, "Третья");
        SpecificationEditor.MoveRow(spec, third.Id, TestData.ByName(spec, "Первая").Id);
        SpecificationEditor.AddNote(spec, context, SectionIds.Parts, "Примечание к третьей", third.Id);

        SpecificationEditor.Reset(spec, context);

        Assert.Equal(new[] { "Первая", "Вторая", "Третья", "Примечание к третьей", "Болт М8х20 ГОСТ 7798-70" }, TestData.Names(spec));
        Assert.Equal(new int?[] { 1, 2, 3, null, 6 }, spec.Rows.Select(r => r.Position));
    }

    [Fact]
    public void Sort_groups_have_priority_over_gost_order()
    {
        var (spec, context) = Build();
        Assert.All(spec.Rows, r => Assert.Equal(0, r.SortGroup));
        SpecificationEditor.SetCell(spec, context, TestData.ByName(spec, "Первая"), SpecColumn.SortGroup, "2");
        SpecificationEditor.SetCell(spec, context, TestData.ByName(spec, "Вторая"), SpecColumn.SortGroup, "1");

        SpecificationEditor.Reset(spec, context);

        // Группа 0 (по умолчанию), затем 1, затем 2; разделы не смешиваются.
        Assert.Equal(new[] { "Третья", "Вторая", "Первая", "Болт М8х20 ГОСТ 7798-70" }, TestData.Names(spec));
        Assert.Equal(new int?[] { 1, 2, 3, 6 }, spec.Rows.Select(r => r.Position));
        Assert.Equal("2", TestData.ByName(spec, "Первая").Get(SpecColumn.SortGroup));

        SpecificationEditor.SetCell(spec, context, TestData.ByName(spec, "Первая"), SpecColumn.SortGroup, "");
        Assert.Equal(0, TestData.ByName(spec, "Первая").SortGroup);
    }

    [Fact]
    public void Override_and_clear_restore_component_value()
    {
        var (spec, context) = Build();
        var row = TestData.ByName(spec, "Первая");

        SpecificationEditor.SetCell(spec, context, row, SpecColumn.Format, "А3");
        Assert.True(row.IsOverridden(SpecColumn.Format));
        Assert.Equal("А3", row.Get(SpecColumn.Format));
        Assert.Equal("", row.Source.Format);

        SpecificationEditor.SetCell(spec, context, row, SpecColumn.Format, "");
        Assert.False(row.IsOverridden(SpecColumn.Format));
    }

    [Fact]
    public void Section_override_moves_row_and_assigns_new_position()
    {
        var (spec, context) = Build();
        var row = TestData.ByName(spec, "Вторая");

        SpecificationEditor.SetCell(spec, context, row, SpecColumn.Section, SectionIds.OtherProducts);

        Assert.Equal("Вторая", TestData.Names(spec).Last());
        Assert.Equal(7, row.Position);

        SpecificationEditor.ClearOverride(spec, context, row, SpecColumn.Section);
        Assert.Equal(SectionIds.Parts, row.SectionId);
        Assert.Equal(4, row.Position);
    }

    [Fact]
    public void Refresh_component_moves_row_to_new_section_without_highlight()
    {
        var (spec, context) = Build();
        var row = TestData.ByName(spec, "Вторая");
        var data = TestData.Part("А.002", "Вторая");
        data.SectionId = SectionIds.StandardProducts;

        SpecificationEditor.RefreshComponent(spec, context, row, data);

        Assert.Equal(SectionIds.StandardProducts, row.SectionId);
        Assert.Equal("Вторая", TestData.Names(spec).Last());
        Assert.Equal(RowStatus.None, row.Status);
        Assert.Equal(7, row.Position);
        // ГОСТ: у стандартных изделий графа «Обозначение» пустая.
        Assert.Equal("", row.Get(SpecColumn.Designation));
    }

    [Fact]
    public void Manual_item_gets_reserve_position_and_component_rows_cannot_be_deleted()
    {
        var (spec, context) = Build();

        var manual = SpecificationEditor.AddManualItem(
            spec,
            context,
            new ComponentSnapshot { SectionId = SectionIds.Parts, Designation = "А.004", Name = "Ручная", Quantity = "1" });

        Assert.Equal(4, manual.Position);
        Assert.False(SpecificationEditor.DeleteRow(spec, TestData.ByName(spec, "Первая").Id));
        Assert.True(SpecificationEditor.DeleteRow(spec, manual.Id));
    }

    [Fact]
    public void Custom_section_is_shown_and_ordered()
    {
        var (spec, _) = Build();
        SpecificationEditor.AddCustomSection(spec, new SectionDefinition { Id = "Custom_Tools", Name = "Инструмент", Order = 450 });
        var context = SpecificationContext.Create(null, spec);

        var view = SpecificationViewBuilder.Build(spec, context);

        var headers = view.Where(r => r.Kind == DisplayRowKind.SectionHeader).Select(r => r.SectionName);
        Assert.Equal(new[] { "Детали", "Инструмент", "Стандартные изделия" }, headers);
        Assert.Throws<ArgumentException>(() =>
            SpecificationEditor.AddCustomSection(spec, new SectionDefinition { Id = SectionIds.Parts }));
    }

    [Fact]
    public void View_contains_reserve_rows_when_enabled()
    {
        var (spec, context) = Build();

        var reserves = SpecificationViewBuilder.Build(spec, context).Where(r => r.Kind == DisplayRowKind.Reserve).Select(r => r.ReservedPosition);
        Assert.Equal(new int?[] { 4, 5 }, reserves);

        spec.ShowReserveRows = false;
        Assert.DoesNotContain(SpecificationViewBuilder.Build(spec, context), r => r.Kind == DisplayRowKind.Reserve);
    }
}

public class SerializerTests
{
    [Fact]
    public void Round_trip_keeps_all_data()
    {
        var spec = new Specification();
        var context = TestData.Context(spec);
        SpecificationUpdater.Update(spec, new[] { TestData.Part("А.001", "Корпус") }, context, TestData.Now);
        var row = spec.Rows[0];
        row.Note = "Примечание";
        SpecificationEditor.SetCell(spec, context, row, SpecColumn.Format, "А3");
        spec.ReserveOverrides[SectionIds.Parts] = new ReserveSettings { Count = 3, RoundStartTo = 10 };
        spec.CustomSections.Add(new SectionDefinition { Id = "Custom", Name = "Свой", Order = 900 });

        var json = SpecificationSerializer.Serialize(spec);
        var copy = SpecificationSerializer.Deserialize(json);

        Assert.Contains("Корпус", json); // кириллица без экранирования
        Assert.Equal(SpecificationSerializer.Serialize(copy), json);
        Assert.Equal("А3", copy.Rows[0].Get(SpecColumn.Format));
        Assert.Equal("Примечание", copy.Rows[0].Note);
        Assert.Equal(3, copy.ReserveOverrides[SectionIds.Parts].Count);
        Assert.Equal(TestData.Now, copy.LastUpdatedUtc);
    }

    [Fact]
    public void Newer_schema_is_rejected()
    {
        var json = SpecificationSerializer.Serialize(new Specification { SchemaVersion = Specification.CurrentSchemaVersion + 1 });

        Assert.Throws<NotSupportedException>(() => SpecificationSerializer.Deserialize(json));
    }
}
