using InventorGostSpec.Core.Editing;
using InventorGostSpec.Core.Model;
using InventorGostSpec.Core.Sections;
using InventorGostSpec.Core.Update;

namespace InventorGostSpec.Core.Tests;

public class UpdaterTests
{
    private static List<ComponentData> Assembly() => new()
    {
        TestData.Standard("Болт М12х60 ГОСТ 7798-70", 4),
        TestData.Part("АБВГ.715431.002", "Ось"),
        TestData.Assembly("АБВГ.301234.010", "Привод"),
        TestData.Part("АБВГ.715431.001", "Корпус"),
        TestData.Standard("Болт М8х20 ГОСТ 7798-70", 2),
        TestData.Assembly("АБВГ.301234.005", "Редуктор"),
    };

    private static (Specification Spec, SpecificationContext Context) Build(List<ComponentData> components)
    {
        var spec = new Specification();
        var context = TestData.Context(spec, (SectionIds.AssemblyUnits, 2, 0), (SectionIds.Parts, 2, 0));
        SpecificationUpdater.Update(spec, components, context, TestData.Now);
        return (spec, context);
    }

    [Fact]
    public void Initial_build_sorts_and_numbers_without_highlight()
    {
        var (spec, _) = Build(Assembly());

        Assert.Equal(
            new[] { "Редуктор", "Привод", "Корпус", "Ось", "Болт М8х20 ГОСТ 7798-70", "Болт М12х60 ГОСТ 7798-70" },
            TestData.Names(spec));
        Assert.Equal(new int?[] { 1, 2, 5, 6, 9, 10 }, spec.Rows.Select(r => r.Position));
        Assert.All(spec.Rows, r => Assert.Equal(RowStatus.None, r.Status));
        Assert.Equal(TestData.Now, spec.LastUpdatedUtc);
    }

    [Fact]
    public void Quantity_of_repeated_component_is_summed()
    {
        var components = Assembly();
        components.Add(TestData.Standard("Болт М8х20 ГОСТ 7798-70", 3));

        var (spec, _) = Build(components);

        Assert.Equal("5", TestData.ByName(spec, "Болт М8х20 ГОСТ 7798-70").Get(SpecColumn.Quantity));
    }

    [Fact]
    public void Update_keeps_manual_order_and_appends_new_rows_with_reserve_numbers()
    {
        var components = Assembly();
        var (spec, context) = Build(components);
        SpecificationEditor.MoveRow(spec, TestData.ByName(spec, "Ось").Id, TestData.ByName(spec, "Корпус").Id);

        components.Add(TestData.Part("АБВГ.715431.000", "Втулка"));
        components.Add(TestData.Assembly("АБВГ.301234.001", "Муфта"));
        var result = SpecificationUpdater.Update(spec, components, context, TestData.Now);

        Assert.Equal(
            new[] { "Редуктор", "Привод", "Муфта", "Ось", "Корпус", "Втулка", "Болт М8х20 ГОСТ 7798-70", "Болт М12х60 ГОСТ 7798-70" },
            TestData.Names(spec));
        Assert.Equal(new int?[] { 1, 2, 3, 5, 6, 7, 9, 10 }, spec.Rows.Select(r => r.Position));
        Assert.Equal(2, result.Added.Count);
        Assert.Equal(RowStatus.New, TestData.ByName(spec, "Втулка").Status);
        Assert.True(result.Positions.IsComplete);
    }

    [Fact]
    public void Removed_component_is_marked_not_deleted()
    {
        var components = Assembly();
        var (spec, context) = Build(components);
        components.RemoveAll(c => c.Name == "Ось");

        var result = SpecificationUpdater.Update(spec, components, context, TestData.Now);

        var row = TestData.ByName(spec, "Ось");
        Assert.Equal(RowStatus.Removed, row.Status);
        Assert.Equal(6, row.Position);
        Assert.Single(result.Removed);

        SpecificationEditor.AcceptChanges(spec);
        Assert.DoesNotContain("Ось", TestData.Names(spec));
    }

    [Fact]
    public void Changed_component_is_highlighted_with_previous_value()
    {
        var components = Assembly();
        var (spec, context) = Build(components);
        components.Single(c => c.Name == "Корпус").Designation = "АБВГ.715431.003";
        components.Single(c => c.Name == "Ось").Quantity = 2;

        var result = SpecificationUpdater.Update(spec, components, context, TestData.Now);

        var body = TestData.ByName(spec, "Корпус");
        Assert.Equal(RowStatus.Changed, body.Status);
        Assert.Equal("АБВГ.715431.001", body.PreviousValues[SpecColumn.Designation]);
        Assert.Equal("АБВГ.715431.003", body.Get(SpecColumn.Designation));
        Assert.Equal("2", TestData.ByName(spec, "Ось").Get(SpecColumn.Quantity));
        Assert.Equal(2, result.Changed.Count);

        // Подсветка относится только к последнему обновлению.
        SpecificationUpdater.Update(spec, components, context, TestData.Now);
        Assert.Equal(RowStatus.None, body.Status);
    }

    [Fact]
    public void Component_moved_to_another_section_goes_to_end_of_it()
    {
        var components = Assembly();
        var (spec, context) = Build(components);
        components.Single(c => c.Name == "Ось").SectionId = SectionIds.OtherProducts;

        SpecificationUpdater.Update(spec, components, context, TestData.Now);

        Assert.Equal("Ось", TestData.Names(spec).Last());
        Assert.Equal(11, TestData.ByName(spec, "Ось").Position);
    }

    [Fact]
    public void Notes_overrides_and_manual_rows_survive_update()
    {
        var components = Assembly();
        var (spec, context) = Build(components);
        var body = TestData.ByName(spec, "Корпус");
        body.Note = "Покупной";
        SpecificationEditor.SetCell(spec, context, body, SpecColumn.Name, "Корпус сварной");
        var note = SpecificationEditor.AddNote(spec, context, SectionIds.Parts, "Детали по ТУ", body.Id);

        SpecificationUpdater.Update(spec, components, context, TestData.Now);

        Assert.Equal("Покупной", body.Note);
        Assert.Equal("Корпус сварной", body.Get(SpecColumn.Name));
        Assert.Equal(spec.Rows.IndexOf(body) + 1, spec.Rows.IndexOf(note));
    }

    [Fact]
    public void Removed_component_returned_to_assembly_is_restored()
    {
        var components = Assembly();
        var (spec, context) = Build(components);
        var axis = components.Single(c => c.Name == "Ось");
        components.Remove(axis);
        SpecificationUpdater.Update(spec, components, context, TestData.Now);

        components.Add(axis);
        SpecificationUpdater.Update(spec, components, context, TestData.Now);

        var row = TestData.ByName(spec, "Ось");
        Assert.Equal(RowStatus.New, row.Status);
        Assert.Equal(6, row.Position);
        Assert.Single(spec.Rows, r => r.Get(SpecColumn.Name) == "Ось");
    }
}
