using InventorGostSpec.Core.Model;
using InventorGostSpec.Core.Sections;

namespace InventorGostSpec.Core.Tests;

public class SnapshotTests
{
    [Fact]
    public void Standard_product_has_no_designation_and_name_falls_back_to_it()
    {
        var component = TestData.Standard("");
        component.Designation = "Болт М12х60 ГОСТ 7798-70";

        var snapshot = ComponentSnapshot.FromComponent(component);

        Assert.Equal(SectionIds.StandardProducts, snapshot.SectionId);
        Assert.Equal("", snapshot.Designation);
        Assert.Equal("Болт М12х60 ГОСТ 7798-70", snapshot.Name);
        Assert.Equal("Болт", snapshot.SortName);
        Assert.Equal("ГОСТ 7798-70", snapshot.StandardNumber);
        Assert.Equal("М12х60", snapshot.Parameters);
    }

    [Fact]
    public void Part_keeps_designation()
    {
        var snapshot = ComponentSnapshot.FromComponent(TestData.Part("АБВГ.715431.001", "Корпус", 2.5m));

        Assert.Equal(SectionIds.Parts, snapshot.SectionId);
        Assert.Equal("АБВГ.715431.001", snapshot.Designation);
        Assert.Equal("2,5", snapshot.Quantity);
    }

    [Fact]
    public void Assembly_unit_gets_specification_format_a4()
    {
        Assert.Equal("А4", ComponentSnapshot.FromComponent(TestData.Assembly("А.100СБ", "Привод")).Format);

        var withFormat = TestData.Assembly("А.101", "Рама");
        withFormat.Format = "А3";
        Assert.Equal("А3", ComponentSnapshot.FromComponent(withFormat).Format);

        // У деталей формат не подставляется.
        Assert.Equal("", ComponentSnapshot.FromComponent(TestData.Part("А.001", "Ось")).Format);
    }

    [Theory]
    [InlineData(ComponentKind.Document, false, SectionIds.Documentation)]
    [InlineData(ComponentKind.Part, true, SectionIds.StandardProducts)]
    [InlineData(ComponentKind.Part, false, SectionIds.Parts)]
    [InlineData(ComponentKind.Assembly, false, SectionIds.AssemblyUnits)]
    [InlineData(ComponentKind.Virtual, false, SectionIds.OtherProducts)]
    public void Default_sections(ComponentKind kind, bool contentCenter, string expected) =>
        Assert.Equal(expected, SectionResolver.GetDefault(kind, contentCenter));
}

public class SheetFormatsTests
{
    [Theory]
    [InlineData(210, 297, "А4")]
    [InlineData(420, 297, "А3")]
    [InlineData(1189, 841, "А0")]
    [InlineData(297, 630, "А4х3")]
    [InlineData(841, 297, "А4х4")]
    [InlineData(420, 891, "А3х3")]
    [InlineData(297, 420.5, "А3")]
    [InlineData(100, 100, "")]
    public void Recognizes_formats(double width, double height, string expected) =>
        Assert.Equal(expected, SheetFormats.FromSize(width, height));
}

public class DesignationsTests
{
    [Theory]
    [InlineData("АБВГ.301234.001СБ", "АБВГ.301234.001")]
    [InlineData("АБВГ.301234.001 СБ", "АБВГ.301234.001")]
    [InlineData("АБВГ.301234.001CБ", "АБВГ.301234.001")] // латинская C
    [InlineData("АБВГ.301234.001", "АБВГ.301234.001")]
    [InlineData("", "")]
    public void Strips_assembly_drawing_code(string designation, string expected) =>
        Assert.Equal(expected, Designations.WithoutAssemblyDrawingCode(designation));

    [Theory]
    [InlineData("АБВГ.301234.001", "АБВГ.301234.001СБ")]
    [InlineData("АБВГ.301234.001СБ", "АБВГ.301234.001СБ")]
    [InlineData("", "")]
    public void Assembly_drawing_has_single_code(string assembly, string expected) =>
        Assert.Equal(expected, Designations.AssemblyDrawing(assembly));

    [Fact]
    public void Assembly_row_has_no_drawing_code_but_part_keeps_designation()
    {
        Assert.Equal("АБВГ.301234.005", ComponentSnapshot.FromComponent(TestData.Assembly("АБВГ.301234.005СБ", "Редуктор")).Designation);
        // У деталей обозначение не трогаем.
        Assert.Equal("АБВГ.715431.001СБ", ComponentSnapshot.FromComponent(TestData.Part("АБВГ.715431.001СБ", "Деталь")).Designation);
    }
}
