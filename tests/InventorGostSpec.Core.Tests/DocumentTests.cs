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
    public void Document_type_is_ignored_outside_documentation()
    {
        var spec = new Specification();
        var context = TestData.Context(spec);
        SpecificationUpdater.Update(spec, new[] { TestData.Part("АБВГ.715431.001", "Корпус") }, context, TestData.Now);
        var row = spec.Rows.Single();

        SpecificationEditor.SetDocumentType(spec, context, row, "СБ");

        Assert.Equal("АБВГ.715431.001", row.Get(SpecColumn.Designation));
        Assert.Equal("Корпус", row.Get(SpecColumn.Name));
        Assert.False(row.IsOverridden(SpecColumn.Designation));
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

public class ModelNotDocumentTests
{
    [Fact]
    public void Model_with_documentation_section_property_goes_to_its_own_section()
    {
        var spec = new Specification();
        var context = TestData.Context(spec);
        var unit = TestData.Assembly("АБВГ.301234.010СБ", "Узел");
        unit.SectionId = SectionIds.Documentation;
        var part = TestData.Part("АБВГ.715431.001", "Корпус");
        part.SectionId = " Documentation ";

        SpecificationUpdater.Update(spec, new[] { unit, part }, context, TestData.Now);

        Assert.Equal(SectionIds.AssemblyUnits, TestData.ByName(spec, "Узел").SectionId);
        Assert.Equal("АБВГ.301234.010", TestData.ByName(spec, "Узел").Get(SpecColumn.Designation));
        Assert.Equal(SectionIds.Parts, TestData.ByName(spec, "Корпус").SectionId);
        Assert.DoesNotContain(spec.Rows, r => r.SectionId == SectionIds.Documentation);
    }

    [Fact]
    public void Model_row_cannot_be_moved_to_documentation_but_manual_row_can()
    {
        var spec = new Specification();
        var context = TestData.Context(spec);
        SpecificationUpdater.Update(spec, new[] { TestData.Part("А.001", "Корпус") }, context, TestData.Now);
        var manual = SpecificationEditor.AddManualItem(spec, context, new ComponentSnapshot { SectionId = SectionIds.Parts, Name = "Чертеж" });

        SpecificationEditor.SetCell(spec, context, TestData.ByName(spec, "Корпус"), SpecColumn.Section, SectionIds.Documentation);
        SpecificationEditor.SetCell(spec, context, manual, SpecColumn.Section, SectionIds.Documentation);

        Assert.Equal(SectionIds.Parts, TestData.ByName(spec, "Корпус").SectionId);
        Assert.Equal(SectionIds.Documentation, manual.SectionId);
    }

    [Fact]
    public void Old_override_to_documentation_is_dropped_on_update()
    {
        var spec = new Specification();
        var context = TestData.Context(spec);
        var components = new[] { TestData.Assembly("А.010", "Узел") };
        SpecificationUpdater.Update(spec, components, context, TestData.Now);
        var row = spec.Rows.Single();
        row.Overrides[SpecColumn.Section] = SectionIds.Documentation; // как в спецификации старой версии

        SpecificationUpdater.Update(spec, components, context, TestData.Now);

        Assert.Equal(SectionIds.AssemblyUnits, row.SectionId);
        Assert.NotNull(row.Position);
    }
}

public class ExternalDocumentTests
{
    [Theory]
    [InlineData(@"C:\Док\АБВГ.301234.001ТУ-Технические условия-ред2.pdf", "АБВГ.301234.001ТУ", "Технические условия")]
    [InlineData("АБВГ.301234.001-01ТУ-Условия-копия.pdf", "АБВГ.301234.001-01ТУ", "Условия")]
    [InlineData("АБВГ.301234.001Э3 - Схема электрическая - v2.pdf", "АБВГ.301234.001Э3", "Схема электрическая")]
    [InlineData("АБВГ.301234.001ПЗ.docx", "АБВГ.301234.001ПЗ", "")]
    public void Designation_and_name_from_file_name(string path, string designation, string name) =>
        Assert.Equal((designation, name), ExternalDocuments.ParseFileName(path));

    [Fact]
    public void Document_row_gets_type_name_and_empty_format()
    {
        var spec = new Specification();
        var context = TestData.Context(spec);

        var row = SpecificationEditor.AddExternalDocument(spec, context, @"Документы\АБВГ.301234.001ТУ-ТУ на изделие.pdf");

        Assert.Equal(RowKind.ManualItem, row.Kind);
        Assert.Equal(SectionIds.Documentation, row.SectionId);
        Assert.Equal("АБВГ.301234.001ТУ", row.Get(SpecColumn.Designation));
        Assert.Equal("Технические условия", row.Get(SpecColumn.Name));
        Assert.Equal("", row.Get(SpecColumn.Format));
        Assert.Equal("", row.Get(SpecColumn.Quantity));
        Assert.Null(row.Position);
        Assert.Equal(@"Документы\АБВГ.301234.001ТУ-ТУ на изделие.pdf", row.DocumentFile);
    }

    [Fact]
    public void Without_code_name_comes_from_file()
    {
        var values = ExternalDocuments.FromFile("АБВГ.301234.001-Паспорт-2024.pdf");

        Assert.Equal(("АБВГ.301234.001", "Паспорт"), (values.Designation, values.Name));
    }
}
