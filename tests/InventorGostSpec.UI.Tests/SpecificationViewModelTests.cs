using InventorGostSpec.Core.Model;
using InventorGostSpec.Core.Sections;
using InventorGostSpec.Core.Storage;
using InventorGostSpec.UI.ViewModels;

namespace InventorGostSpec.UI.Tests;

public class SpecificationViewModelTests
{
    private readonly FakeHost _host = new();
    private readonly FakeDialogs _dialogs = new();

    public SpecificationViewModelTests()
    {
        _host.Settings.DefaultReserves[SectionIds.Parts] = new ReserveSettings { Count = 2 };
        _host.Components.AddRange(new[]
        {
            Part("А.002", "Ось"),
            Part("А.001", "Корпус"),
            new ComponentData { Key = "bolt.ipt", Kind = ComponentKind.Part, IsContentCenter = true, Name = "Болт М8х20 ГОСТ 7798-70", Quantity = 4 },
        });
    }

    [Fact]
    public void New_specification_is_built_and_marked_unsaved()
    {
        var vm = Open();

        Assert.True(vm.IsDirty);
        Assert.EndsWith("*", vm.Title);
        Assert.Equal(
            new[] { "Детали", "Корпус", "Ось", "(резерв)", "(резерв)", "Стандартные изделия", "Болт М8х20 ГОСТ 7798-70" },
            vm.Rows.Select(r => r.Name));
        Assert.Equal(new[] { "", "1", "2", "3", "4", "", "5" }, vm.Rows.Select(r => r.Position));
    }

    [Fact]
    public void Editing_component_cell_creates_override()
    {
        var vm = Open();
        var row = RowByName(vm, "Корпус");

        row.Name = "Корпус сварной";

        row = RowByName(vm, "Корпус сварной");
        Assert.True(row.Overridden[nameof(SpecColumn.Name)]);
        Assert.Contains("Значение компонента: Корпус", row.ToolTips[nameof(SpecColumn.Name)]);
        Assert.Equal("Корпус", row.Row!.Source.Name);

        vm.SelectedRow = row;
        Assert.True(vm.ClearOverrideCommand.CanExecute(nameof(SpecColumn.Name)));
        vm.ClearOverrideCommand.Execute(nameof(SpecColumn.Name));
        Assert.False(RowByName(vm, "Корпус").Overridden[nameof(SpecColumn.Name)]);
    }

    [Fact]
    public void Duplicate_position_is_rejected()
    {
        var vm = Open();

        RowByName(vm, "Ось").Position = "1";

        Assert.Single(_dialogs.Warnings);
        Assert.Equal("2", RowByName(vm, "Ось").Position);
    }

    [Fact]
    public void Position_edit_rebuilds_reserve_rows()
    {
        var vm = Open();

        RowByName(vm, "Ось").Position = "3";

        Assert.Equal(new[] { "", "1", "3", "4", "", "5" }, vm.Rows.Select(r => r.Position));
    }

    [Fact]
    public void Header_and_reserve_rows_are_read_only()
    {
        var vm = Open();

        Assert.False(vm.Rows[0].CanEdit(SpecColumn.Name));
        Assert.False(vm.Rows.First(r => r.IsReserve).CanEdit(SpecColumn.Name));
        vm.Rows[0].Name = "Другое";
        Assert.Equal("Детали", vm.Rows[0].Name);
    }

    [Fact]
    public void Add_note_after_selected_row_requests_edit()
    {
        var vm = Open();
        SpecRowViewModel? edited = null;
        vm.EditRequested += (row, column) => edited = row;
        vm.SelectedRow = RowByName(vm, "Корпус");

        vm.AddNoteCommand.Execute(null);

        Assert.NotNull(edited);
        Assert.True(edited!.IsNote);
        Assert.Equal(vm.Rows.IndexOf(RowByName(vm, "Корпус")) + 1, vm.Rows.IndexOf(edited));
        Assert.Same(edited, vm.SelectedRow);

        edited.Name = "Текст примечания";
        Assert.Equal("Текст примечания", vm.SelectedRow!.Name);
        Assert.False(edited.CanEdit(SpecColumn.Position));
    }

    [Fact]
    public void Add_item_takes_reserve_position()
    {
        var vm = Open();
        vm.SelectedRow = vm.Rows.First(r => r.IsReserve);

        vm.AddItemCommand.Execute(null);

        var item = vm.SelectedRow!;
        Assert.Equal(SpecRowKind.ManualItem, item.Kind);
        Assert.Equal("3", item.Position);
        Assert.Equal("1", item.Quantity);
    }

    [Fact]
    public void Component_row_cannot_be_deleted()
    {
        var vm = Open();
        vm.SelectedRow = RowByName(vm, "Ось");

        vm.DeleteRowCommand.Execute(null);

        Assert.Single(_dialogs.Warnings);
        Assert.Contains(vm.Rows, r => r.Name == "Ось");
    }

    [Fact]
    public void Drag_to_other_section_is_rejected_within_section_reorders()
    {
        var vm = Open();

        vm.MoveRow(RowByName(vm, "Ось").RowId!, RowByName(vm, "Болт М8х20 ГОСТ 7798-70").RowId!);
        Assert.Single(_dialogs.Warnings);

        vm.MoveRow(RowByName(vm, "Ось").RowId!, RowByName(vm, "Корпус").RowId!);
        Assert.Equal(new[] { "Ось", "Корпус" }, vm.Rows.Where(r => r.Kind == SpecRowKind.Component).Select(r => r.Name).Take(2));
        Assert.Equal("1", RowByName(vm, "Ось").Position);
        Assert.Equal("Ось", vm.SelectedRow!.Name);
    }

    [Fact]
    public void Move_to_section_moves_row()
    {
        var vm = Open();
        vm.SelectedRow = RowByName(vm, "Ось");

        vm.MoveToSectionCommand.Execute(SectionIds.OtherProducts);

        Assert.Equal("Прочие изделия", vm.Rows[^2].Name);
        Assert.Equal("Ось", vm.Rows[^1].Name);
        Assert.True(vm.Rows[^1].Row!.IsOverridden(SpecColumn.Section));
    }

    [Fact]
    public void Added_section_is_shown_and_saved_globally()
    {
        var vm = Open();
        _dialogs.OnNewSection = section =>
        {
            section.Name = "Инструмент";
            section.PlaceAfter = section.PlaceAfterOptions.Single(o => o.Id == SectionIds.Parts);
            section.SaveGlobally = true;
        };

        vm.AddSectionCommand.Execute(null);

        var header = vm.SelectedRow!;
        Assert.True(header.IsHeader);
        Assert.Equal("Инструмент", header.Name);
        Assert.True(vm.Rows.IndexOf(header) < vm.Rows.IndexOf(vm.Rows.Single(r => r.Name == "Стандартные изделия")));
        Assert.Contains(_host.Settings.CustomSections, s => s.Name == "Инструмент");

        vm.AddItemCommand.Execute(null);
        Assert.Equal(header.SectionId, vm.SelectedRow!.SectionId);
    }

    [Fact]
    public void Reserve_change_renumbers()
    {
        var vm = Open();
        _dialogs.OnReserves = reserves =>
        {
            reserves.Items.Single(i => i.SectionId == SectionIds.Parts).Count = 0;
            reserves.Items.Single(i => i.SectionId == SectionIds.Parts).RoundStartTo = 0;
            reserves.Items.Single(i => i.SectionId == SectionIds.StandardProducts).RoundStartTo = 10;
        };

        vm.ReservesCommand.Execute(null);

        Assert.Equal("10", RowByName(vm, "Болт М8х20 ГОСТ 7798-70").Position);
        Assert.Equal(0, vm.Specification.ReserveOverrides[SectionIds.Parts].Count);
    }

    [Fact]
    public void Update_highlights_new_component_and_accept_clears_it()
    {
        var vm = Open();
        _host.Components.Add(Part("А.003", "Втулка"));

        vm.UpdateFromAssemblyCommand.Execute(null);

        var row = RowByName(vm, "Втулка");
        Assert.True(row.IsNew);
        Assert.Equal("3", row.Position);
        Assert.Contains("новых: 1", vm.StatusText);

        vm.AcceptChangesCommand.Execute(null);
        Assert.False(RowByName(vm, "Втулка").IsNew);
    }

    [Fact]
    public void Reserve_rows_can_be_hidden()
    {
        var vm = Open();

        vm.ShowReserveRows = false;

        Assert.DoesNotContain(vm.Rows, r => r.IsReserve);
    }

    [Fact]
    public void Window_works_on_a_copy_until_saved()
    {
        var original = new Specification();
        var vm = new SpecificationViewModel(_host, _dialogs, original, post: Immediate);

        vm.UpdateFromAssemblyCommand.Execute(null);

        Assert.Empty(original.Rows);
        vm.SaveCommand.Execute(null);
        Assert.Equal(1, _host.SaveCount);
        Assert.False(vm.IsDirty);
        Assert.Equal(3, _host.Saved!.Rows.Count);
    }

    [Fact]
    public void Closing_with_changes_asks_to_save()
    {
        var vm = Open();

        _dialogs.SaveAnswer = null;
        Assert.False(vm.CanClose());

        _dialogs.SaveAnswer = false;
        Assert.True(vm.CanClose());
        Assert.Equal(0, _host.SaveCount);

        _dialogs.SaveAnswer = true;
        Assert.True(vm.CanClose());
        Assert.Equal(1, _host.SaveCount);
    }

    [Fact]
    public void Write_positions_saves_first()
    {
        var vm = Open();

        vm.WritePositionsCommand.Execute(null);

        Assert.Equal(1, _host.SaveCount);
        Assert.Equal(1, _host.WritePositionsCount);
        Assert.Equal(new[] { "ok" }, _dialogs.Infos);
    }

    [Fact]
    public void Group_is_added_rows_moved_and_positions_continue()
    {
        var vm = Open();
        _dialogs.OnGroup = g =>
        {
            g.Title = "Изделия, устанавливаемые по электромонтажному чертежу А.000МЭ";
            g.FirstPositionText = "101";
        };

        vm.AddGroupCommand.Execute(null);
        var header = vm.SelectedRow!;
        Assert.True(header.IsGroupHeader);
        Assert.Equal("Изделия, устанавливаемые по электромонтажному чертежу А.000МЭ", header.Name);
        Assert.Equal(new[] { "", "Изделия, устанавливаемые по электромонтажному чертежу А.000МЭ" }, vm.GroupOptions.Select(o => o.Name == "Основная часть" ? "" : o.Name));

        vm.SelectedRow = RowByName(vm, "Болт М8х20 ГОСТ 7798-70");
        vm.MoveToGroupCommand.Execute(header.GroupId);

        var names = vm.Rows.Select(r => r.Name).ToList();
        Assert.Equal(
            new[] { "Изделия, устанавливаемые по электромонтажному чертежу А.000МЭ", "Стандартные изделия", "Болт М8х20 ГОСТ 7798-70" },
            names.Skip(names.IndexOf("Изделия, устанавливаемые по электромонтажному чертежу А.000МЭ")));
        Assert.Equal("101", RowByName(vm, "Болт М8х20 ГОСТ 7798-70").Position);
    }

    [Fact]
    public void Group_header_title_is_editable_and_group_can_be_deleted()
    {
        var vm = Open();
        _dialogs.OnGroup = g => g.Title = "Устанавливают по МЭ";
        vm.AddGroupCommand.Execute(null);
        vm.SelectedRow = RowByName(vm, "Ось");
        vm.MoveToGroupCommand.Execute(vm.GroupOptions[1].Id);

        var header = vm.Rows.Single(r => r.IsGroupHeader);
        Assert.True(header.CanEdit(SpecColumn.Name));
        header.Name = "Изделия, устанавливаемые по МЭ";
        Assert.Equal("Изделия, устанавливаемые по МЭ", vm.Rows.Single(r => r.IsGroupHeader).Name);

        vm.SelectedRow = vm.Rows.Single(r => r.IsGroupHeader);
        vm.DeleteGroupCommand.Execute(null);

        Assert.DoesNotContain(vm.Rows, r => r.IsGroupHeader);
        Assert.Equal("", RowByName(vm, "Ось").GroupId);
    }

    [Fact]
    public void Item_added_on_group_header_goes_to_group()
    {
        var vm = Open();
        _dialogs.OnGroup = g => g.Title = "Устанавливают по МЭ";
        vm.AddGroupCommand.Execute(null);
        var groupId = vm.SelectedRow!.GroupId;

        vm.AddItemCommand.Execute(null);

        var item = vm.SelectedRow!;
        Assert.Equal(groupId, item.GroupId);
        Assert.Equal(SectionIds.OtherProducts, item.SectionId);
        Assert.Equal("Прочие изделия", vm.Rows[vm.Rows.IndexOf(item) - 1].Name);
    }

    [Fact]
    public void Each_part_has_its_own_reserve()
    {
        var vm = Open();
        _dialogs.OnGroup = g => g.Title = "Устанавливают по МЭ";
        vm.AddGroupCommand.Execute(null);
        vm.SelectedRow = RowByName(vm, "Ось");
        vm.MoveToGroupCommand.Execute(vm.GroupOptions[1].Id);

        _dialogs.OnReserves = reserves =>
        {
            Assert.Equal(2, reserves.Parts.Count);
            Assert.Same(reserves.Parts[1], reserves.SelectedPart); // выбрана строка группы
            reserves.SelectedPart.FirstPositionText = "50";
            reserves.Items.Single(i => i.SectionId == SectionIds.Parts).Count = 5;
        };
        vm.ReservesCommand.Execute(null);

        var group = vm.Specification.Groups.Single();
        Assert.Equal(50, group.FirstPosition);
        Assert.Equal(5, group.Reserves[SectionIds.Parts].Count);
        Assert.Equal(2, vm.Specification.ReserveOverrides[SectionIds.Parts].Count); // основная часть не изменилась
        Assert.Equal("50", RowByName(vm, "Ось").Position);
    }

    private SpecificationViewModel Open()
    {
        // Как при первом открытии: спецификация сформирована, но не сохранена.
        var spec = new Specification();
        var context = SpecificationContext.Create(_host.Settings, spec);
        Core.Update.SpecificationUpdater.Update(spec, _host.Components, context, DateTime.UtcNow);
        return new SpecificationViewModel(_host, _dialogs, SpecificationSerializer.Deserialize(SpecificationSerializer.Serialize(spec)), isNew: true, post: Immediate);
    }

    // Отложенные действия (перестроение после правки позиции) выполняются сразу.
    private static void Immediate(Action action) => action();

    private static SpecRowViewModel RowByName(SpecificationViewModel vm, string name) => vm.Rows.Single(r => r.Name == name);

    private static ComponentData Part(string designation, string name) => new()
    {
        Key = designation + ".ipt",
        Kind = ComponentKind.Part,
        Designation = designation,
        Name = name,
        Quantity = 1,
    };
}

public class AssignSectionViewModelTests
{
    [Fact]
    public void Only_changed_fields_are_written()
    {
        var initial = new Dictionary<string, string?>
        {
            [PropertyNames.Section] = SectionIds.Parts,
            [PropertyNames.Format] = null, // разные значения у выбранных компонентов
        };
        var vm = new AssignSectionViewModel(SectionCatalog.CreateBuiltIn(), initial, "Выбрано: 2");

        Assert.Empty(vm.GetChanges());
        Assert.True(vm.Format.IsMixed);

        vm.SelectedSection = vm.Sections.Single(s => s.Id == SectionIds.StandardProducts);
        vm.StandardNumber.Value = "ГОСТ 7798-70";

        var changes = vm.GetChanges();
        Assert.Equal(2, changes.Count);
        Assert.Equal(SectionIds.StandardProducts, changes[PropertyNames.Section]);
        Assert.Equal("ГОСТ 7798-70", changes[PropertyNames.StandardNumber]);
        Assert.True(vm.ShowProductFields);
        Assert.False(vm.ShowMaterialFields);
    }

    [Fact]
    public void Auto_section_removes_property()
    {
        var vm = new AssignSectionViewModel(SectionCatalog.CreateBuiltIn(), new Dictionary<string, string?> { [PropertyNames.Section] = SectionIds.Parts }, "");

        vm.SelectedSection = AssignSectionViewModel.AutoSection;

        Assert.Equal("", vm.GetChanges()[PropertyNames.Section]);
    }
}

public class StandardNamesViewModelTests
{
    [Fact]
    public void Statuses_and_default_selection()
    {
        var vm = new StandardNamesViewModel(new[]
        {
            new StandardNameCandidate("a", "Болт ГОСТ 7798-70 M12x60", "ГОСТ 7798-70 - М12 x 60", false),
            new StandardNameCandidate("b", "Болт М8х20 ГОСТ 7798-70", "Болт М8х20 ГОСТ 7798-70", false),
            new StandardNameCandidate("c", "ГОСТ 5915-70 М12", "", false),
        });

        var changed = vm.Items.Single(i => i.Candidate.Key == "a");
        Assert.Equal(StandardNameStatus.Change, changed.Status);
        Assert.Equal("Болт М12х60 ГОСТ 7798-70", changed.Proposed);
        Assert.True(changed.IsSelected);

        var ok = vm.Items.Single(i => i.Candidate.Key == "b");
        Assert.Equal(StandardNameStatus.AlreadyOk, ok.Status);
        Assert.False(ok.IsSelected);
        Assert.Same(ok, vm.Items.Last());

        var review = vm.Items.Single(i => i.Candidate.Key == "c");
        Assert.Equal(StandardNameStatus.Review, review.Status);
        Assert.False(review.IsSelected);

        Assert.Equal(new[] { changed }, vm.GetChanges());
    }

    [Fact]
    public void Name_missing_in_file_is_taken_from_current_description()
    {
        var item = new StandardNameItem(new StandardNameCandidate("a", "ГОСТ 7798-70 M12x60", "Болт ГОСТ 7798-70", false));

        Assert.Equal("Болт М12х60 ГОСТ 7798-70", item.Proposed);
        Assert.Equal(StandardNameStatus.Change, item.Status);
    }

    [Fact]
    public void Foreign_standard_is_marked_and_skipped()
    {
        var vm = new StandardNamesViewModel(new[]
        {
            new StandardNameCandidate("a", "Гайка барашек DIN 315 - M8 - St - C", "Гайка барашек DIN 315 - M8 - St - C", false),
            new StandardNameCandidate("b", "Гайка барашек DIN 315 - M8 - St - C", "Гайка", false),
        });

        Assert.All(vm.Items, i => Assert.Equal(StandardNameStatus.Foreign, i.Status));
        Assert.All(vm.Items, i => Assert.Equal(i.Current, i.Proposed));
        Assert.Empty(vm.GetChanges());
        vm.SelectAllCommand.Execute(null);
        Assert.Empty(vm.GetChanges());
    }

    [Fact]
    public void Manual_edit_selects_item()
    {
        var vm = new StandardNamesViewModel(new[] { new StandardNameCandidate("c", "ГОСТ 5915-70 М12", "", false) });
        var item = vm.Items[0];

        item.Proposed = "Гайка М12 ГОСТ 5915-70";

        Assert.True(item.IsSelected);
        Assert.Single(vm.GetChanges());
    }

    [Fact]
    public void Preselection_from_assembly_limits_default_selection()
    {
        var vm = new StandardNamesViewModel(new[]
        {
            new StandardNameCandidate("a", "Болт ГОСТ 7798-70 M12x60", "x", true),
            new StandardNameCandidate("b", "Болт ГОСТ 7798-70 M8x20", "y", false),
        });

        Assert.True(vm.Items.Single(i => i.Candidate.Key == "a").IsSelected);
        Assert.False(vm.Items.Single(i => i.Candidate.Key == "b").IsSelected);

        vm.SelectAllCommand.Execute(null);
        Assert.Equal(2, vm.GetChanges().Count);
        vm.SelectNoneCommand.Execute(null);
        Assert.Empty(vm.GetChanges());
    }
}
