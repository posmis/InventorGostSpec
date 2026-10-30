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
        VersionSync.Sync(spec, context, Head, new Dictionary<string, VersionComponents> { [version.Id] = new VersionComponents(Version01) });
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
    public void Versions_of_the_same_product_and_same_designation_share_the_position()
    {
        // Ящик: основное исполнение и -01. Блоки -01 — другие файлы, исполнения тех же изделий; «12.00.000» в -01 —
        // другой файл с тем же обозначением; «10.43.000» — новая составная часть.
        var head = new[]
        {
            TestData.Assembly("АБВГ.10.10.000", "Блок электроники"),
            TestData.Assembly("АБВГ.10.30.000", "Блок контроллеров"),
            TestData.Assembly("АБВГ.12.00.000", "Жгут"),
            TestData.Part("АБВГ.00.00.001", "Корпус"),
        };
        var version01 = new[]
        {
            TestData.Assembly("АБВГ.10.10.000-01", "Блок электроники"),
            TestData.Assembly("АБВГ.10.30.000-01", "Блок контроллеров"),
            TestData.Assembly("АБВГ.10.43.000", "Блок электроники лебедки"),
            new ComponentData { Key = @"Исп01\АБВГ.12.00.000.iam", Kind = ComponentKind.Assembly, Designation = "АБВГ.12.00.000", Name = "Жгут", Quantity = 1 },
            TestData.Part("АБВГ.00.00.001", "Корпус"),
        };
        var spec = new Specification();
        var context = TestData.Context(spec);
        SpecificationUpdater.Update(spec, head, context, TestData.Now);
        var version = VersionSync.AddVersion(spec, "АБВГ.10.00.000", "АБВГ.10.00.000-01", "v1.iam");
        VersionSync.Sync(spec, context, head, new Dictionary<string, VersionComponents> { [version.Id] = new VersionComponents(version01) });
        PositionAllocator.RenumberAll(spec, context);
        var baseId = VersionSync.Base(spec)!.Id;

        Assert.Equal(Row(spec, "АБВГ.10.10.000", baseId).Position, Row(spec, "АБВГ.10.10.000-01", version.Id).Position);
        Assert.Equal(Row(spec, "АБВГ.10.30.000", baseId).Position, Row(spec, "АБВГ.10.30.000-01", version.Id).Position);
        Assert.Equal(Row(spec, "АБВГ.12.00.000", baseId).Position, Row(spec, "АБВГ.12.00.000", version.Id).Position);
        var baseMax = spec.Rows.Where(r => r.GroupId == baseId).Max(r => r.Position!.Value);
        Assert.Equal(baseMax + 1, Row(spec, "АБВГ.10.43.000", version.Id).Position);
        Assert.DoesNotContain(Checking.SpecificationChecker.Check(spec, context), i => i.Severity == Checking.IssueSeverity.Error);

        // То же после сохранения и чтения.
        var copy = Storage.SpecificationSerializer.Deserialize(Storage.SpecificationSerializer.Serialize(spec));
        Assert.Equal(Row(spec, "АБВГ.10.10.000", baseId).Position, Row(copy, "АБВГ.10.10.000-01", version.Id).Position);
    }

    [Theory]
    [InlineData("АБВГ.10.10.000-01", "АБВГ.10.10.000")]
    [InlineData("АБВГ.301234.001 - 12", "АБВГ.301234.001")]
    [InlineData("АБВГ.301234.001", "АБВГ.301234.001")]
    [InlineData("АБВГ.686189.349-08", "АБВГ.686189.349")]
    [InlineData("М12-6g", "М12-6g")]
    public void Version_number_is_stripped(string designation, string expected) =>
        Assert.Equal(expected, Designations.WithoutVersionNumber(designation));

    [Fact]
    public void Version_rows_are_read_only_in_the_editor()
    {
        var (spec, context, version) = Build();
        var items = spec.Rows.Where(r => r.GroupId == version.Id && r.SectionId == SectionIds.AssemblyUnits).ToList();

        Assert.False(Editing.SpecificationEditor.MoveRow(spec, items[1].Id, items[0].Id));
        Assert.False(Editing.SpecificationEditor.DeleteRow(spec, items[0].Id));
        Editing.SpecificationEditor.SetCell(spec, context, items[0], SpecColumn.Name, "Другое");
        Assert.NotEqual("Другое", items[0].Get(SpecColumn.Name));
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
        VersionSync.Sync(spec, context, Head, new Dictionary<string, VersionComponents>
        {
            [spec.Groups.Single(g => g.VersionFile.Contains("Исп01")).Id] = new VersionComponents(Version01),
            [same.Id] = new VersionComponents(Version01),
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
        VersionSync.Sync(spec, context, Head, new Dictionary<string, VersionComponents> { [version.Id] = new VersionComponents(withoutShield) });

        var rows = SpecificationViewBuilder.Build(spec, context).ToList();
        var versionHeader = rows.First(r => r.Kind == DisplayRowKind.GroupHeader && r.GroupId == version.Id);
        Assert.Equal("Отсутствуют", rows[rows.IndexOf(versionHeader) + 1].SectionName);

        VersionSync.RemoveVersion(spec, context, version.Id);

        Assert.Empty(spec.Groups);
        Assert.All(spec.Rows, r => Assert.Equal("", r.GroupId));
        Assert.DoesNotContain(spec.Rows, r => r.Kind == RowKind.VersionItem);
    }

    [Fact]
    public void Version_groups_from_the_version_assembly_go_under_the_version()
    {
        var spec = new Specification();
        var context = TestData.Context(spec);
        SpecificationUpdater.Update(spec, Head, context, TestData.Now);
        var version = VersionSync.AddVersion(spec, "АГБВ.685910.321", "АГБВ.685910.321-01", "v1.iam");
        const string title = "Изделия, устанавливаемые по чертежу АГБВ.685910.321-01МЭ";
        var titles = new Dictionary<string, string> { ["АГБВ.688245.005.ipt"] = title };

        VersionSync.Sync(spec, context, Head, new Dictionary<string, VersionComponents> { [version.Id] = new VersionComponents(Version01, titles) });

        var child = spec.Groups.Single(g => g.VersionId == version.Id);
        Assert.Equal(title, child.Title);
        Assert.Equal(spec.Groups.IndexOf(version) + 1, spec.Groups.IndexOf(child));
        var shield = Row(spec, "АГБВ.688245.005", child.Id);
        Assert.Equal(RowKind.VersionItem, shield.Kind);
        Assert.True(VersionSync.IsReadOnly(spec, child.Id));

        var rows = SpecificationViewBuilder.Build(spec, context).ToList();
        var versionHeader = rows.FindIndex(r => r.Kind == DisplayRowKind.GroupHeader && r.GroupId == version.Id);
        var childHeader = rows.FindIndex(r => r.Kind == DisplayRowKind.GroupHeader && r.GroupId == child.Id);
        Assert.True(childHeader > versionHeader);
        Assert.Contains(rows.Skip(childHeader), r => r.Row == shield);

        // В сборке-исполнении компонент вынули из группы — строка возвращается в исполнение, группа исчезает.
        VersionSync.Sync(spec, context, Head, new Dictionary<string, VersionComponents> { [version.Id] = new VersionComponents(Version01) });
        Assert.DoesNotContain(spec.Groups, g => g.VersionId == version.Id);
        Assert.Equal(version.Id, spec.Rows.Single(r => r.Get(SpecColumn.Designation) == "АГБВ.688245.005").GroupId);
    }

    [Fact]
    public void Base_version_group_keeps_manual_rows_and_becomes_constant_when_versions_are_removed()
    {
        var (spec, context, version) = Build();
        var baseVersion = VersionSync.Base(spec)!;
        var group = Editing.SpecificationEditor.AddGroup(spec, "Изделия, устанавливаемые по чертежу А.000МЭ", versionId: baseVersion.Id);
        Assert.True(spec.Groups.IndexOf(group) < spec.Groups.IndexOf(version));
        var rotor = Row(spec, "АГБВ.686295.012", baseVersion.Id);
        Editing.SpecificationEditor.MoveToGroup(spec, context, rotor, group.Id);

        VersionSync.Sync(spec, context, Head, new Dictionary<string, VersionComponents> { [version.Id] = new VersionComponents(Version01) });
        Assert.Equal(group.Id, rotor.GroupId);
        Assert.False(VersionSync.IsReadOnly(spec, group.Id));

        VersionSync.RemoveVersion(spec, context, version.Id);
        Assert.Equal("", group.VersionId);
        Assert.Equal(new[] { group }, spec.Groups);
        Assert.Equal(group.Id, rotor.GroupId);
    }

    [Fact]
    public void Positions_for_a_version_are_constant_data_and_its_variable_data_only()
    {
        var (spec, context, version) = Build();
        PositionAllocator.RenumberAll(spec, context);
        var baseVersion = VersionSync.Base(spec)!;

        var positions = VersionSync.PositionsFor(spec, version);

        // Постоянные данные — есть.
        Assert.Equal(Row(spec, "АГБВ.686285.129", "").Position, positions["АГБВ.686285.129.iam"]);
        // Своя составная часть -01 — есть, в том числе номер, взятый у основного исполнения.
        Assert.Equal(Row(spec, "АГБВ.686295.012-01", version.Id).Position, positions["АГБВ.686295.012-01.iam"]);
        Assert.Equal(Row(spec, "АГБВ.686295.012", baseVersion.Id).Position, positions["АГБВ.686295.012-01.iam"]);
        // Переменные данные основного исполнения, которых нет в -01, — нет.
        Assert.False(positions.ContainsKey("АГБВ.686189.349-08.iam"));
    }
}

public class VariableDataSwitchTests
{
    [Fact]
    public void Switch_shows_empty_variable_data_and_returns_rows_when_turned_off()
    {
        var spec = new Specification();
        var context = TestData.Context(spec);
        SpecificationUpdater.Update(spec, new[] { TestData.Part("А.001", "Корпус"), TestData.Part("А.002", "Ось") }, context, TestData.Now);

        Assert.True(VersionSync.SetShown(spec, context, true, "АБВГ.00.00.000"));

        var rows = SpecificationViewBuilder.Build(spec, context).ToList();
        Assert.Contains(rows, r => r.Kind == DisplayRowKind.VersionsHeader);
        var header = rows.FindIndex(r => r.Kind == DisplayRowKind.GroupHeader && r.SectionName == "АБВГ.00.00.000");
        Assert.Equal("Отсутствуют", rows[header + 1].SectionName);

        // Строку можно перенести в основное исполнение вручную.
        var axis = TestData.ByName(spec, "Ось");
        Editing.SpecificationEditor.MoveToGroup(spec, context, axis, VersionSync.Base(spec)!.Id);
        Assert.Contains(SpecificationViewBuilder.Build(spec, context), r => r.Row == axis);

        // С исполнением выключить нельзя.
        VersionSync.AddVersion(spec, "АБВГ.00.00.000", "АБВГ.00.00.000-01", "v1.iam");
        Assert.False(VersionSync.SetShown(spec, context, false, "АБВГ.00.00.000"));
        VersionSync.RemoveVersion(spec, context, spec.Groups.Single(g => g.VersionFile == "v1.iam").Id);
        Assert.NotNull(VersionSync.Base(spec)); // галочка включена — раздел остаётся

        Assert.True(VersionSync.SetShown(spec, context, false, "АБВГ.00.00.000"));
        Assert.Empty(spec.Groups);
        Assert.Equal("", axis.GroupId);
        Assert.DoesNotContain(SpecificationViewBuilder.Build(spec, context), r => r.Kind == DisplayRowKind.VersionsHeader);
    }

}

public class RebaseKeyTests
{
    [Theory]
    [InlineData(@"Детали/А.ipt", "/p/Исп01", "/p/Головная", @"../Исп01/Детали/А.ipt")]
    [InlineData(@"../Общие/Б.ipt", "/p/Исп01", "/p/Головная", @"../Общие/Б.ipt")]
    [InlineData("virtual:Смазка", "/p/Исп01", "/p/Головная", "virtual:Смазка")]
    [InlineData("drawing:Узел.idw", "/p/Исп01", "/p/Головная", "drawing:../Исп01/Узел.idw")]
    public void Keys_are_rebased_between_assembly_folders(string key, string from, string to, string expected) =>
        Assert.Equal(expected, VersionSync.RebaseKey(key, from, to));

    [Fact]
    public void Rebase_there_and_back_gives_the_same_key()
    {
        const string key = "Детали/А.ipt";
        var head = VersionSync.RebaseKey(key, "/p/Исп01", "/p/Головная");
        Assert.Equal(key, VersionSync.RebaseKey(head, "/p/Головная", "/p/Исп01"));
    }
}
