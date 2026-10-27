using InventorGostSpec.Core.Checking;
using InventorGostSpec.Core.Editing;
using InventorGostSpec.Core.Model;
using InventorGostSpec.Core.Sections;
using InventorGostSpec.Core.Update;

namespace InventorGostSpec.Core.Tests;

public class CheckerTests
{
    private static ComponentData Part(string designation, string name, string format = "А4", string section = SectionIds.Parts) => new()
    {
        Key = designation + ".ipt",
        Kind = ComponentKind.Part,
        Designation = designation,
        Name = name,
        Format = format,
        SectionId = section,
        Quantity = 1,
    };

    private static readonly ComponentData Drawing = new()
    {
        Key = "drawing:Привод.idw", Kind = ComponentKind.Document, Designation = "А.000СБ", Name = "Сборочный чертеж", Format = "А1",
    };

    private static (Specification Spec, SpecificationContext Context) Build(params ComponentData[] components)
    {
        var spec = new Specification();
        var context = TestData.Context(spec);
        SpecificationUpdater.Update(spec, components, context, TestData.Now);
        return (spec, context);
    }

    private static List<SpecIssue> Issues(Specification spec, SpecificationContext context, CheckInput? input = null) =>
        SpecificationChecker.Check(spec, context, input);

    [Fact]
    public void Correct_specification_has_no_issues()
    {
        var (spec, context) = Build(Drawing, Part("А.001", "Корпус"), Part("А.002", "Ось"));

        Assert.Empty(Issues(spec, context, new CheckInput
        {
            Components = new[] { Drawing, Part("А.001", "Корпус"), Part("А.002", "Ось") },
            BomItemNumbers = new Dictionary<string, string> { ["А.001.ipt"] = "1", ["а.002.IPT"] = "2" },
            TitleBlock = new TitleBlock { Designation = "А.000", Name = "Узел", Developer = "Иванов", Checker = "Петров" },
        }));
    }

    [Fact]
    public void Assembly_unit_without_drawing_is_only_noted()
    {
        var unit = new ComponentData { Key = "Узел.iam", Kind = ComponentKind.Assembly, Designation = "А.100", Name = "Узел", Quantity = 1, HasDrawing = false };
        var (spec, context) = Build(Drawing, unit);

        var issue = Assert.Single(Issues(spec, context), i => i.Message.Contains("Узел"));
        Assert.Equal(IssueSeverity.Info, issue.Severity);
        Assert.Contains("сборочный чертёж не найден", issue.Message);
    }

    [Fact]
    public void Missing_and_duplicate_positions_are_errors()
    {
        var (spec, context) = Build(Part("А.001", "Корпус"), Part("А.002", "Ось"), Part("А.003", "Вал"));
        TestData.ByName(spec, "Ось").Position = null;
        TestData.ByName(spec, "Вал").Position = 1;

        var issues = Issues(spec, context);

        Assert.Contains(issues, i => i.Severity == IssueSeverity.Error && i.Message.Contains("Ось") && i.Message.Contains("нет номера позиции"));
        Assert.Contains(issues, i => i.Severity == IssueSeverity.Error && i.Message.Contains("позиция 1 повторяется") && i.Message.Contains("Корпус"));
    }

    [Fact]
    public void Positions_out_of_order_are_reported()
    {
        var (spec, context) = Build(Part("А.001", "Корпус"), Part("А.002", "Ось"));
        TestData.ByName(spec, "Корпус").Position = 5;

        var issue = Assert.Single(Issues(spec, context));

        Assert.Equal(IssueSeverity.Warning, issue.Severity);
        Assert.Contains("не по порядку", issue.Message);
        Assert.Equal(TestData.ByName(spec, "Ось").Id, issue.RowId);
    }

    [Fact]
    public void Required_cells_are_checked_by_section()
    {
        var (spec, context) = Build(
            Part("", "Корпус", format: ""),
            new ComponentData { Key = "bolt", Kind = ComponentKind.Part, IsContentCenter = true, Name = "Болт М8 ГОСТ 7798-70", Quantity = 2 });
        var manual = SpecificationEditor.AddManualItem(spec, context, new ComponentSnapshot { SectionId = SectionIds.OtherProducts });

        var issues = Issues(spec, context);

        Assert.Contains(issues, i => i.Message.Contains("Корпус: нет обозначения"));
        Assert.Contains(issues, i => i.Message.Contains("Корпус: не указан формат") && i.Message.Contains("БЧ"));
        Assert.Contains(issues, i => i.RowId == manual.Id && i.Severity == IssueSeverity.Error && i.Message.Contains("нет наименования"));
        Assert.Contains(issues, i => i.RowId == manual.Id && i.Message.Contains("не указано количество"));

        // У стандартных изделий обозначение и формат не нужны.
        Assert.DoesNotContain(issues, i => i.Message.Contains("Болт"));
    }

    [Fact]
    public void Duplicate_designations_are_found_ignoring_lookalike_letters()
    {
        var (spec, context) = Build(Part("АБВГ.715431.001", "Корпус"), Part("AБBГ.715431.001 ", "Крышка"));

        var issue = Assert.Single(Issues(spec, context));

        Assert.Contains("обозначение повторяется", issue.Message);
    }

    [Fact]
    public void Removed_and_unaccepted_rows_are_reported()
    {
        var (spec, context) = Build(Part("А.001", "Корпус"), Part("А.002", "Ось"));
        SpecificationUpdater.Update(spec, new[] { Part("А.001", "Корпус сварной") }, context, TestData.Now);

        var issues = Issues(spec, context);

        Assert.Contains(issues, i => i.Severity == IssueSeverity.Warning && i.Message.Contains("Ось: компонента нет в сборке"));
        Assert.Contains(issues, i => i.Severity == IssueSeverity.Info && i.Message.Contains("Не приняты изменения") && i.Message.Contains(": 1."));
    }

    [Fact]
    public void Positions_differing_from_bom_are_reported()
    {
        var (spec, context) = Build(Part("А.001", "Корпус"), Part("А.002", "Ось"));

        var issues = Issues(spec, context, new CheckInput
        {
            BomItemNumbers = new Dictionary<string, string> { ["А.001.ipt"] = "7", ["А.002.ipt"] = "" },
        });

        Assert.Equal(2, issues.Count);
        Assert.Contains("в BOM Inventor позиция 7", issues[0].Message);
        Assert.Contains("позиция пусто", issues[1].Message);
    }

    [Fact]
    public void Components_without_section_property_are_noted()
    {
        var auto = new ComponentData { Key = "a.ipt", Kind = ComponentKind.Part, Designation = "А.001", Name = "Корпус", Format = "А4", Quantity = 1 };
        var (spec, context) = Build(Drawing, auto);

        var issue = Assert.Single(Issues(spec, context, new CheckInput { Components = new[] { Drawing, auto } }));

        Assert.Equal(IssueSeverity.Info, issue.Severity);
        Assert.Contains("определён автоматически — «Детали»", issue.Message);
    }

    [Fact]
    public void Empty_title_block_and_group_title_are_reported()
    {
        var (spec, context) = Build(Part("А.001", "Корпус"));
        SpecificationEditor.AddGroup(spec, " ");

        var issues = Issues(spec, context, new CheckInput { TitleBlock = new TitleBlock { Designation = "А.000" } });

        Assert.Contains(issues, i => i.Message.Contains("без заголовка"));
        Assert.Contains(issues, i => i.Severity == IssueSeverity.Warning && i.Message.Contains("наименование"));
        Assert.Contains(issues, i => i.Severity == IssueSeverity.Info && i.Message.Contains("Разраб."));
        Assert.DoesNotContain(issues, i => i.Message.Contains("обозначение (Part Number"));
    }

    [Fact]
    public void Errors_come_first_then_table_order()
    {
        var (spec, context) = Build(Part("А.001", "Корпус", format: ""), Part("А.002", "Ось"));
        TestData.ByName(spec, "Ось").Position = null;

        var issues = Issues(spec, context);

        Assert.Equal(new[] { IssueSeverity.Error, IssueSeverity.Warning }, issues.Select(i => i.Severity));
    }

    [Fact]
    public void Notes_and_documentation_rows_need_no_position_or_quantity()
    {
        var (spec, context) = Build(
            new ComponentData { Key = "drawing:сб", Kind = ComponentKind.Document, Designation = "А.000СБ", Name = "Сборочный чертеж", Format = "А1" },
            Part("А.001", "Корпус"));
        SpecificationEditor.AddNote(spec, context, SectionIds.Parts, "");

        Assert.Empty(Issues(spec, context));
    }
}
