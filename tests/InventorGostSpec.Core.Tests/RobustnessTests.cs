using System.Text.Json;
using InventorGostSpec.Core.Editing;
using InventorGostSpec.Core.Model;
using InventorGostSpec.Core.Positions;
using InventorGostSpec.Core.Sections;
using InventorGostSpec.Core.Storage;
using InventorGostSpec.Core.Update;
using InventorGostSpec.Core.View;

namespace InventorGostSpec.Core.Tests;

/// <summary>Нештатные данные и пограничные случаи редактирования.</summary>
public class EditorEdgeCaseTests
{
    private static (Specification Spec, SpecificationContext Context) Build(params ComponentData[] components)
    {
        var spec = new Specification();
        var context = TestData.Context(spec);
        SpecificationUpdater.Update(spec, components, context, TestData.Now);
        return (spec, context);
    }

    [Fact]
    public void Empty_section_is_ignored_for_manual_row_and_clears_override_of_component()
    {
        var (spec, context) = Build(TestData.Part("А.001", "Корпус"));
        var manual = SpecificationEditor.AddManualItem(spec, context, new ComponentSnapshot { SectionId = SectionIds.Parts, Name = "Шильдик" });
        var component = TestData.ByName(spec, "Корпус");
        SpecificationEditor.SetCell(spec, context, component, SpecColumn.Section, SectionIds.OtherProducts);

        SpecificationEditor.SetCell(spec, context, manual, SpecColumn.Section, " ");
        SpecificationEditor.SetCell(spec, context, component, SpecColumn.Section, "");

        Assert.Equal(SectionIds.Parts, manual.SectionId);
        Assert.Equal(SectionIds.Parts, component.SectionId);
        Assert.False(component.IsOverridden(SpecColumn.Section));
        Assert.NotEmpty(SpecificationViewBuilder.Build(spec, context));
    }

    [Fact]
    public void Position_is_not_set_for_rows_without_positions()
    {
        var spec = new Specification();
        var context = TestData.Context(spec);
        var drawing = SpecificationEditor.AddManualItem(spec, context, new ComponentSnapshot { SectionId = SectionIds.Documentation, Designation = "А.000СБ" });
        var note = SpecificationEditor.AddNote(spec, context, SectionIds.Parts, "Текст");

        SpecificationEditor.SetCell(spec, context, drawing, SpecColumn.Position, "5");
        SpecificationEditor.SetCell(spec, context, note, SpecColumn.Position, "6");

        Assert.Null(drawing.Position);
        Assert.Null(note.Position);
    }

    [Fact]
    public void Note_and_manual_item_require_section()
    {
        var spec = new Specification();
        var context = TestData.Context(spec);

        Assert.Throws<ArgumentException>(() => SpecificationEditor.AddNote(spec, context, " ", "Текст"));
        Assert.Throws<ArgumentException>(() => SpecificationEditor.AddManualItem(spec, context, new ComponentSnapshot()));
        Assert.Empty(spec.Rows);
    }

    [Fact]
    public void Sort_group_accepts_only_non_negative_numbers()
    {
        var (spec, context) = Build(TestData.Part("А.001", "Корпус"));
        var row = spec.Rows.Single();

        SpecificationEditor.SetCell(spec, context, row, SpecColumn.SortGroup, "3");
        Assert.Equal(3, row.SortGroup);
        SpecificationEditor.SetCell(spec, context, row, SpecColumn.SortGroup, "-1");
        Assert.Equal(0, row.SortGroup);
        SpecificationEditor.SetCell(spec, context, row, SpecColumn.SortGroup, "abc");
        Assert.Equal(0, row.SortGroup);
    }

    [Fact]
    public void Move_row_by_stops_at_section_bounds()
    {
        var (spec, _) = Build(TestData.Part("А.001", "Корпус"), TestData.Part("А.002", "Ось"), TestData.Assembly("Б.001", "Узел"));
        var first = TestData.ByName(spec, "Корпус");
        var unit = TestData.ByName(spec, "Узел");

        Assert.False(SpecificationEditor.MoveRowBy(spec, first.Id, -1));
        Assert.False(SpecificationEditor.MoveRowBy(spec, unit.Id, +1));
        Assert.True(SpecificationEditor.MoveRowBy(spec, first.Id, +1));
        Assert.Equal(new[] { "Узел", "Ось", "Корпус" }, TestData.Names(spec));
    }

    [Fact]
    public void Accept_changes_removes_rows_of_missing_components_and_clears_marks()
    {
        var (spec, context) = Build(TestData.Part("А.001", "Корпус"), TestData.Part("А.002", "Ось"));
        SpecificationUpdater.Update(spec, new[] { TestData.Part("А.001", "Корпус сварной") }, context, TestData.Now);

        SpecificationEditor.AcceptChanges(spec);

        var row = Assert.Single(spec.Rows);
        Assert.Equal(RowStatus.None, row.Status);
        Assert.Empty(row.PreviousValues);
    }

    [Fact]
    public void Deleting_component_present_in_assembly_is_refused()
    {
        var (spec, _) = Build(TestData.Part("А.001", "Корпус"));

        Assert.False(SpecificationEditor.DeleteRow(spec, spec.Rows[0].Id));
        Assert.False(SpecificationEditor.DeleteRow(spec, "нет такой строки"));
        Assert.Single(spec.Rows);
    }

    [Fact]
    public void Built_in_section_cannot_be_added_as_custom()
    {
        Assert.Throws<ArgumentException>(() => SpecificationEditor.AddCustomSection(
            new Specification(),
            new SectionDefinition { Id = SectionIds.Parts, Name = "Детали" }));
    }

    [Fact]
    public void Unknown_section_from_iproperties_is_shown_at_the_end()
    {
        var (spec, context) = Build(
            TestData.Part("А.001", "Корпус"),
            new ComponentData { Key = "x.ipt", Kind = ComponentKind.Part, SectionId = "Удалённый раздел", Name = "Кронштейн", Quantity = 1 });

        var headers = SpecificationViewBuilder.Build(spec, context)
            .Where(r => r.Kind == DisplayRowKind.SectionHeader)
            .Select(r => r.SectionName)
            .ToList();

        Assert.Equal(new[] { "Детали", "Удалённый раздел" }, headers);
        Assert.NotNull(TestData.ByName(spec, "Кронштейн").Position);
    }
}

public class UpdaterEdgeCaseTests
{
    [Fact]
    public void Empty_component_key_is_rejected()
    {
        var spec = new Specification();

        Assert.Throws<ArgumentException>(() => SpecificationUpdater.Update(
            spec,
            new[] { new ComponentData { Key = " ", Name = "Без ключа" } },
            TestData.Context(spec),
            TestData.Now));
    }

    [Fact]
    public void Keys_differing_by_case_are_one_component()
    {
        var spec = new Specification();
        var first = TestData.Part("А.001", "Корпус");
        var second = TestData.Part("А.001", "Корпус");
        second.Key = first.Key.ToUpperInvariant();

        SpecificationUpdater.Update(spec, new[] { first, second }, TestData.Context(spec), TestData.Now);

        Assert.Equal("2", Assert.Single(spec.Rows).Get(SpecColumn.Quantity));
    }

    [Fact]
    public void Repeated_update_without_changes_reports_nothing()
    {
        var spec = new Specification();
        var context = TestData.Context(spec);
        var components = new[] { TestData.Part("А.001", "Корпус"), TestData.Standard("Болт М8х20 ГОСТ 7798-70", 4) };
        SpecificationUpdater.Update(spec, components, context, TestData.Now);

        var result = SpecificationUpdater.Update(spec, components, context, TestData.Now);

        Assert.False(result.HasChanges);
        Assert.False(result.IsInitialBuild);
        Assert.All(spec.Rows, r => Assert.Equal(RowStatus.None, r.Status));
    }

    [Fact]
    public void Removed_component_keeps_its_number_occupied_until_accepted()
    {
        var spec = new Specification();
        var context = TestData.Context(spec);
        SpecificationUpdater.Update(spec, new[] { TestData.Part("А.001", "Корпус"), TestData.Part("А.002", "Ось") }, context, TestData.Now);

        SpecificationUpdater.Update(spec, new[] { TestData.Part("А.002", "Ось"), TestData.Part("А.003", "Вал") }, context, TestData.Now);

        Assert.Equal(1, TestData.ByName(spec, "Корпус").Position);
        Assert.True(TestData.ByName(spec, "Корпус").IsRemoved);
        Assert.Equal(3, TestData.ByName(spec, "Вал").Position);
    }

    [Fact]
    public void Zero_quantity_is_shown_empty()
    {
        Assert.Equal("", ComponentSnapshot.FormatQuantity(0));
        Assert.Equal("2,5", ComponentSnapshot.FormatQuantity(2.5m));
        Assert.Equal("0,125", ComponentSnapshot.FormatQuantity(0.125m));
    }
}

public class PositionEdgeCaseTests
{
    [Fact]
    public void Negative_reserve_is_treated_as_zero()
    {
        var spec = new Specification
        {
            Rows = { TestData.Row(SectionIds.AssemblyUnits, "А.1"), TestData.Row(SectionIds.Parts, "А.2") },
        };
        var context = TestData.Context(spec, (SectionIds.AssemblyUnits, -5, 0));

        PositionAllocator.RenumberAll(spec, context);

        Assert.Equal(new int?[] { 1, 2 }, spec.Rows.Select(r => r.Position));
    }

    [Fact]
    public void Missing_number_fills_gap_left_by_deleted_row()
    {
        var spec = new Specification
        {
            Rows = { TestData.Row(SectionIds.Parts, "А.1"), TestData.Row(SectionIds.Parts, "А.2"), TestData.Row(SectionIds.Parts, "А.3") },
        };
        var context = TestData.Context(spec);
        PositionAllocator.RenumberAll(spec, context);
        spec.Rows[1].Position = null;

        var result = PositionAllocator.AssignMissing(spec, context);

        Assert.True(result.IsComplete);
        Assert.Equal(new int?[] { 1, 2, 3 }, spec.Rows.Select(r => r.Position));
    }

    [Fact]
    public void Reserved_numbers_of_last_section_follow_reserve_setting()
    {
        var spec = new Specification { Rows = { TestData.Row(SectionIds.Parts, "А.1") } };
        var context = TestData.Context(spec, (SectionIds.Parts, 3, 0));
        PositionAllocator.RenumberAll(spec, context);

        Assert.Equal(new[] { 2, 3, 4 }, PositionAllocator.GetReservedNumbers(spec, context, SectionIds.Parts));
        Assert.Empty(PositionAllocator.GetReservedNumbers(spec, context, SectionIds.Materials));
    }
}

public class SerializerEdgeCaseTests
{
    [Fact]
    public void Null_collections_and_values_are_repaired()
    {
        const string json = """
            {
              "rows": [
                { "id": "a", "kind": "ManualItem", "source": null, "overrides": null, "previousValues": null, "zone": null, "note": null, "groupId": null },
                null,
                { "id": "a", "kind": "Note", "source": { "sectionId": "Parts", "name": "Текст" } }
              ],
              "groups": null,
              "customSections": [ null, { "id": "", "name": "Без id" } ],
              "reserveOverrides": null
            }
            """;

        var spec = SpecificationSerializer.Deserialize(json);

        Assert.Equal(2, spec.Rows.Count);
        Assert.Equal(SectionIds.OtherProducts, spec.Rows[0].SectionId);
        Assert.Equal("", spec.Rows[0].Note);
        Assert.NotEqual(spec.Rows[0].Id, spec.Rows[1].Id);
        Assert.Empty(spec.Groups);
        Assert.Empty(spec.CustomSections);
        Assert.NotNull(spec.ReserveOverrides);

        var context = SpecificationContext.Create(null, spec);
        Assert.NotEmpty(SpecificationViewBuilder.Build(spec, context));
    }

    [Fact]
    public void Corrupted_specification_falls_back_to_backup()
    {
        var backup = new Specification();
        backup.Rows.Add(TestData.Row(SectionIds.Parts, "А.1", "Корпус"));
        var backupJson = SpecificationSerializer.Serialize(backup);

        var spec = SpecificationSerializer.Load("{ \"rows\": [ {", backupJson, out var fromBackup);

        Assert.True(fromBackup);
        Assert.Equal("Корпус", Assert.Single(spec!.Rows).Get(SpecColumn.Name));
    }

    [Fact]
    public void Load_without_data_or_backup()
    {
        Assert.Null(SpecificationSerializer.Load(null, null, out var fromBackup));
        Assert.False(fromBackup);
        Assert.ThrowsAny<JsonException>(() => SpecificationSerializer.Load("не json", null, out _));
    }

    [Fact]
    public void Newer_schema_is_not_replaced_by_backup()
    {
        var newer = SpecificationSerializer.Serialize(new Specification { SchemaVersion = Specification.CurrentSchemaVersion + 1 });

        Assert.Throws<NotSupportedException>(() =>
            SpecificationSerializer.Load(newer, SpecificationSerializer.Serialize(new Specification()), out _));
    }

    [Fact]
    public void Settings_with_nulls_get_defaults()
    {
        var settings = SpecificationSerializer.DeserializeSettings("""{ "customSections": null, "defaultReserves": null, "notePropertyName": "" }""");

        Assert.Empty(settings.CustomSections);
        Assert.Empty(settings.DefaultReserves);
        Assert.Equal("Примечание", settings.NotePropertyName);
    }
}
