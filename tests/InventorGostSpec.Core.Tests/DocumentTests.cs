using InventorGostSpec.Core.Checking;
using InventorGostSpec.Core.Editing;
using InventorGostSpec.Core.Model;
using InventorGostSpec.Core.Sections;
using InventorGostSpec.Core.Sorting;
using InventorGostSpec.Core.Update;

namespace InventorGostSpec.Core.Tests;

/// <summary>Раздел «Документация»: тип документа по коду, без количества.</summary>
public class DocumentTests
{
    [Theory]
    [InlineData("СБ", "Сборочный чертеж")]
    [InlineData("ТУ", "Технические условия")]
    [InlineData("МЭ", "Электромонтажный чертеж")]
    [InlineData("Э3", "Схема электрическая принципиальная")]
    [InlineData("Г5", "Схема гидравлическая подключения")]
    [InlineData("ПЭ3", "Перечень элементов")]
    [InlineData("CБ", "Сборочный чертеж")] // латинская C
    [InlineData("Д1", "")]
    [InlineData("ЖЖ", "")]
    [InlineData("", "")]
    public void Document_name_by_code(string code, string name)
    {
        Assert.Equal(name, DocumentCodes.NameOf(code));
    }

    [Theory]
    [InlineData("АБВГ.301234.001", "СБ", "АБВГ.301234.001СБ")]
    [InlineData("АБВГ.301234.001СБ", "ТУ", "АБВГ.301234.001ТУ")]
    [InlineData("АБВГ.301234.001 Э3", "ПЭ3", "АБВГ.301234.001ПЭ3")]
    [InlineData("", "СБ", "СБ")]
    public void Document_code_is_replaced(string designation, string code, string expected)
    {
        Assert.Equal(expected, DocumentCodes.WithCode(designation, code));
    }

    [Fact]
    public void Drawing_from_assembly_gets_name_by_code_and_no_quantity()
    {
        var spec = new Specification();
        SpecificationUpdater.Update(spec, new[]
        {
            new ComponentData { Key = "drawing:a.idw", Kind = ComponentKind.Document, Designation = "АБВГ.301234.001СБ", Quantity = 1 },
            new ComponentData { Key = "drawing:a.idw", Kind = ComponentKind.Document, Designation = "АБВГ.301234.001СБ", Quantity = 1 },
        }, TestData.Context(spec), TestData.Now);

        var row = Assert.Single(spec.Rows);
        Assert.Equal("Сборочный чертеж", row.Get(SpecColumn.Name));
        Assert.Equal("", row.Get(SpecColumn.Quantity));
    }

    [Fact]
    public void Typed_designation_fills_document_name_but_keeps_typed_name()
    {
        var spec = new Specification();
        var context = TestData.Context(spec);
        var row = SpecificationEditor.AddManualItem(spec, context, new ComponentSnapshot { SectionId = SectionIds.Documentation });

        SpecificationEditor.SetCell(spec, context, row, SpecColumn.Designation, "АБВГ.301234.001СБ");
        Assert.Equal("Сборочный чертеж", row.Get(SpecColumn.Name));

        // Код поменяли — наименование, подставленное по старому коду, меняется тоже.
        SpecificationEditor.SetCell(spec, context, row, SpecColumn.Designation, "АБВГ.301234.001ТУ");
        Assert.Equal("Технические условия", row.Get(SpecColumn.Name));

        // Вписанное вручную наименование не затирается.
        SpecificationEditor.SetCell(spec, context, row, SpecColumn.Name, "Технические условия на поставку");
        SpecificationEditor.SetCell(spec, context, row, SpecColumn.Designation, "АБВГ.301234.001ПЗ");
        Assert.Equal("Технические условия на поставку", row.Get(SpecColumn.Name));

        // Неизвестный код — наименование вписывается вручную.
        var other = SpecificationEditor.AddManualItem(spec, context, new ComponentSnapshot { SectionId = SectionIds.Documentation });
        SpecificationEditor.SetCell(spec, context, other, SpecColumn.Designation, "АБВГ.301234.001Д1");
        Assert.Equal("", other.Get(SpecColumn.Name));
    }

    [Fact]
    public void Document_type_sets_code_and_name_also_for_component_row()
    {
        var spec = new Specification();
        var context = TestData.Context(spec);
        SpecificationUpdater.Update(spec, new[]
        {
            new ComponentData { Key = "drawing:a.idw", Kind = ComponentKind.Document, Designation = "АБВГ.301234.001", Name = "Чертеж" },
        }, context, TestData.Now);
        var row = spec.Rows.Single();

        SpecificationEditor.SetDocumentType(spec, context, row, "СБ");

        Assert.Equal("АБВГ.301234.001СБ", row.Get(SpecColumn.Designation));
        Assert.Equal("Сборочный чертеж", row.Get(SpecColumn.Name));
        Assert.True(row.IsOverridden(SpecColumn.Designation)); // в файлах ничего не меняется
    }

    [Fact]
    public void Part_name_is_not_touched_by_codes()
    {
        var spec = new Specification();
        var context = TestData.Context(spec);
        var part = SpecificationEditor.AddManualItem(spec, context, new ComponentSnapshot { SectionId = SectionIds.Parts, Name = "" });

        SpecificationEditor.SetCell(spec, context, part, SpecColumn.Designation, "АБВГ.715431.001СБ");

        Assert.Equal("", part.Get(SpecColumn.Name));
    }

    [Fact]
    public void Check_warns_about_document_without_code()
    {
        var spec = new Specification();
        var context = TestData.Context(spec);
        var doc = SpecificationEditor.AddManualItem(spec, context,
            new ComponentSnapshot { SectionId = SectionIds.Documentation, Designation = "АБВГ.301234.001", Name = "Чертеж", Format = "А1" });

        var issue = Assert.Single(SpecificationChecker.Check(spec, context));

        Assert.Equal(doc.Id, issue.RowId);
        Assert.Contains("нет кода", issue.Message);
    }
}
