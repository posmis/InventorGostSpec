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

// Документы и файлы: сторонние документы, тип документа, «Показать … в папке», форматы чертежей деталей.
public sealed partial class SpecificationViewModel
{
    /// <summary>Пункт «Тип документа» — только для строк раздела «Документация».</summary>
    public bool CanSetDocumentType => TargetRows.Any(IsDocumentRow);

    /// <summary>
    /// Форматы чертежей деталей (раздел «Детали»): выбранных строк или всех деталей. Форматы всех листов чертежа
    /// записываются в iProperty SPEC_Format детали.
    /// </summary>
    public ICommand DrawingFormatsCommand { get; }

    /// <summary>Варианты графы «Формат» у детали без чертежа.</summary>
    public IReadOnlyList<Option> PartWithoutDrawingOptions { get; } = new[]
    {
        new Option(SheetFormats.NoDrawing, "БЧ"),
        new Option("", "пусто"),
    };

    /// <summary>
    /// Формат детали без чертежа (только деталей): «БЧ» или пусто. Общая настройка — действует в «Форматах»,
    /// «Авторазделах» и проверке.
    /// </summary>
    public Option SelectedPartWithoutDrawing
    {
        get => PartWithoutDrawingOptions.First(o => o.Id == _settings.NoDrawingFormat());
        set
        {
            if (value is null || value.Id == _settings.NoDrawingFormat())
            {
                return;
            }

            _settings.PartWithoutDrawingFormat = value.Id;
            _host.SaveSettings(_settings);
            OnPropertyChanged();
        }
    }

    /// <summary>Папка документа строки «Документации»: найденного чертежа или стороннего файла.</summary>
    public ICommand ShowDocumentFileCommand { get; }

    /// <summary>
    /// Выбрана строка «Документации»: в меню один пункт «Показать документ в папке»
    /// вместо пунктов файла компонента и чертежа.
    /// </summary>
    public bool IsDocumentSelected => SelectedRow?.Row is { } row && IsDocumentRow(row);

    public bool IsNotDocumentSelected => !IsDocumentSelected;

    /// <summary>Сторонний документ (PDF и т. п.) в «Документацию» — из меню строк этого раздела.</summary>
    public ICommand AddExternalDocumentCommand { get; }

    /// <summary>Выбрана строка или заголовок раздела «Документация».</summary>
    public bool CanAddExternalDocument =>
        SelectedRow is { IsGroupHeader: false } selected && ComponentSnapshot.IsDocument(selected.SectionId) &&
        !VersionSync.IsReadOnly(Specification, selected.GroupId);

    /// <summary>Найти чертёж компонента выбранной строки и показать его в Проводнике.</summary>
    public ICommand ShowDrawingFileCommand { get; }

    /// <summary>Тип документа (параметр — код «СБ», «ТУ»…) выбранных строк «Документации»: код в обозначении и наименование.</summary>
    public ICommand SetDocumentTypeCommand { get; }

    /// <summary>Пункты меню «Тип документа»: «СБ — Сборочный чертеж».</summary>
    public IReadOnlyList<Option> DocumentTypeOptions { get; } = Core.Sorting.DocumentCodes.Common
        .Select(code => new Option(code, $"{code} — {Core.Sorting.DocumentCodes.NameOf(code)}"))
        .ToList();

    /// <summary>Полный путь стороннего документа у выбранной строки; <c>null</c> — строка не связана с файлом.</summary>
    private string? SelectedDocumentFile() =>
        SelectedRow?.Row is { Kind: RowKind.ManualItem, DocumentFile.Length: > 0 } row
            ? System.IO.Path.GetFullPath(System.IO.Path.Combine(_host.AssemblyFolder, row.DocumentFile))
            : null;

    private void AddExternalDocument()
    {
        var folder = _host.AssemblyFolder;
        var files = _dialogs.AskDocumentFiles(folder);
        if (files is null || files.Count == 0)
        {
            return;
        }

        var existing = Specification.Rows
            .Where(r => r.DocumentFile.Length > 0)
            .Select(r => System.IO.Path.GetFullPath(System.IO.Path.Combine(folder, r.DocumentFile)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var afterRowId = SelectedRow?.RowId;
        var groupId = SelectedRow?.GroupId ?? "";
        var skipped = new List<string>();
        SpecRow? last = null;
        foreach (var file in files)
        {
            if (!existing.Add(System.IO.Path.GetFullPath(file)))
            {
                skipped.Add(System.IO.Path.GetFileName(file));
                continue;
            }

            last = SpecificationEditor.AddExternalDocument(Specification, _context, StoredPath(folder, file), afterRowId, groupId);
            last.DocumentHash = ExternalDocuments.Hash(file);
            afterRowId = last.Id;
        }

        if (last is not null)
        {
            // Формат стороннего документа неизвестен — вписывается вручную.
            Changed(rebuild: true, selectRowId: last.Id);
            RequestEdit(last.Id, SpecColumn.Format);
        }

        if (skipped.Count > 0)
        {
            _dialogs.Info("Эти документы уже есть в спецификации:\n" + string.Join("\n", skipped));
        }
    }

    /// <summary>
    /// Путь документа для хранения: относительно папки сборки (на другом ПК с той же структурой папок, например в облаке,
    /// он найдётся), на другом диске — полный.
    /// </summary>
    private static string StoredPath(string folder, string file) =>
        string.Equals(System.IO.Path.GetPathRoot(folder), System.IO.Path.GetPathRoot(file), StringComparison.OrdinalIgnoreCase)
            ? System.IO.Path.GetRelativePath(folder, file)
            : file;

    private void ShowDocumentFile()
    {
        var document = SelectedDocumentFile()
            ?? (SelectedComponent() is { } row ? TryHost<string>(() => _host.GetComponentFile(row.ComponentKey!)) : null);
        if (document is null || !System.IO.File.Exists(document))
        {
            _dialogs.Warning(document is null ? "Строка документа не связана с файлом." : $"Файл документа не найден:\n{document}");
            return;
        }

        _dialogs.ShowInFolder(document);
    }

    /// <summary>Отмечает сторонние документы, файлы которых изменились (уведомление, ссылки не меняются).</summary>
    private void CheckDocumentFiles() =>
        TryHostAction(() => ExternalDocuments.CheckFiles(Specification, _host.AssemblyFolder));

    private void ShowComponentFile()
    {
        if (SelectedComponent() is not { } row)
        {
            return;
        }

        var file = TryHost<string>(() => _host.GetComponentFile(row.ComponentKey!));
        if (file is null || !System.IO.File.Exists(file))
        {
            _dialogs.Warning($"Файл компонента «{Describe(row)}» не найден{(file is null ? " в сборке" : $":\n{file}")}.");
            return;
        }

        _dialogs.ShowInFolder(file);
    }

    private void ShowDrawingFile()
    {
        if (SelectedComponent() is not { } row)
        {
            return;
        }

        var found = TryHost(() => _host.FindDrawingFormats(new[] { row.ComponentKey! }));
        if (found is null || !found.TryGetValue(row.ComponentKey!, out var drawing) || drawing.DrawingPath is null)
        {
            _dialogs.Warning(
                $"Чертёж компонента «{Describe(row)}» не найден в папках проекта.\n\n" +
                "Чертёж ищется по имени (как у модели или по обозначению) и по содержимому; подробности — в журнале аддина.");
            return;
        }

        _dialogs.ShowInFolder(drawing.DrawingPath);
    }

    private static bool IsDocumentRow(SpecRow row) => row.Kind != RowKind.Note && ComponentSnapshot.IsDocument(row.SectionId);

    private void SetDocumentType(object? parameter)
    {
        if (parameter is not string code)
        {
            return;
        }

        foreach (var row in TargetRows.Where(IsDocumentRow).ToList())
        {
            SpecificationEditor.SetDocumentType(Specification, _context, row, code);
        }

        Changed(rebuild: true);
    }

    /// <summary>Строка детали, у которой есть модель в сборке.</summary>
    private static bool IsPartWithModel(SpecRow row) =>
        CanEditComponent(row) && string.Equals(row.SectionId, Core.Sections.SectionIds.Parts, StringComparison.OrdinalIgnoreCase);

    private void FindDrawingFormats()
    {
        CommitEdits?.Invoke();
        var rows = (_selectedRows.Count > 1 ? TargetRows : Specification.Rows).Where(IsPartWithModel).ToList();
        if (rows.Count == 0)
        {
            _dialogs.Info("Среди выбранных строк нет деталей (раздел «Детали»).");
            return;
        }

        var scope = _selectedRows.Count > 1 ? "выбранных деталей" : "всех деталей спецификации";
        if (!_dialogs.Confirm(
                $"Найти чертежи {scope} ({rows.Count}) и записать форматы их листов в свойства деталей (SPEC_Format)?\n\n" +
                "Несколько форматов записываются через запятую в порядке уменьшения («А1, А2х3, А2»), " +
                (_settings.NoDrawingFormat().Length > 0
                    ? "детали без чертежа — «БЧ» (если формат не был задан). "
                    : "у деталей без чертежа формат не указывается (прежний «БЧ» убирается). ") +
                "Изменятся файлы деталей — после этого сохраните сборку. Детали без чертежа не меняются."))
        {
            return;
        }

        IReadOnlyDictionary<string, DrawingFormat> found;
        try
        {
            found = _host.FindDrawingFormats(rows.Select(r => r.ComponentKey!).ToList());
        }
        catch (Exception ex)
        {
            _host.ReportError(ex);
            _dialogs.Warning($"Не удалось найти чертежи: {ex.Message}");
            return;
        }

        var written = new List<string>();
        var same = 0;
        var noDrawing = new List<string>();
        var keptWithoutDrawing = new List<string>();
        var unknown = new List<string>();
        var failed = new List<string>();
        foreach (var row in rows)
        {
            string format;
            if (!found.TryGetValue(row.ComponentKey!, out var result) || result.DrawingPath is null)
            {
                // Чертежа нет — «БЧ» (ГОСТ 2.106) или пусто, по настройке; формат, вписанный вручную, не затираем.
                if (row.Source.Format.Length > 0 && row.Source.Format != SheetFormats.NoDrawing)
                {
                    keptWithoutDrawing.Add($"{Describe(row)}: {row.Source.Format}");
                    continue;
                }

                noDrawing.Add(Describe(row));
                format = _settings.NoDrawingFormat();
            }
            else if (result.Format.Length == 0)
            {
                unknown.Add($"{Describe(row)} ({System.IO.Path.GetFileName(result.DrawingPath)})");
                continue;
            }
            else
            {
                format = result.Format;
            }

            if (format == row.Source.Format)
            {
                same++;
                continue;
            }

            try
            {
                var data = _host.WriteComponentProperties(row.ComponentKey!, new Dictionary<string, string> { [PropertyNames.Format] = format });
                SpecificationEditor.RefreshComponent(Specification, _context, row, data);
                written.Add($"{Describe(row)}: {format}");
            }
            catch (Exception ex)
            {
                _host.ReportError(ex);
                failed.Add($"{Describe(row)}: {ex.Message}");
            }
        }

        Changed(rebuild: true);

        static string List(IReadOnlyList<string> items) =>
            string.Join("\n", items.Take(10).Select(i => "• " + i)) + (items.Count > 10 ? $"\n… и ещё {items.Count - 10}" : "");

        var report = $"Записано форматов: {written.Count}. Без изменений: {same}.";
        if (written.Count > 0)
        {
            report += "\n" + List(written);
        }

        if (noDrawing.Count > 0)
        {
            var noDrawingText = _settings.NoDrawingFormat() is { Length: > 0 } text ? $"формат «{text}»" : "формат не указывается";
            report += $"\n\nЧертёж не найден — {noDrawingText} ({noDrawing.Count}):\n" + List(noDrawing);
        }

        if (keptWithoutDrawing.Count > 0)
        {
            report += $"\n\nЧертёж не найден, оставлен формат, заданный раньше ({keptWithoutDrawing.Count}):\n" + List(keptWithoutDrawing);
        }

        if (unknown.Count > 0)
        {
            report += $"\n\nФормат листов не распознан ({unknown.Count}):\n" + List(unknown);
        }

        if (failed.Count > 0)
        {
            report += $"\n\nНе удалось записать ({failed.Count}):\n" + List(failed);
        }

        var overridden = rows.Count(r => r.IsOverridden(SpecColumn.Format));
        if (overridden > 0)
        {
            report += $"\n\nУ {overridden} строк формат изменён вручную в спецификации — показывается ручное значение " +
                      "(«Вернуть значение из компонента» в контекстном меню).";
        }

        _dialogs.Info(report);
    }
}
