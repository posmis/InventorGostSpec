using InventorGostSpec.Core.Model;
using InventorGostSpec.Core.Sections;
using InventorGostSpec.UI.ViewModels;

namespace InventorGostSpec.UI.Tests;

public class UndoAndCheckTests
{
    private readonly FakeHost _host = new();
    private readonly FakeDialogs _dialogs = new();

    public UndoAndCheckTests()
    {
        _host.Settings.DefaultReserves[SectionIds.Parts] = new ReserveSettings { Count = 1 };
        _host.Components.AddRange(new[]
        {
            new ComponentData { Key = "b.ipt", Kind = ComponentKind.Part, Designation = "А.002", Name = "Ось", Format = "А4", SectionId = SectionIds.Parts, Quantity = 1 },
            new ComponentData { Key = "a.ipt", Kind = ComponentKind.Part, Designation = "А.001", Name = "Корпус", Format = "А3", SectionId = SectionIds.Parts, Quantity = 1 },
        });
    }

    /// <summary>Сохранённая спецификация (окно открыто без изменений).</summary>
    private SpecificationViewModel Open()
    {
        var spec = new Specification();
        Core.Update.SpecificationUpdater.Update(spec, _host.Components, SpecificationContext.Create(_host.Settings, spec), DateTime.UtcNow);
        return new SpecificationViewModel(_host, _dialogs, spec, post: action => action());
    }

    private static SpecRowViewModel RowByName(SpecificationViewModel vm, string name) => vm.Rows.Single(r => r.Name == name);

    private static string[] Names(SpecificationViewModel vm) =>
        vm.Rows.Where(r => r.Row is not null).Select(r => r.Name).ToArray();

    [Fact]
    public void Undo_and_redo_cell_edits()
    {
        var vm = Open();
        Assert.False(vm.UndoCommand.CanExecute(null));

        RowByName(vm, "Корпус").Note = "Литьё";
        RowByName(vm, "Корпус").Zone = "B3";
        Assert.True(vm.IsDirty);

        vm.UndoCommand.Execute(null);
        Assert.Equal("", RowByName(vm, "Корпус").Zone);
        Assert.Equal("Литьё", RowByName(vm, "Корпус").Note);

        vm.UndoCommand.Execute(null);
        Assert.Equal("", RowByName(vm, "Корпус").Note);
        Assert.False(vm.IsDirty); // вернулись к сохранённому состоянию
        Assert.False(vm.UndoCommand.CanExecute(null));

        vm.RedoCommand.Execute(null);
        vm.RedoCommand.Execute(null);
        Assert.Equal("B3", RowByName(vm, "Корпус").Zone);
        Assert.True(vm.IsDirty);
        Assert.False(vm.RedoCommand.CanExecute(null));
    }

    [Fact]
    public void New_change_clears_redo()
    {
        var vm = Open();
        RowByName(vm, "Корпус").Note = "Литьё";
        vm.UndoCommand.Execute(null);
        Assert.True(vm.RedoCommand.CanExecute(null));

        RowByName(vm, "Ось").Note = "Покупная";

        Assert.False(vm.RedoCommand.CanExecute(null));
        Assert.Contains("1", vm.UndoToolTip);
    }

    [Fact]
    public void Undo_restores_order_after_gost_sort_and_deleted_rows()
    {
        var vm = Open();
        vm.SelectedRow = RowByName(vm, "Ось");
        vm.MoveDownCommand.Execute(null);
        Assert.Equal(new[] { "Корпус", "Ось" }, Names(vm));

        vm.ResetCommand.Execute(null);
        vm.UndoCommand.Execute(null);
        Assert.Equal(new[] { "Корпус", "Ось" }, Names(vm));

        vm.SelectedRow = RowByName(vm, "Корпус");
        vm.AddItemCommand.Execute(null);
        var manual = vm.SelectedRow!.RowId;
        vm.DeleteRowCommand.Execute(null);
        Assert.DoesNotContain(vm.Rows, r => r.RowId == manual);

        vm.UndoCommand.Execute(null);
        Assert.Contains(vm.Rows, r => r.RowId == manual);
    }

    [Fact]
    public void Undo_after_save_marks_window_dirty_and_keeps_selection()
    {
        var vm = Open();
        RowByName(vm, "Корпус").Note = "Литьё";
        vm.SaveCommand.Execute(null);
        Assert.False(vm.IsDirty);
        vm.SelectedRow = RowByName(vm, "Ось");

        vm.UndoCommand.Execute(null);

        Assert.True(vm.IsDirty);
        Assert.Equal("Ось", vm.SelectedRow!.Name);
        vm.RedoCommand.Execute(null);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void Undo_covers_groups_reserves_and_update_from_assembly()
    {
        var vm = Open();
        _dialogs.OnGroup = g => g.Title = "Изделия, устанавливаемые по МЭ";
        vm.AddGroupCommand.Execute(null);
        Assert.Contains(vm.Rows, r => r.IsGroupHeader);

        vm.ShowReserveRows = false;
        _host.Components.RemoveAt(0);
        vm.UpdateFromAssemblyCommand.Execute(null);
        Assert.True(RowByName(vm, "Ось").IsRemoved);

        vm.UndoCommand.Execute(null);
        Assert.False(RowByName(vm, "Ось").IsRemoved);
        vm.UndoCommand.Execute(null);
        Assert.True(vm.ShowReserveRows);
        vm.UndoCommand.Execute(null);
        Assert.DoesNotContain(vm.Rows, r => r.IsGroupHeader);
    }

    [Fact]
    public void Undo_history_is_limited()
    {
        var vm = Open();
        for (var i = 0; i < SpecificationViewModel.UndoLimit + 20; i++)
        {
            RowByName(vm, "Корпус").Note = i.ToString();
        }

        var steps = 0;
        while (vm.UndoCommand.CanExecute(null))
        {
            vm.UndoCommand.Execute(null);
            steps++;
        }

        Assert.Equal(SpecificationViewModel.UndoLimit, steps);
        Assert.Equal("19", RowByName(vm, "Корпус").Note);
    }

    [Fact]
    public void Check_without_issues_says_so()
    {
        var vm = Open();
        _host.BomItemNumbers = new() { ["a.ipt"] = "1", ["b.ipt"] = "2" };
        _host.Title = () => new TitleBlock { Designation = "А.000", Name = "Узел", Developer = "Иванов", Checker = "Петров" };

        vm.CheckCommand.Execute(null);

        Assert.Null(_dialogs.LastCheck);
        Assert.Contains("Замечаний нет.", _dialogs.Infos);
    }

    [Fact]
    public void Check_lists_issues_and_goes_to_selected_row()
    {
        var vm = Open();
        RowByName(vm, "Ось").Name = "";
        vm.SearchText = "Корпус";
        _host.BomItemNumbers = null; // BOM недоступна — проверка всё равно выполняется
        _dialogs.OnCheck = check => check.SelectedItem!.RowId;

        vm.CheckCommand.Execute(null);

        var check = _dialogs.LastCheck!;
        Assert.StartsWith("Ошибок: 1", check.Summary);
        Assert.True(check.Items[0].IsError);
        Assert.Contains("нет наименования", check.Items[0].Message);
        Assert.Equal("", vm.SearchText);
        Assert.Equal(check.Items[0].RowId, vm.SelectedRow!.RowId);
        Assert.Single(_host.Errors);
    }

    [Fact]
    public void Check_view_model_allows_going_only_to_rows()
    {
        var check = new CheckViewModel(new[]
        {
            new Core.Checking.SpecIssue(Core.Checking.IssueSeverity.Warning, "Основная надпись: не заполнено наименование."),
            new Core.Checking.SpecIssue(Core.Checking.IssueSeverity.Info, "Строка", "row"),
        });

        Assert.False(check.CanGoToRow);
        Assert.Null(check.RowToShow);
        check.SelectedItem = check.Items[1];
        Assert.Equal("row", check.RowToShow);
        Assert.Equal("Замечание", check.Items[1].SeverityText);
    }

    [Fact]
    public void Pdf_with_errors_asks_before_creating()
    {
        var vm = Open();
        RowByName(vm, "Ось").Name = "";
        _dialogs.ConfirmAnswer = false;

        vm.ExportPdfCommand.Execute(null);

        Assert.Empty(_host.PdfCalls);
    }
}
