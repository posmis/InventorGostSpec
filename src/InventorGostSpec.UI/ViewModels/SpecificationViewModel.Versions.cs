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

// Групповая спецификация исполнений (ГОСТ 2.113, вариант А): исполнения, переменные данные, «Заменяет поз.».
public sealed partial class SpecificationViewModel
{
    /// <summary>
    /// Раздел «Переменные данные для исполнений» и без исполнений: основное исполнение, в которое можно переносить
    /// строки. При добавленных исполнениях выключить нельзя.
    /// </summary>
    public bool ShowVariableData
    {
        get => VersionSync.IsShown(Specification);
        set
        {
            if (value == ShowVariableData)
            {
                return;
            }

            var designation = TryHost(() => _host.GetExportInfo())?.TitleBlock.Designation ?? "";
            if (!VersionSync.SetShown(Specification, _context, value, designation.Length > 0 ? designation : _host.DocumentName))
            {
                _dialogs.Info("В спецификации есть исполнения — раздел «Переменные данные для исполнений» нужен. " +
                              "Чтобы его убрать, уберите исполнения (ПКМ на заголовке исполнения).");
            }

            OnPropertyChanged();
            Changed(rebuild: true);
        }
    }

    /// <summary>Добавить исполнение (сборку-исполнение) — групповая спецификация по ГОСТ 2.113, вариант А.</summary>
    public ICommand AddVersionCommand { get; }

    /// <summary>Убрать исполнение (на заголовке исполнения).</summary>
    public ICommand RemoveVersionCommand { get; }

    /// <summary>«Заменяет поз. …» у строки другого исполнения: параметр — номер позиции, пусто — не заменяет.</summary>
    public ICommand SetReplacesCommand { get; }

    public bool IsVersionItemSelected => SelectedRow?.IsVersionItem == true;

    /// <summary>
    /// В сборке-исполнении: номера позиций из групповой спецификации головной сборки — чтобы BOM и выноски на чертеже
    /// исполнения совпадали с групповой спецификацией.
    /// </summary>
    public ICommand PullHeadPositionsCommand { get; }

    /// <summary>
    /// Чьи номера может взять строка исполнения: составные части основного исполнения и предыдущих исполнений.
    /// </summary>
    public IReadOnlyList<Option> ReplacesOptions
    {
        get
        {
            if (SelectedRow?.Row is not { Kind: RowKind.VersionItem } item)
            {
                return Array.Empty<Option>();
            }

            var options = new List<Option> { new("", "Не заменяет (свой номер)") };
            var seen = new HashSet<int>();
            var own = VersionSync.VersionOf(Specification, item.GroupId)?.Id;
            foreach (var group in VersionSync.Versions(Specification).TakeWhile(g => g.Id != own).ToList())
            {
                var scope = VersionSync.Scope(Specification, group).ToHashSet();
                foreach (var row in Specification.Rows.Where(r => scope.Contains(r.GroupId) && r.Position.HasValue && !r.InheritsPosition && r.ComponentKey is not null))
                {
                    if (seen.Add(row.Position!.Value))
                    {
                        options.Add(new Option(row.ComponentKey!, $"{row.Position} — {Describe(row)}"));
                    }
                }
            }

            return options;
        }
    }

    /// <summary>Сообщение, если эта сборка — исполнение другой (пометку записывает головная спецификация).</summary>
    public string? VersionNotice { get; private set; }

    /// <summary>
    /// Сравнивает состав головной сборки и сборок-исполнений и раскладывает строки по постоянным и переменным данным.
    /// Сборку-исполнение не удалось прочитать — раскладка не меняется (предупреждение).
    /// </summary>
    private void SyncVersions(IReadOnlyList<ComponentData> head)
    {
        if (!VersionSync.HasVersions(Specification))
        {
            return;
        }

        var versions = new Dictionary<string, VersionComponents>();
        var failed = new List<string>();
        foreach (var group in VersionSync.Versions(Specification).Where(g => !g.IsBaseVersion))
        {
            var data = TryHost(() => _host.ReadVersion(VersionPath(group)));
            if (data is null)
            {
                failed.Add($"{group.Title} ({VersionPath(group)})");
                continue;
            }

            group.Title = data.Designation.Length > 0 ? data.Designation : group.Title;
            versions[group.Id] = new VersionComponents(data.Components, data.GroupTitles);
        }

        if (failed.Count > 0)
        {
            _dialogs.Warning("Не удалось прочитать сборки-исполнения — переменные данные не обновлены:\n" + string.Join("\n", failed));
            return;
        }

        if (TryHost(() => _host.GetExportInfo()) is { } info && VersionSync.Base(Specification) is { } baseVersion &&
            info.TitleBlock.Designation.Length > 0)
        {
            baseVersion.Title = info.TitleBlock.Designation;
        }

        VersionSync.Sync(Specification, _context, head, versions);
    }

    private string VersionPath(SpecGroup group) =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(_host.AssemblyFolder, group.VersionFile));

    /// <summary>Исполнение выбранного заголовка или строки.</summary>
    private SpecGroup? SelectedVersion() =>
        SelectedRow is { } selected ? _context.FindGroup(selected.GroupId) is { IsVersion: true } group ? group : null : null;

    private void AddVersion()
    {
        CommitEdits?.Invoke();
        var path = _dialogs.AskVersionFile(_host.AssemblyFolder);
        if (path is null)
        {
            return;
        }

        var folder = _host.AssemblyFolder;
        var relative = StoredPath(folder, path);
        if (TryHost(() => _host.GetExportInfo()) is not { } info)
        {
            return;
        }

        var data = TryHost(() => _host.ReadVersion(path));
        if (data is null)
        {
            _dialogs.Warning($"Не удалось прочитать сборку-исполнение:\n{path}");
            return;
        }

        if (string.Equals(System.IO.Path.GetFullPath(path), System.IO.Path.GetFullPath(System.IO.Path.Combine(folder, _host.DocumentName)), StringComparison.OrdinalIgnoreCase))
        {
            _dialogs.Warning("Это сама головная сборка. Выберите сборку-исполнение.");
            return;
        }

        var designation = data.Designation.Length > 0 ? data.Designation : System.IO.Path.GetFileNameWithoutExtension(path);
        var group = VersionSync.AddVersion(Specification, info.TitleBlock.Designation, designation, relative);
        try
        {
            _host.MarkVersion(path, info.TitleBlock.Designation);
        }
        catch (Exception ex)
        {
            _host.ReportError(ex);
            _dialogs.Warning($"Не удалось записать в сборку-исполнение пометку об исполнении (файл только для чтения?).\n\n{ex.Message}");
        }

        SyncVersions(_host.ReadComponents());

        // Часть строк ушла в переменные данные — в постоянных остались пропуски номеров. По ГОСТ 2.113 переменные данные
        // основного исполнения продолжают номера постоянных: предлагаем перенумеровать.
        if (_dialogs.Confirm(
                $"Исполнение «{group.Title}» добавлено.\n\nПеренумеровать позиции по порядку: постоянные данные, затем " +
                "переменные данные основного исполнения и новые составные части исполнений (ГОСТ 2.113)?\n\n" +
                "Номера изменятся — потом обновите позиции в BOM и выноски."))
        {
            PositionAllocator.RenumberAll(Specification, _context);
        }

        Changed(rebuild: true);
        OnPropertyChanged(nameof(ShowVariableData));
        SelectedRow = Rows.FirstOrDefault(r => r.IsGroupHeader && r.GroupId == group.Id);
    }

    private void RemoveVersion()
    {
        if (SelectedVersion() is not { IsBaseVersion: false } group ||
            !_dialogs.Confirm($"Убрать исполнение «{group.Title}» из спецификации?\n\nЕго строки удалятся; если исполнений не останется, " +
                              "переменные данные основного исполнения вернутся в постоянные данные."))
        {
            return;
        }

        try
        {
            _host.UnmarkVersion(VersionPath(group));
        }
        catch (Exception ex)
        {
            _host.ReportError(ex);
        }

        VersionSync.RemoveVersion(Specification, _context, group.Id);
        if (VersionSync.HasVersions(Specification))
        {
            SyncVersions(_host.ReadComponents());
        }

        Changed(rebuild: true);
        OnPropertyChanged(nameof(ShowVariableData));
    }

    private void SetReplaces(object? parameter)
    {
        if (parameter is not string value || SelectedRow?.Row is not { Kind: RowKind.VersionItem } row)
        {
            return;
        }

        row.ReplacesKey = value.Length > 0 ? value : null;
        row.Position = null;
        VersionSync.MarkInherited(Specification);
        PositionAllocator.AssignMissing(Specification, _context);
        Changed(rebuild: true, selectRowId: row.Id);
    }

    private void PullHeadPositions()
    {
        CommitEdits?.Invoke();
        IReadOnlyDictionary<string, int>? positions;
        try
        {
            positions = _host.ReadHeadPositions();
        }
        catch (Exception ex)
        {
            _host.ReportError(ex);
            _dialogs.Warning($"Не удалось взять номера позиций из головной сборки.\n\n{ex.Message}");
            return;
        }

        if (positions is null || positions.Count == 0)
        {
            _dialogs.Warning("В групповой спецификации головной сборки нет номеров позиций для этого исполнения.");
            return;
        }

        var changed = 0;
        var missing = new List<string>();
        foreach (var row in Specification.Rows.Where(r => r.Kind == RowKind.Component && !r.IsRemoved && r.ComponentKey is not null &&
                                                         PositionAllocator.IsNumbered(r, _context)))
        {
            if (!positions.TryGetValue(row.ComponentKey!, out var position))
            {
                missing.Add(Describe(row));
                continue;
            }

            if (row.Position != position)
            {
                row.Position = position;
                changed++;
            }
        }

        Changed(rebuild: true);
        var report = $"Номера позиций из головной сборки: изменено {changed}.";
        if (missing.Count > 0)
        {
            report += $"\n\nНет в групповой спецификации ({missing.Count}), номер не менялся:\n" +
                      string.Join("\n", missing.Take(10).Select(m => "• " + m)) + (missing.Count > 10 ? "\n…" : "");
        }

        report += "\n\nЗаписать позиции в BOM (для выносок на чертеже)? Не нажимайте потом «Перенумеровать» — номера пересчитаются.";
        if (_dialogs.Confirm(report))
        {
            WritePositions();
        }
    }
}
