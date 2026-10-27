using InventorGostSpec.Core.Model;
using InventorGostSpec.Core.Positions;
using InventorGostSpec.Core.Sections;

namespace InventorGostSpec.Core.Tests;

public class PositionTests
{
    private static Specification Spec(params SpecRow[] rows) => new() { Rows = rows.ToList() };

    private static int?[] Positions(Specification spec) => spec.Rows.Select(r => r.Position).ToArray();

    [Fact]
    public void Renumber_skips_reserve_after_section()
    {
        // Пример из требований: СЕ 1, 2, 3; резерв 4, 5; детали 6, 7, 8.
        var spec = Spec(
            TestData.Row(SectionIds.Documentation, "А.001СБ"),
            TestData.Row(SectionIds.AssemblyUnits, "А.1"),
            TestData.Row(SectionIds.AssemblyUnits, "А.2"),
            TestData.Row(SectionIds.AssemblyUnits, "А.3"),
            TestData.Row(SectionIds.Parts, "А.4"),
            TestData.Row(SectionIds.Parts, "А.5"),
            TestData.Row(SectionIds.Parts, "А.6"));
        var context = TestData.Context(spec, (SectionIds.AssemblyUnits, 2, 0));

        PositionAllocator.RenumberAll(spec, context);

        Assert.Equal(new int?[] { null, 1, 2, 3, 6, 7, 8 }, Positions(spec));
    }

    [Fact]
    public void Renumber_rounds_section_start()
    {
        var spec = Spec(
            TestData.Row(SectionIds.AssemblyUnits, "А.1"),
            TestData.Row(SectionIds.Parts, "А.2"),
            TestData.Row(SectionIds.StandardProducts, name: "Болт"));
        var context = TestData.Context(spec, (SectionIds.AssemblyUnits, 0, 10), (SectionIds.Parts, 0, 10), (SectionIds.StandardProducts, 0, 10));

        PositionAllocator.RenumberAll(spec, context);

        Assert.Equal(new int?[] { 1, 10, 20 }, Positions(spec));
    }

    [Fact]
    public void Notes_and_removed_rows_get_no_position()
    {
        var note = new SpecRow { Kind = RowKind.Note, Source = { SectionId = SectionIds.Parts, Name = "Текст" } };
        var removed = TestData.Row(SectionIds.Parts, "А.1");
        removed.Status = RowStatus.Removed;
        var spec = Spec(removed, note, TestData.Row(SectionIds.Parts, "А.2"));

        PositionAllocator.RenumberAll(spec, TestData.Context(spec));

        Assert.Equal(new int?[] { null, null, 1 }, Positions(spec));
    }

    [Fact]
    public void New_row_takes_number_from_reserve_of_its_section()
    {
        var newAssembly = TestData.Row(SectionIds.AssemblyUnits, "А.4");
        var newPart = TestData.Row(SectionIds.Parts, "А.9");
        var spec = Spec(
            TestData.Row(SectionIds.AssemblyUnits, "А.1"),
            TestData.Row(SectionIds.AssemblyUnits, "А.2"),
            TestData.Row(SectionIds.AssemblyUnits, "А.3"),
            newAssembly,
            TestData.Row(SectionIds.Parts, "А.6"),
            TestData.Row(SectionIds.Parts, "А.7"),
            newPart);
        var context = TestData.Context(spec, (SectionIds.AssemblyUnits, 2, 0));
        int?[] existing = { 1, 2, 3, null, 6, 7, null };
        for (var i = 0; i < existing.Length; i++)
        {
            spec.Rows[i].Position = existing[i];
        }

        var result = PositionAllocator.AssignMissing(spec, context);

        Assert.True(result.IsComplete);
        Assert.Equal(new int?[] { 1, 2, 3, 4, 6, 7, 8 }, Positions(spec));
    }

    [Fact]
    public void First_row_of_new_section_does_not_take_previous_reserve()
    {
        var newRow = TestData.Row(SectionIds.StandardProducts, name: "Болт");
        var spec = Spec(
            TestData.Row(SectionIds.Parts, "А.1"),
            TestData.Row(SectionIds.Parts, "А.2"),
            newRow);
        spec.Rows[0].Position = 1;
        spec.Rows[1].Position = 2;

        PositionAllocator.AssignMissing(spec, TestData.Context(spec, (SectionIds.Parts, 2, 0)));

        // Номера 3–4 — резерв деталей.
        Assert.Equal(5, newRow.Position);
    }

    [Fact]
    public void Exhausted_reserve_is_reported_and_numbers_are_kept()
    {
        var newRow = TestData.Row(SectionIds.AssemblyUnits, "А.3");
        var spec = Spec(
            TestData.Row(SectionIds.AssemblyUnits, "А.1"),
            newRow,
            TestData.Row(SectionIds.Parts, "А.2"));
        spec.Rows[0].Position = 1;
        spec.Rows[2].Position = 2;

        var result = PositionAllocator.AssignMissing(spec, TestData.Context(spec));

        Assert.False(result.IsComplete);
        Assert.Equal(new[] { SectionIds.AssemblyUnits }, result.ExhaustedSectionIds);
        Assert.Equal(new int?[] { 1, null, 2 }, Positions(spec));
    }

    [Fact]
    public void Removed_rows_keep_their_numbers_occupied()
    {
        var removed = TestData.Row(SectionIds.Parts, "А.2");
        removed.Status = RowStatus.Removed;
        var spec = Spec(TestData.Row(SectionIds.Parts, "А.1"), removed, TestData.Row(SectionIds.Parts, "А.3"));
        spec.Rows[0].Position = 1;
        removed.Position = 2;

        PositionAllocator.AssignMissing(spec, TestData.Context(spec));

        Assert.Equal(new int?[] { 1, 2, 3 }, Positions(spec));
    }

    [Fact]
    public void Reserved_numbers_are_listed_for_display()
    {
        var spec = Spec(
            TestData.Row(SectionIds.AssemblyUnits, "А.1"),
            TestData.Row(SectionIds.Parts, "А.2"));
        var context = TestData.Context(spec, (SectionIds.AssemblyUnits, 2, 0), (SectionIds.Parts, 3, 0));
        PositionAllocator.RenumberAll(spec, context);

        Assert.Equal(new[] { 2, 3 }, PositionAllocator.GetReservedNumbers(spec, context, SectionIds.AssemblyUnits));
        Assert.Equal(new[] { 5, 6, 7 }, PositionAllocator.GetReservedNumbers(spec, context, SectionIds.Parts));
    }
}
