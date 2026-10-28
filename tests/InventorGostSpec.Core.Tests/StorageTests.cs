using InventorGostSpec.Core.Model;
using InventorGostSpec.Core.Storage;
using InventorGostSpec.Core.Versions;

namespace InventorGostSpec.Core.Tests;

/// <summary>Хранение новых данных спецификации: исполнения, «с нового листа», основная надпись, сторонние документы.</summary>
public class StorageTests
{
    [Fact]
    public void New_data_survives_serialization()
    {
        var spec = new Specification { ShowVariableData = true };
        spec.SetStartsNewPage("", Sections.SectionIds.Parts, true);
        spec.Title = new SpecTitleData
        {
            IsSet = true,
            Letter = "О1",
            NameTextHeight = 7,
            Changes = { new ChangeRecord { Number = "1", Document = "АБВГ.12-24" } },
        };
        var version = VersionSync.AddVersion(spec, "А", "А-01", "a01.iam");
        var group = Editing.SpecificationEditor.AddGroup(spec, "Изделия, устанавливаемые по А-01МЭ", startsNewPage: true, versionId: version.Id);
        spec.Rows.Add(new SpecRow
        {
            Kind = RowKind.VersionItem, ComponentKey = "x.ipt", GroupId = group.Id, InheritsPosition = true, ReplacesKey = "y.ipt",
            DocumentFile = @"Док\А.pdf", DocumentHash = "AB", ManualVersionPlacement = true,
        });

        var copy = SpecificationSerializer.Deserialize(SpecificationSerializer.Serialize(spec));

        Assert.True(copy.ShowVariableData);
        Assert.True(copy.StartsNewPage("", Sections.SectionIds.Parts));
        Assert.Equal(("О1", 7.0, "АБВГ.12-24"), (copy.Title.Letter, copy.Title.NameTextHeight!.Value, copy.Title.Changes.Single().Document));
        Assert.Equal("a01.iam", copy.Groups.Single(g => g.Id == version.Id).VersionFile);
        var copyGroup = copy.Groups.Single(g => g.Id == group.Id);
        Assert.Equal((version.Id, true), (copyGroup.VersionId, copyGroup.StartsNewPage));
        var row = copy.Rows.Single();
        Assert.Equal((RowKind.VersionItem, true, "y.ipt", @"Док\А.pdf", "AB", true),
            (row.Kind, row.InheritsPosition, row.ReplacesKey, row.DocumentFile, row.DocumentHash, row.ManualVersionPlacement));
    }

    [Fact]
    public void Missing_new_fields_in_old_files_are_repaired()
    {
        // Файл старой версии: полей нет или они null.
        const string json = """{"schemaVersion":1,"rows":[],"groups":[{"id":"G1","title":"Группа","versionId":"нет такого"}],"newPageSections":null,"title":null}""";

        var spec = SpecificationSerializer.Deserialize(json);

        Assert.NotNull(spec.NewPageSections);
        Assert.NotNull(spec.Title);
        Assert.NotNull(spec.Title.Changes);
        Assert.False(spec.ShowVariableData);
        Editing.SpecificationEditor.NormalizeGroups(spec);
        Assert.Equal("", spec.Groups.Single().VersionId); // ссылка на несуществующее исполнение снимается
    }

    [Fact]
    public void Title_data_skips_empty_changes_and_copies_records()
    {
        var data = new SpecTitleData { Letter = "А", Changes = { new ChangeRecord(), new ChangeRecord { Number = "2" } } };
        var title = new TitleBlock();

        data.ApplyTo(title);
        title.Changes[0].Number = "изменено";

        Assert.Equal("А", title.Letter);
        Assert.Single(title.Changes);
        Assert.Equal("2", data.Changes[1].Number); // в спецификации не меняется
    }
}
