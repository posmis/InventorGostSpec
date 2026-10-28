using InventorGostSpec.Core.Model;
using InventorGostSpec.Core.Positions;
using InventorGostSpec.Core.Sections;
using InventorGostSpec.Core.Update;
using InventorGostSpec.Core.Versions;
using InventorGostSpec.Core.View;

namespace InventorGostSpec.Core.Tests;

/// <summary>Групповая спецификация исполнений, ГОСТ 2.113 вариант А (по примеру приложения 6).</summary>
public class VersionTests
{
    private static readonly ComponentData[] Head =
    {
        TestData.Assembly("АГБВ.686285.129", "Коробка выводов"),
        TestData.Part("АГБВ.688283.128", "Крышка подшипника"),
        TestData.Part("АГБВ.688284.121", "Крышка подшипника", 2),
        TestData.Assembly("АГБВ.686189.349-08", "Статор"),
        TestData.Assembly("АГБВ.686243.185", "Щит подшипниковый", 2),
        TestData.Assembly("АГБВ.686295.012", "Ротор"),
    };

    private static readonly ComponentData[] Version01 =
    {
        TestData.Assembly("АГБВ.686285.129", "Коробка выводов"),
        TestData.Part("АГБВ.688283.128", "Крышка подшипника"),
        TestData.Part("АГБВ.688284.121", "Крышка подшипника", 2),
        TestData.Assembly("АГБВ.686189.350-03", "Статор"),
        TestData.Assembly("АГБВ.686295.012-01", "Ротор"),
        TestData.Part("АГБВ.688245.005", "Щит подшипниковый", 2),
        TestData.Assembly("АГБВ.686243.185", "Щит подшипниковый", 3),
    };

    private static (Specification Spec, SpecificationContext Context, SpecGroup Version) Build()
    {
        var spec = new Specification();
        var context = TestData.Context(spec);
        SpecificationUpdater.Update(spec, Head, context, TestData.Now);
        var version = VersionSync.AddVersion(spec, "АГБВ.685910.321", "АГБВ.685910.321-01", @"..\Исп01\Двигатель-01.iam");
        VersionSync.Sync(spec, context, Head, new Dictionary<string, IReadOnlyList<ComponentData>> { [version.Id] = Version01 });
        return (spec, context, version);
    }

    private static SpecRow Row(Specification spec, string designation, string groupId) =>
        spec.Rows.Single(r => r.GroupId == groupId && r.Get(SpecColumn.Designation) == designation);

    [Fact]
    public void Common_components_stay_in_constant_data_and_others_go_to_versions()
    {
        var (spec, _, version) = Build();
        var baseVersion = VersionSync.Base(spec)!;

        Assert.Equal("АГБВ.685910.321", baseVersion.Title);
        Assert.Equal(new[] { "АГБВ.686285.129", "АГБВ.688283.128", "АГБВ.688284.121" },
            spec.Rows.Where(r => r.GroupId == "").Select(r => r.Get(SpecColumn.Designation)).OrderBy(x => x));
        Assert.Equal(new[] { "АГБВ.686189.349-08", "АГБВ.686243.185", "АГБВ.686295.012" },
            spec.Rows.Where(r => r.GroupId == baseVersion.Id).Select(r => r.Get(SpecColumn.Designation)).OrderBy(x => x));
        var items = spec.Rows.Where(r => r.GroupId == version.Id).ToList();
        Assert.All(items, r => Assert.Equal(RowKind.VersionItem, r.Kind));
        Assert.Equal(4, items.Count);
        Assert.Equal(SectionIds.Parts, Row(spec, "АГБВ.688245.005", version.Id).SectionId);
    }

    [Fact]
    public void Positions_continue_constant_data_and_same_file_keeps_its_number()
    {
        var (spec, _, version) = Build();
        var baseVersion = VersionSync.Base(spec)!;

        var constantMax = spec.Rows.Where(r => r.GroupId == "").Max(r => r.Position!.Value);
        Assert.All(spec.Rows.Where(r => r.GroupId == baseVersion.Id), r => Assert.True(r.Position > constantMax));

        // Щит АГБВ.686243.185 — тот же файл, другое количество: в обоих исполнениях один номер.
        var shield = Row(spec, "АГБВ.686243.185", version.Id);
        Assert.True(shield.InheritsPosition);
        Assert.Equal(Row(spec, "АГБВ.686243.185", baseVersion.Id).Position, shield.Position);
        Assert.Equal("3", shield.Get(SpecColumn.Quantity));

        // Другие файлы — новые номера, после номеров основного исполнения.
        var baseMax = spec.Rows.Where(r => r.GroupId == baseVersion.Id).Max(r => r.Position!.Value);
        Assert.True(Row(spec, "АГБВ.686189.350-03", version.Id).Position > baseMax);
    }

    [Fact]
    public void Replacing_part_takes_the_number_of_the_replaced_one()
    {
        var (spec, context, version) = Build();
        var baseStator = Row(spec, "АГБВ.686189.349-08", VersionSync.Base(spec)!.Id);
        var stator = Row(spec, "АГБВ.686189.350-03", version.Id);

        stator.ReplacesKey = baseStator.ComponentKey;
        VersionSync.MarkInherited(spec);
        PositionAllocator.AssignMissing(spec, context);

        Assert.Equal(baseStator.Position, stator.Position);

        // После перенумерации номер по-прежнему тот же, что у заменяемой части.
        PositionAllocator.RenumberAll(spec, context);
        Assert.Equal(baseStator.Position, stator.Position);
        // Строки исполнения — по возрастанию унаследованных номеров, новые — в конце раздела.
        var units = spec.Rows.Where(r => r.GroupId == version.Id && r.SectionId == SectionIds.AssemblyUnits).ToList();
        Assert.Equal(stator, units[0]);
        Assert.DoesNotContain(Checking.SpecificationChecker.Check(spec, context), i => i.Message.Contains("повторяется"));
    }

    [Fact]
    public void View_has_variable_data_header_version_headers_and_same_as()
    {
        var (spec, context, _) = Build();
        var same = VersionSync.AddVersion(spec, "АГБВ.685910.321", "АГБВ.685910.321-02", @"..\Исп02\Двигатель-02.iam");
        VersionSync.Sync(spec, context, Head, new Dictionary<string, IReadOnlyList<ComponentData>>
        {
            [spec.Groups.Single(g => g.VersionFile.Contains("Исп01")).Id] = Version01,
            [same.Id] = Version01,
        });

        var rows = SpecificationViewBuilder.Build(spec, context).ToList();
        var header = rows.Single(r => r.Kind == DisplayRowKind.VersionsHeader);
        Assert.Equal(VersionSync.VariableDataTitle, header.SectionName);
        var titles = rows.Where(r => r.Kind == DisplayRowKind.GroupHeader).Select(r => r.SectionName).ToList();
        Assert.Equal(new[] { "АГБВ.685910.321", "АГБВ.685910.321-01", "АГБВ.685910.321-02 (тоже как для АГБВ.685910.321-01)" }, titles);
        Assert.True(rows.IndexOf(header) < rows.IndexOf(rows.First(r => r.Kind == DisplayRowKind.GroupHeader)));
        Assert.DoesNotContain(rows, r => r.GroupId == same.Id && r.Kind == DisplayRowKind.Row);
    }

    [Fact]
    public void Version_without_variable_data_says_absent_and_removing_versions_restores_constant_data()
    {
        var spec = new Specification();
        var context = TestData.Context(spec);
        SpecificationUpdater.Update(spec, Head, context, TestData.Now);
        var version = VersionSync.AddVersion(spec, "А", "А-01", "a01.iam");
        var withoutShield = Head.Where(c => c.Name != "Щит подшипниковый").ToArray();
        VersionSync.Sync(spec, context, Head, new Dictionary<string, IReadOnlyList<ComponentData>> { [version.Id] = withoutShield });

        var rows = SpecificationViewBuilder.Build(spec, context).ToList();
        var versionHeader = rows.First(r => r.Kind == DisplayRowKind.GroupHeader && r.GroupId == version.Id);
        Assert.Equal("Отсутствуют", rows[rows.IndexOf(versionHeader) + 1].SectionName);

        VersionSync.RemoveVersion(spec, context, version.Id);

        Assert.Empty(spec.Groups);
        Assert.All(spec.Rows, r => Assert.Equal("", r.GroupId));
        Assert.DoesNotContain(spec.Rows, r => r.Kind == RowKind.VersionItem);
    }
}
