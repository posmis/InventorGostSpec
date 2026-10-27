using InventorGostSpec.Core.Model;
using InventorGostSpec.Core.Sections;
using InventorGostSpec.Core.Storage;
using InventorGostSpec.UI.ViewModels;

namespace InventorGostSpec.UI.Tests;

/// <summary>Ошибки Inventor и файлов, отмена диалогов, пограничные случаи окна спецификации.</summary>
public class ViewModelEdgeCaseTests
{
    private readonly FakeHost _host = new();
    private readonly FakeDialogs _dialogs = new();

    public ViewModelEdgeCaseTests()
    {
        _host.Components.AddRange(new[]
        {
            new ComponentData { Key = "drawing:сб", Kind = ComponentKind.Document, Designation = "А.000СБ", Name = "Сборочный чертеж", Quantity = 1 },
            new ComponentData { Key = "a.ipt", Kind = ComponentKind.Part, Designation = "А.001", Name = "Корпус", Quantity = 1 },
            new ComponentData { Key = "b.ipt", Kind = ComponentKind.Part, Designation = "А.002", Name = "Ось", Quantity = 1 },
        });
    }

    private SpecificationViewModel Open()
    {
        var spec = new Specification();
        Core.Update.SpecificationUpdater.Update(spec, _host.Components, SpecificationContext.Create(_host.Settings, spec), DateTime.UtcNow);
        return new SpecificationViewModel(_host, _dialogs, spec, post: action => action());
    }

    private static SpecRowViewModel RowByName(SpecificationViewModel vm, string name) => vm.Rows.Single(r => r.Name == name);

    [Fact]
    public void Position_is_editable_only_for_numbered_rows()
    {
        var vm = Open();

        Assert.False(RowByName(vm, "Сборочный чертеж").CanEdit(SpecColumn.Position));
        Assert.True(RowByName(vm, "Корпус").CanEdit(SpecColumn.Position));

        RowByName(vm, "Сборочный чертеж").Position = "7";
        Assert.Equal("", RowByName(vm, "Сборочный чертеж").Position);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void Failed_write_of_one_component_does_not_stop_others()
    {
        var vm = Open();
        _host.ReadOnlyComponents.Add("a.ipt");
        vm.SetSelectedRows(new[] { RowByName(vm, "Корпус"), RowByName(vm, "Ось") });
        _dialogs.OnComponentProperties = props => props.SelectedSection = props.Sections.Single(s => s.Id == SectionIds.OtherProducts);

        vm.EditComponentCommand.Execute(null);

        Assert.Equal(SectionIds.OtherProducts, RowByName(vm, "Ось").SectionId);
        Assert.Equal(SectionIds.Parts, RowByName(vm, "Корпус").SectionId);
        Assert.Contains(_dialogs.Warnings, w => w.Contains("1 из 2") && w.Contains("только для чтения"));
        Assert.Single(_host.Errors);
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void Drawing_row_has_no_component_properties()
    {
        var vm = Open();
        vm.SelectedRow = RowByName(vm, "Сборочный чертеж");

        Assert.False(vm.EditComponentCommand.CanExecute(null));
        Assert.False(vm.InsertComponentNoteCommand.CanExecute(null));
    }

    [Fact]
    public void Cancelled_dialogs_change_nothing()
    {
        var vm = Open();
        var before = SpecificationSerializer.Serialize(vm.Specification);
        vm.SelectedRow = RowByName(vm, "Корпус");
        _dialogs.ConfirmAnswer = false;
        _dialogs.ExportPath = _ => null;

        vm.ResetCommand.Execute(null);
        vm.RenumberCommand.Execute(null);
        vm.AddSectionCommand.Execute(null);
        vm.AddGroupCommand.Execute(null);
        vm.ReservesCommand.Execute(null);
        vm.EditComponentCommand.Execute(null);
        vm.ExportExcelCommand.Execute(null);
        vm.ExportPdfCommand.Execute(null);

        Assert.Equal(before, SpecificationSerializer.Serialize(vm.Specification));
        Assert.False(vm.IsDirty);
        Assert.Empty(_host.PdfCalls);
    }

    [Fact]
    public void Closing_asks_to_save_only_when_dirty()
    {
        var vm = Open();
        _dialogs.SaveAnswer = null;
        Assert.True(vm.CanClose());

        RowByName(vm, "Корпус").Note = "Литьё";
        Assert.False(vm.CanClose());
        Assert.Equal(0, _host.SaveCount);

        _dialogs.SaveAnswer = true;
        Assert.True(vm.CanClose());
        Assert.Equal(1, _host.SaveCount);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void Window_works_on_a_copy_until_saved()
    {
        var spec = new Specification();
        Core.Update.SpecificationUpdater.Update(spec, _host.Components, SpecificationContext.Create(_host.Settings, spec), DateTime.UtcNow);
        var vm = new SpecificationViewModel(_host, _dialogs, spec, post: action => action());

        RowByName(vm, "Корпус").Note = "Литьё";

        Assert.Equal("", spec.Rows.Single(r => r.Get(SpecColumn.Name) == "Корпус").Note);
        vm.SaveCommand.Execute(null);
        Assert.Equal("Литьё", _host.Saved!.Rows.Single(r => r.Get(SpecColumn.Name) == "Корпус").Note);
    }

    [Fact]
    public void Search_ignores_case_and_yo()
    {
        var vm = Open();
        RowByName(vm, "Корпус").Note = "Литьё";

        vm.SearchText = "ЛИТЬЕ";

        Assert.Equal(new[] { "Детали", "Корпус" }, vm.Rows.Select(r => r.Name));
        vm.ClearSearchCommand.Execute(null);
        Assert.Contains(vm.Rows, r => r.Name == "Ось");
    }

    [Fact]
    public void Pdf_from_inventor_gets_change_records_from_excel_next_to_assembly()
    {
        var vm = Open();
        var folder = Directory.CreateTempSubdirectory().FullName;
        _host.ExportFolder = folder;
        try
        {
            var context = SpecificationContext.Create(null, new Specification());
            Excel.ExcelExporter.Export(new Specification(), context, new TitleBlock
            {
                NameTextHeight = 7,
                Changes = { new ChangeRecord { Number = "1", Document = "АБВГ.5-25" } },
            }, Path.Combine(folder, "spec.xlsx"));
            string? tempExcel = null;
            _host.OnBuildPdf = excel => tempExcel = excel;

            vm.ExportPdfCommand.Execute(null);

            Assert.NotNull(tempExcel);
            Assert.Equal(7, _host.LastPdfTitle!.NameTextHeight);
            Assert.Equal("АБВГ.5-25", Assert.Single(_host.LastPdfTitle.Changes).Document);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
