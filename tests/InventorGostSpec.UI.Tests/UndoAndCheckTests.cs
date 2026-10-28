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
        _host.Components.Add(new ComponentData
        {
            Key = "drawing:Привод.idw", Kind = ComponentKind.Document, Designation = "А.000СБ", Name = "Сборочный чертеж", Format = "А1",
        });
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

public class InsertSectionTests
{
    private readonly FakeHost _host = new();
    private readonly FakeDialogs _dialogs = new();

    private SpecificationViewModel Open()
    {
        _host.Components.Add(new ComponentData { Key = "a.ipt", Kind = ComponentKind.Part, Designation = "А.001", Name = "Корпус", Quantity = 1 });
        var spec = new Specification();
        Core.Update.SpecificationUpdater.Update(spec, _host.Components, SpecificationContext.Create(_host.Settings, spec), DateTime.UtcNow);
        return new SpecificationViewModel(_host, _dialogs, spec, post: action => action());
    }

    [Fact]
    public void Missing_sections_are_offered_and_inserted_with_reserve()
    {
        var vm = Open();
        vm.ShowReserveRows = false;
        var offered = vm.InsertableSections().Select(o => o.Id).ToList();
        Assert.Contains(SectionIds.Documentation, offered);
        Assert.DoesNotContain(SectionIds.Parts, offered);

        vm.InsertSectionCommand.Execute(SectionIds.Documentation);

        Assert.True(vm.ShowReserveRows);
        Assert.Equal("Документация", vm.SelectedRow!.Name);
        Assert.Equal(SpecificationViewModel.InsertedSectionReserve, vm.Rows.Count(r => r.IsReserve && r.SectionId == SectionIds.Documentation));
        Assert.Contains("Вставлен раздел «Документация»", vm.StatusText);
        Assert.DoesNotContain(SectionIds.Documentation, vm.InsertableSections().Select(o => o.Id));
        Assert.False(vm.InsertSectionCommand.CanExecute(SectionIds.Documentation));
    }

    [Fact]
    public void Drawing_row_is_added_into_inserted_documentation_and_empty_section_can_be_removed()
    {
        var vm = Open();
        vm.InsertSectionCommand.Execute(SectionIds.Documentation);
        Assert.True(vm.HideSectionCommand.CanExecute(null));

        vm.AddItemCommand.Execute(null);
        var drawing = vm.SelectedRow!;
        Assert.Equal(SectionIds.Documentation, drawing.SectionId);
        Assert.Equal("", drawing.Position);

        vm.SelectedRow = vm.Rows.First(r => r.IsHeader && r.SectionId == SectionIds.Documentation);
        Assert.False(vm.HideSectionCommand.CanExecute(null)); // в разделе уже есть строка

        vm.UndoCommand.Execute(null);
        vm.SelectedRow = vm.Rows.First(r => r.IsHeader && r.SectionId == SectionIds.Documentation);
        vm.HideSectionCommand.Execute(null);
        Assert.DoesNotContain(vm.Rows, r => r.SectionId == SectionIds.Documentation);
    }

    [Fact]
    public void Existing_reserve_is_kept_and_numbers_hint_is_given()
    {
        _host.Settings.DefaultReserves[SectionIds.StandardProducts] = new ReserveSettings { Count = 2 };
        var vm = Open();

        vm.InsertSectionCommand.Execute(SectionIds.StandardProducts);

        Assert.Equal(2, vm.Specification.ReserveOverrides.TryGetValue(SectionIds.StandardProducts, out var r) ? r.Count : 2);
        Assert.Equal(2, vm.Rows.Count(x => x.IsReserve && x.SectionId == SectionIds.StandardProducts));
        Assert.DoesNotContain("Перенумеровать", vm.StatusText);
    }
}

public class DrawingFormatTests
{
    private readonly FakeHost _host = new();
    private readonly FakeDialogs _dialogs = new();

    public DrawingFormatTests()
    {
        _host.Components.AddRange(new[]
        {
            new ComponentData { Key = "a.ipt", Kind = ComponentKind.Part, Designation = "А.001", Name = "Корпус", Quantity = 1 },
            new ComponentData { Key = "b.ipt", Kind = ComponentKind.Part, Designation = "А.002", Name = "Ось", Format = "А4", Quantity = 1 },
            new ComponentData { Key = "c.ipt", Kind = ComponentKind.Part, Designation = "А.003", Name = "Вал", Quantity = 1 },
            new ComponentData { Key = "u.iam", Kind = ComponentKind.Assembly, Designation = "Б.001", Name = "Узел", Quantity = 1 },
        });
    }

    private SpecificationViewModel Open()
    {
        var spec = new Specification();
        Core.Update.SpecificationUpdater.Update(spec, _host.Components, SpecificationContext.Create(_host.Settings, spec), DateTime.UtcNow);
        return new SpecificationViewModel(_host, _dialogs, spec, post: action => action());
    }

    [Fact]
    public void Formats_of_part_drawings_are_written_to_parts_only()
    {
        _host.Drawings["a.ipt"] = "А1, А2х3, А2";
        _host.Drawings["b.ipt"] = "А4";
        _host.Drawings["u.iam"] = "А1";
        var vm = Open();

        vm.DrawingFormatsCommand.Execute(null);

        Assert.Equal(new[] { "a.ipt", "b.ipt", "c.ipt" }, _host.DrawingRequests.Single().OrderBy(k => k));
        Assert.Equal("А1, А2х3, А2", vm.Rows.Single(r => r.Name == "Корпус").Format);
        Assert.Equal("А1, А2х3, А2", _host.Properties["a.ipt"][Core.Storage.PropertyNames.Format]);
        Assert.False(_host.Properties.ContainsKey("b.ipt")); // формат уже такой
        Assert.False(_host.Properties.ContainsKey("u.iam"));

        // Вал без чертежа — «БЧ».
        Assert.Equal("БЧ", vm.Rows.Single(r => r.Name == "Вал").Format);
        Assert.Equal("БЧ", _host.Properties["c.ipt"][Core.Storage.PropertyNames.Format]);

        var report = _dialogs.Infos.Single();
        Assert.Contains("Записано форматов: 2. Без изменений: 1.", report);
        Assert.Contains("Чертёж не найден — формат «БЧ» (1)", report);
        Assert.Contains("Вал", report);
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void Part_without_drawing_gets_blank_format_by_setting_and_old_bch_is_cleared()
    {
        _host.Components.Single(c => c.Key == "c.ipt").Format = "БЧ";
        var vm = Open();
        Assert.Equal("БЧ", vm.SelectedPartWithoutDrawing.Id); // по умолчанию

        vm.SelectedPartWithoutDrawing = vm.PartWithoutDrawingOptions.Single(o => o.Id == "");
        Assert.Equal("", _host.Settings.PartWithoutDrawingFormat); // настройка сохранена

        vm.DrawingFormatsCommand.Execute(null);

        Assert.Equal("", vm.Rows.Single(r => r.Name == "Вал").Format);
        Assert.Equal("", _host.Properties["c.ipt"][Core.Storage.PropertyNames.Format]);
        Assert.Contains("Чертёж не найден — формат не указывается (2)", _dialogs.Infos.Single()); // Вал и Корпус

        // Пустой формат детали — не замечание проверки.
        vm.CheckCommand.Execute(null);
        Assert.DoesNotContain(_dialogs.LastCheck?.Items ?? new List<CheckItem>(), i => i.Message.Contains("Вал") && i.Message.Contains("формат"));
    }

    [Fact]
    public void Format_typed_earlier_is_kept_when_drawing_is_not_found()
    {
        _host.Components.Single(c => c.Key == "c.ipt").Format = "А4";
        var vm = Open();
        vm.SetSelectedRows(new[] { vm.Rows.Single(r => r.Name == "Вал"), vm.Rows.Single(r => r.Name == "Ось") });

        vm.DrawingFormatsCommand.Execute(null);

        Assert.Equal("А4", vm.Rows.Single(r => r.Name == "Вал").Format);
        Assert.False(_host.Properties.ContainsKey("c.ipt"));
        Assert.Contains("оставлен формат, заданный раньше (2)", _dialogs.Infos.Single());
    }

    [Fact]
    public void Only_selected_parts_when_several_rows_are_selected_and_cancel_changes_nothing()
    {
        _host.Drawings["a.ipt"] = "А3";
        _host.Drawings["c.ipt"] = "А3";
        var vm = Open();
        vm.SetSelectedRows(new[] { vm.Rows.Single(r => r.Name == "Вал"), vm.Rows.Single(r => r.Name == "Узел") });

        _dialogs.ConfirmAnswer = false;
        vm.DrawingFormatsCommand.Execute(null);
        Assert.Empty(_host.DrawingRequests);

        _dialogs.ConfirmAnswer = true;
        vm.DrawingFormatsCommand.Execute(null);
        Assert.Equal(new[] { "c.ipt" }, _host.DrawingRequests.Single());
        Assert.Equal("", vm.Rows.Single(r => r.Name == "Корпус").Format);
    }
}

public class DocumentRowTests
{
    private readonly FakeHost _host = new();
    private readonly FakeDialogs _dialogs = new();

    private SpecificationViewModel Open()
    {
        _host.Components.Add(new ComponentData { Key = "a.ipt", Kind = ComponentKind.Part, Designation = "А.001", Name = "Корпус", Quantity = 1 });
        var spec = new Specification();
        Core.Update.SpecificationUpdater.Update(spec, _host.Components, SpecificationContext.Create(_host.Settings, spec), DateTime.UtcNow);
        return new SpecificationViewModel(_host, _dialogs, spec, post: action => action());
    }

    [Fact]
    public void Document_added_by_hand_has_no_quantity_and_type_menu_fills_it()
    {
        var vm = Open();
        vm.InsertSectionCommand.Execute(SectionIds.Documentation);
        vm.AddItemCommand.Execute(null);
        var row = vm.SelectedRow!;
        Assert.Equal("", row.Quantity);

        row.Designation = "АБВГ.301234.001";
        vm.SelectedRow = vm.Rows.Single(r => r.RowId == row.RowId);
        Assert.Contains(vm.DocumentTypeOptions, o => o.Id == "ТУ" && o.Name == "ТУ — Технические условия");

        vm.SetDocumentTypeCommand.Execute("СБ");

        var updated = vm.Rows.Single(r => r.RowId == row.RowId);
        Assert.Equal("АБВГ.301234.001СБ", updated.Designation);
        Assert.Equal("Сборочный чертеж", updated.Name);
        Assert.True(vm.CanSetDocumentType);

        vm.SelectedRow = vm.Rows.Single(r => r.Name == "Корпус");
        Assert.False(vm.SetDocumentTypeCommand.CanExecute("СБ")); // не документ
        Assert.False(vm.CanSetDocumentType); // пункта меню нет
    }

    [Fact]
    public void External_document_only_from_documentation_and_shows_its_folder()
    {
        var vm = Open();
        vm.SelectedRow = vm.Rows.Single(r => r.Name == "Корпус");
        Assert.False(vm.CanAddExternalDocument); // в «Деталях» пункта нет

        vm.InsertSectionCommand.Execute(SectionIds.Documentation);
        vm.SelectedRow = vm.Rows.Single(r => r.IsHeader && r.SectionId == SectionIds.Documentation);
        Assert.True(vm.CanAddExternalDocument);

        var file = System.IO.Path.Combine(_host.AssemblyFolder, "Документы", "АБВГ.301234.001ТУ-ТУ-ред.pdf");
        _dialogs.DocumentFiles = new[] { file };
        vm.AddExternalDocumentCommand.Execute(null);

        var row = vm.Rows.Single(r => r.Designation == "АБВГ.301234.001ТУ");
        Assert.Equal(("Технические условия", "", ""), (row.Name, row.Format, row.Quantity));
        Assert.Equal(System.IO.Path.Combine("Документы", "АБВГ.301234.001ТУ-ТУ-ред.pdf"), row.Row!.DocumentFile);

        // Второй раз тот же файл не добавляется.
        vm.AddExternalDocumentCommand.Execute(null);
        Assert.Single(vm.Rows, r => r.Designation == "АБВГ.301234.001ТУ");

        // «Показать файл в папке»: файла пока нет — предупреждение, есть — папка.
        vm.SelectedRow = vm.Rows.Single(r => r.RowId == row.RowId);
        Assert.True(vm.IsDocumentSelected);
        Assert.True(vm.ShowDocumentFileCommand.CanExecute(null));
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
        System.IO.File.WriteAllText(file, "");
        try
        {
            vm.ShowDocumentFileCommand.Execute(null);
            Assert.Equal(file, Assert.Single(_dialogs.ShownInFolder));
        }
        finally
        {
            System.IO.File.Delete(file);
        }
    }
}

public class ResetSectionsTests
{
    private readonly FakeHost _host = new();
    private readonly FakeDialogs _dialogs = new();

    public ResetSectionsTests()
    {
        _host.Components.AddRange(new[]
        {
            new ComponentData { Key = "u.iam", Kind = ComponentKind.Assembly, SectionId = SectionIds.OtherProducts, Designation = "Б.001", Name = "Узел", Quantity = 1 },
            new ComponentData { Key = "a.ipt", Kind = ComponentKind.Part, SectionId = SectionIds.Materials, Name = "Пластина", Quantity = 1 },
            new ComponentData { Key = "b.ipt", Kind = ComponentKind.Part, Designation = "А.002", Name = "Ось", Quantity = 1 },
            new ComponentData { Key = "s.ipt", Kind = ComponentKind.Part, SectionId = SectionIds.StandardProducts, Name = "Шайба 8 ГОСТ 11371-78", Quantity = 1 },
            new ComponentData { Key = "c.ipt", Kind = ComponentKind.Part, IsContentCenter = true, SectionId = SectionIds.OtherProducts, Name = "Болт М8 ГОСТ 7798-70", Quantity = 1 },
        });
        _host.Properties["u.iam"] = new() { [Core.Storage.PropertyNames.Section] = SectionIds.OtherProducts };
        _host.Properties["a.ipt"] = new() { [Core.Storage.PropertyNames.Section] = SectionIds.Materials };
        _host.Properties["s.ipt"] = new() { [Core.Storage.PropertyNames.Section] = SectionIds.StandardProducts };
        _host.Properties["c.ipt"] = new() { [Core.Storage.PropertyNames.Section] = SectionIds.OtherProducts };
    }

    private SpecificationViewModel Open()
    {
        var spec = new Specification();
        Core.Update.SpecificationUpdater.Update(spec, _host.Components, SpecificationContext.Create(_host.Settings, spec), DateTime.UtcNow);
        return new SpecificationViewModel(_host, _dialogs, spec, post: action => action());
    }

    private static string Section(SpecificationViewModel vm, string name) => vm.Rows.Single(r => r.Name == name).SectionId;

    [Fact]
    public void All_sections_are_reset_except_standard_products()
    {
        var vm = Open();
        var oseRow = vm.Specification.Rows.Single(r => r.Get(SpecColumn.Name) == "Ось");
        Core.Editing.SpecificationEditor.SetCell(vm.Specification, SpecificationContext.Create(_host.Settings, vm.Specification),
            oseRow, SpecColumn.Section, SectionIds.Kits); // ручной перенос в этой спецификации
        vm.SearchText = "";

        vm.ResetSectionsCommand.Execute(null);

        Assert.Equal(SectionIds.AssemblyUnits, Section(vm, "Узел"));
        Assert.Equal(SectionIds.Parts, Section(vm, "Пластина"));
        Assert.Equal(SectionIds.Parts, Section(vm, "Ось"));
        Assert.Equal("", _host.Properties["u.iam"][Core.Storage.PropertyNames.Section]);

        // Стандартные изделия не тронуты.
        Assert.Equal(SectionIds.StandardProducts, _host.Properties["s.ipt"][Core.Storage.PropertyNames.Section]);
        Assert.Equal(SectionIds.StandardProducts, Section(vm, "Шайба 8 ГОСТ 11371-78"));

        // Изделие Content Center — стандартное по типу: не трогается, даже если перенесено в прочие.
        Assert.Equal(SectionIds.OtherProducts, Section(vm, "Болт М8 ГОСТ 7798-70"));
        Assert.Equal(SectionIds.OtherProducts, _host.Properties["c.ipt"][Core.Storage.PropertyNames.Section]);
        Assert.Contains("Изменено файлов: 2", _dialogs.Infos.Single());
    }

    [Fact]
    public void Context_menu_resets_only_selected_row_and_cancel_changes_nothing()
    {
        var vm = Open();
        vm.SelectedRow = vm.Rows.Single(r => r.Name == "Узел");

        _dialogs.ConfirmAnswer = false;
        vm.ResetSectionsCommand.Execute("selected");
        Assert.Equal(SectionIds.OtherProducts, Section(vm, "Узел"));

        _dialogs.ConfirmAnswer = true;
        vm.ResetSectionsCommand.Execute("selected");
        Assert.Equal(SectionIds.AssemblyUnits, Section(vm, "Узел"));
        Assert.Equal(SectionIds.Materials, Section(vm, "Пластина"));

        vm.SelectedRow = vm.Rows.Single(r => r.Name == "Шайба 8 ГОСТ 11371-78");
        Assert.False(vm.ResetSectionsCommand.CanExecute("selected"));
    }
}

public class ShowInFolderTests
{
    private readonly FakeHost _host = new();
    private readonly FakeDialogs _dialogs = new();

    private SpecificationViewModel Open()
    {
        _host.Components.AddRange(new[]
        {
            new ComponentData { Key = "drawing:Привод.idw", Kind = ComponentKind.Document, Designation = "А.000СБ" },
            new ComponentData { Key = "a.ipt", Kind = ComponentKind.Part, Designation = "А.001", Name = "Корпус", Quantity = 1 },
        });
        var spec = new Specification();
        Core.Update.SpecificationUpdater.Update(spec, _host.Components, SpecificationContext.Create(_host.Settings, spec), DateTime.UtcNow);
        return new SpecificationViewModel(_host, _dialogs, spec, post: action => action());
    }

    [Fact]
    public void Component_file_and_its_drawing_are_shown_in_folder()
    {
        var vm = Open();
        var part = Path.GetTempFileName();
        try
        {
            _host.Files["a.ipt"] = part;
            _host.Drawings["a.ipt"] = "А3";
            vm.SelectedRow = vm.Rows.Single(r => r.Name == "Корпус");

            vm.ShowComponentFileCommand.Execute(null);
            vm.ShowDrawingFileCommand.Execute(null);

            Assert.Equal(new[] { part, "a.ipt.idw" }, _dialogs.ShownInFolder);
        }
        finally
        {
            File.Delete(part);
        }
    }

    [Fact]
    public void Missing_file_or_drawing_is_reported_and_drawing_row_has_no_drawing_command()
    {
        var vm = Open();
        vm.SelectedRow = vm.Rows.Single(r => r.Name == "Корпус");

        vm.ShowComponentFileCommand.Execute(null);
        vm.ShowDrawingFileCommand.Execute(null);

        Assert.Empty(_dialogs.ShownInFolder);
        Assert.Equal(2, _dialogs.Warnings.Count);
        Assert.Contains("Чертёж компонента «А.001 Корпус» не найден", _dialogs.Warnings[1]);

        // У документа — один пункт «Показать документ в папке».
        vm.SelectedRow = vm.Rows.Single(r => r.Name == "Сборочный чертеж");
        Assert.True(vm.IsDocumentSelected);
        Assert.False(vm.ShowComponentFileCommand.CanExecute(null));
        Assert.False(vm.ShowDrawingFileCommand.CanExecute(null));
        Assert.True(vm.ShowDocumentFileCommand.CanExecute(null));
        var drawing = Path.GetTempFileName();
        try
        {
            _host.Files["drawing:Привод.idw"] = drawing;
            vm.ShowDocumentFileCommand.Execute(null);
            Assert.Equal(drawing, Assert.Single(_dialogs.ShownInFolder));
        }
        finally
        {
            File.Delete(drawing);
        }

        vm.SelectedRow = vm.Rows.First(r => r.IsHeader);
        Assert.False(vm.ShowComponentFileCommand.CanExecute(null));
    }
}

public class ConfirmationViewModelTests
{
    [Fact]
    public void Action_needs_the_check_box()
    {
        var vm = new ConfirmationViewModel("Авторазделы", "Будут изменены файлы.", "Понимаю", "Выполнить");
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Assert.False(vm.IsConfirmed);
        vm.IsConfirmed = true;

        Assert.True(vm.IsConfirmed);
        Assert.Equal(new[] { nameof(ConfirmationViewModel.IsConfirmed) }, changed);
        Assert.Equal("Выполнить", vm.ActionText);
    }
}

public class PageMarkTests
{
    private readonly FakeHost _host = new();
    private readonly FakeDialogs _dialogs = new();

    private SpecificationViewModel Open(int parts)
    {
        for (var i = 1; i <= parts; i++)
        {
            _host.Components.Add(new ComponentData { Key = $"p{i}.ipt", Kind = ComponentKind.Part, Designation = $"А.{i:000}", Name = $"Деталь {i}", Format = "А4", Quantity = 1 });
        }

        _host.Components.Add(new ComponentData { Key = "u.iam", Kind = ComponentKind.Assembly, Designation = "Б.001", Name = "Узел", Quantity = 1 });
        var spec = new Specification();
        Core.Update.SpecificationUpdater.Update(spec, _host.Components, SpecificationContext.Create(_host.Settings, spec), DateTime.UtcNow);
        return new SpecificationViewModel(_host, _dialogs, spec, post: action => action());
    }

    [Fact]
    public void Sheet_boundaries_are_marked_like_in_pdf()
    {
        var vm = Open(40);

        Assert.True(vm.PageCount >= 2);
        var start = vm.Rows.First(r => r.IsPageStart);
        Assert.Equal(2, start.PageStart);
        Assert.Equal("Лист 2", start.PageStartText);
        Assert.Equal(vm.PageCount - 1, vm.Rows.Count(r => r.IsPageStart));
        Assert.Contains($"листов PDF: {vm.PageCount}", vm.StatusText);
    }

    [Fact]
    public void Section_can_start_a_new_sheet_from_the_context_menu()
    {
        var vm = Open(3);
        Assert.Equal(1, vm.PageCount);

        var header = vm.Rows.Single(r => r.IsHeader && r.SectionId == SectionIds.Parts);
        vm.SelectedRow = header;
        Assert.True(vm.CanSetNewPage);
        Assert.False(vm.SelectedStartsNewPage); // по умолчанию выключено

        vm.SelectedStartsNewPage = true;

        Assert.Equal(2, vm.PageCount);
        Assert.Equal(2, vm.Rows.Single(r => r.IsHeader && r.SectionId == SectionIds.Parts).PageStart);
        Assert.True(vm.IsDirty);

        vm.SelectedRow = vm.Rows.First(r => r.Row is not null);
        Assert.False(vm.CanSetNewPage); // только у заголовков
    }

    [Fact]
    public void Group_can_start_a_new_sheet()
    {
        var vm = Open(3);
        var spec = vm.Specification;
        var group = Core.Editing.SpecificationEditor.AddGroup(spec, "Изделия, устанавливаемые по чертежу А.000МЭ");
        Core.Editing.SpecificationEditor.MoveToGroup(spec, SpecificationContext.Create(_host.Settings, spec), spec.Rows.First(r => r.Kind == RowKind.Component), group.Id);
        vm = new SpecificationViewModel(_host, _dialogs, spec, post: action => action());

        vm.SelectedRow = vm.Rows.Single(r => r.IsGroupHeader);
        vm.SelectedStartsNewPage = true;

        Assert.True(vm.Specification.StartsNewPage(group.Id, ""));
        Assert.Equal(2, vm.Rows.Single(r => r.IsGroupHeader).PageStart);
    }
}
