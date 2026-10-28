using System.Collections.ObjectModel;
using System.Windows.Input;
using InventorGostSpec.Core.Checking;
using InventorGostSpec.Core.Editing;
using InventorGostSpec.Core.Model;
using InventorGostSpec.Core.Positions;
using InventorGostSpec.Core.Storage;
using InventorGostSpec.Core.Update;
using InventorGostSpec.Core.Versions;
using InventorGostSpec.Core.View;

namespace InventorGostSpec.UI.ViewModels;

// Выгрузка: Excel, PDF, основная надпись.
public sealed partial class SpecificationViewModel
{
    /// <summary>Экспорт в Excel для ручной правки и последующего вывода в PDF по ГОСТ.</summary>
    public ICommand ExportExcelCommand { get; }

    /// <summary>PDF по ГОСТ: выгрузка во временный Excel и вызов утилиты PDF.</summary>
    public ICommand ExportPdfCommand { get; }

    /// <summary>Настройки утилиты PDF: шрифт, размеры шрифтов, сужение текста.</summary>
    public ICommand PdfSettingsCommand { get; }

    /// <summary>Основная надпись: подписи, организация, литера, изменения.</summary>
    public ICommand EditTitleBlockCommand { get; }

    /// <summary>
    /// Экспорт текущего состояния окна (в том числе несохранённого) в Excel.
    /// Резервные строки выгружаются, если включено «Показывать резерв».
    /// Лист «Изменения» переносится из заменяемого файла.
    /// </summary>
    private void ExportExcel()
    {
        CommitEdits?.Invoke();
        var info = _host.GetExportInfo();
        var path = _dialogs.AskExportPath(info.DefaultPath);
        if (path is null)
        {
            return;
        }

        try
        {
            ApplyTitleData(info.TitleBlock, path);

            Excel.ExcelExporter.Export(Specification, _context, info.TitleBlock, path);
        }
        catch (System.IO.IOException ex)
        {
            _dialogs.Warning($"Не удалось записать файл. Возможно, он открыт в Excel.\n\n{ex.Message}");
            return;
        }

        var sheets = MarkSheets(path);
        if (_dialogs.Confirm($"Спецификация выгружена:\n{path}\n{sheets}\n\nОткрыть файл?"))
        {
            _dialogs.OpenFile(path);
        }
    }

    /// <summary>
    /// PDF по ГОСТ из текущего состояния окна. Таблица изменений берётся из файла Excel по умолчанию
    /// (рядом со сборкой), если он есть: там её ведут вручную.
    /// </summary>
    private void ExportPdf()
    {
        CommitEdits?.Invoke();

        // PDF — документ для выпуска: явные ошибки (нет позиции, повтор позиции, нет наименования) показываем заранее.
        var errors = SpecificationChecker.Check(Specification, _context).Where(i => i.Severity == IssueSeverity.Error).ToList();
        if (errors.Count > 0 && !_dialogs.Confirm(
                $"В спецификации ошибки: {errors.Count}.\n\n" +
                string.Join("\n", errors.Take(5).Select(e => "• " + e.Message)) +
                (errors.Count > 5 ? "\n…" : "") +
                "\n\nВсё равно создать PDF? Подробно — кнопка «Проверка»."))
        {
            return;
        }

        var info = _host.GetExportInfo();
        var path = _dialogs.AskPdfPath(System.IO.Path.ChangeExtension(info.DefaultPath, ".pdf"));
        if (path is null)
        {
            return;
        }

        try
        {
            ApplyTitleData(info.TitleBlock, info.DefaultPath);
        }
        catch (System.IO.IOException)
        {
            // Файл Excel недоступен — PDF без изменений.
        }

        var excel = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"InventorGostSpec-{Guid.NewGuid():N}.xlsx");
        try
        {
            Excel.ExcelExporter.Export(Specification, _context, info.TitleBlock, excel);
            if (_host.BuildPdf(excel, path) is { } error)
            {
                _dialogs.Warning($"PDF не создан.\n\n{error}");
                return;
            }
        }
        catch (System.IO.IOException ex)
        {
            _dialogs.Warning($"Не удалось записать временный файл.\n\n{ex.Message}");
            return;
        }
        finally
        {
            try
            {
                System.IO.File.Delete(excel);
            }
            catch (Exception)
            {
                // Временный файл удалит система.
            }
        }

        if (_dialogs.Confirm($"PDF создан:\n{path}\n\nОткрыть файл?"))
        {
            _dialogs.OpenFile(path);
        }
    }

    private void OpenPdfSettings()
    {
        var excel = _host.GetExportInfo().DefaultPath;
        if (_host.OpenPdfSettings(System.IO.File.Exists(excel) ? excel : null) is { } error)
        {
            _dialogs.Warning(error);
        }
    }

    /// <summary>
    /// Разметка листов PDF в выгруженном файле (жирная линия на границе листов, колонка «Лист PDF»)
    /// с теми же настройками шрифта, что у утилиты PDF. Возвращает строку для сообщения.
    /// </summary>
    private static string MarkSheets(string path)
    {
        try
        {
            var count = Pdf.ExcelSheetMarker.Mark(path, Pdf.PdfSettings.Load().ToOptions());
            return $"Листов PDF: {count} (границы листов отмечены жирной линией).";
        }
        catch (Exception ex)
        {
            return $"Листы не размечены: {ex.Message}";
        }
    }

    /// <summary>
    /// Литера, шрифт наименования и изменения: из спецификации, если их вводили в окне «Основная надпись»,
    /// иначе — из прежнего файла Excel (как раньше).
    /// </summary>
    private void ApplyTitleData(TitleBlock title, string excelPath)
    {
        if (Specification.Title.IsSet)
        {
            Specification.Title.ApplyTo(title);
            if (title.FirstUse.Length == 0)
            {
                KeepFirstUse(title, excelPath);
            }

            return;
        }

        KeepManualTitleData(title, excelPath);
    }

    private static void KeepFirstUse(TitleBlock title, string path)
    {
        try
        {
            if (Excel.ExcelReader.ReadManualTitleData(path) is { } previous)
            {
                title.FirstUse = previous.FirstUse;
            }
        }
        catch (Exception)
        {
            // Нет файла Excel — «Перв. примен.» пустое.
        }
    }

    private void EditTitleBlock()
    {
        CommitEdits?.Invoke();
        if (TryHost(() => _host.GetExportInfo()) is not { } info)
        {
            _dialogs.Warning("Не удалось прочитать iProperties сборки.");
            return;
        }

        var title = info.TitleBlock;
        try
        {
            ApplyTitleData(title, info.DefaultPath);
        }
        catch (System.IO.IOException)
        {
            // Файл Excel занят — изменения из него не подтянулись.
        }

        var before = (title.Developer, title.Checker, title.NormControl, title.Approver, title.Organization, title.FirstUse);
        var viewModel = new TitleBlockViewModel(title);
        if (!_dialogs.EditTitleBlock(viewModel))
        {
            return;
        }

        var properties = viewModel.ToProperties();
        if ((properties.Developer, properties.Checker, properties.NormControl, properties.Approver, properties.Organization, properties.FirstUse) != before)
        {
            try
            {
                _host.WriteTitleProperties(properties);
            }
            catch (Exception ex)
            {
                _host.ReportError(ex);
                _dialogs.Warning($"Не удалось записать iProperties сборки (файл только для чтения?).\n\n{ex.Message}");
            }
        }

        Specification.Title = viewModel.ToTitleData();
        Changed(rebuild: false);
    }

    /// <summary>
    /// Данные, которые ведут вручную в файле Excel (изменения, высота шрифта наименования), переносятся из прежнего
    /// файла. Файл недоступен — исключение IOException; не книга Excel — ничего не переносится.
    /// </summary>
    private static void KeepManualTitleData(TitleBlock title, string path)
    {
        TitleBlock? previous;
        try
        {
            previous = Excel.ExcelReader.ReadManualTitleData(path);
        }
        catch (Exception ex) when (ex is not System.IO.IOException)
        {
            return;
        }

        if (previous is null)
        {
            return;
        }

        if (title.Changes.Count == 0)
        {
            title.Changes = previous.Changes;
        }

        title.NameTextHeight ??= previous.NameTextHeight;
        if (title.Letter.Length == 0)
        {
            title.Letter = previous.Letter;
        }

        // «Перв. примен.» берётся из свойства сборки; не задано — остаётся вписанное в Excel.
        if (title.FirstUse.Length == 0)
        {
            title.FirstUse = previous.FirstUse;
        }
    }
}
