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
