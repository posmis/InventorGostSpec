using InventorGostSpec.Core.Editing;
using InventorGostSpec.Core.Model;
using InventorGostSpec.Core.Positions;
using InventorGostSpec.Core.Update;

namespace InventorGostSpec.Core.Versions;

/// <summary>Состав сборки-исполнения для сравнения.</summary>
/// <param name="Components">Компоненты; ключи — относительно папки головной сборки.</param>
/// <param name="GroupTitles">
/// Группы «устанавливаемые по …» спецификации самой сборки-исполнения: ключ компонента → заголовок группы.
/// Компонента нет — он в основной части.
/// </param>
public sealed record VersionComponents(
    IReadOnlyList<ComponentData> Components,
    IReadOnlyDictionary<string, string>? GroupTitles = null);

/// <summary>
/// Групповая спецификация исполнений по ГОСТ 2.113, вариант А: постоянные данные, затем «Переменные данные
/// для исполнений» — для каждого исполнения отдельно, под его обозначением, со своими разделами и группами
/// «устанавливаемые по …». Исполнения выбирает пользователь; состав сравнивает программа: составная часть (тот же
/// файл, у виртуальных — то же наименование) с одинаковым количеством во всех исполнениях — постоянная, остальные —
/// переменные данные своего исполнения.
/// </summary>
public static class VersionSync
{
    /// <summary>Общий заголовок; в PDF — по центру объединённых граф «Обозначение» и «Наименование».</summary>
    public const string VariableDataTitle = "Переменные данные для исполнений:";

    /// <summary>
    /// Подпись постоянных данных в окне (уровень исполнений): «Общие компоненты» → группы «устанавливаемые по …» →
    /// разделы. В Excel и PDF не выводится — по ГОСТ 2.113 у постоянных данных заголовка нет.
    /// </summary>
    public const string CommonTitle = "Общие компоненты (постоянные данные)";

    /// <summary>
    /// Название части спецификации по уровням: «Общие компоненты → группа», «Исполнение … → группа»; без исполнений —
    /// «Основная часть» и заголовок группы.
    /// </summary>
    public static string PartName(Specification spec, string groupId)
    {
        var shown = IsShown(spec);
        var group = spec.Groups.FirstOrDefault(g => g.Id == groupId);
        if (group is null)
        {
            return shown ? "Общие компоненты" : "Основная часть";
        }

        if (group.IsVersion)
        {
            return $"Исполнение {group.Title}";
        }

        if (VersionOf(spec, group.Id) is { } version)
        {
            return $"Исполнение {version.Title} → {group.Title}";
        }

        return shown ? $"Общие компоненты → {group.Title}" : group.Title;
    }

    /// <summary>Исполнения (без групп внутри них), в порядке записи.</summary>
    public static IEnumerable<SpecGroup> Versions(Specification spec) => spec.Groups.Where(g => g.IsVersion);

    /// <summary>Основное исполнение — сама сборка (её переменные данные); <c>null</c> — исполнений нет.</summary>
    public static SpecGroup? Base(Specification spec) => spec.Groups.FirstOrDefault(g => g.IsBaseVersion);

    /// <summary>Есть ли исполнения, кроме основного.</summary>
    public static bool HasVersions(Specification spec) => Versions(spec).Any(g => !g.IsBaseVersion);

    /// <summary>Показывается ли раздел «Переменные данные для исполнений»: есть исполнения или включена галочка.</summary>
    public static bool IsShown(Specification spec) => HasVersions(spec) || (spec.ShowVariableData && Base(spec) is not null);

    /// <summary>
    /// Галочка «Переменные данные для исполнений»: включена — создаётся основное исполнение (пустое); выключена — если
    /// других исполнений нет, его строки и группы возвращаются в постоянные данные. При исполнениях выключить нельзя
    /// (раздел нужен) — возвращает <c>false</c>.
    /// </summary>
    public static bool SetShown(Specification spec, SpecificationContext context, bool shown, string baseDesignation)
    {
        if (!shown && HasVersions(spec))
        {
            return false;
        }

        spec.ShowVariableData = shown;
        if (shown)
        {
            if (Base(spec) is { } existing)
            {
                existing.Title = baseDesignation;
            }
            else
            {
                spec.Groups.Add(new SpecGroup { Title = baseDesignation, IsVersion = true });
            }
        }
        else
        {
            RemoveVersion(spec, context, "");
        }

        return true;
    }

    /// <summary>
    /// Ключ компонента относительно другой папки сборки: «Детали\А.ipt» сборки-исполнения → «..\Исп01\Детали\А.ipt»
    /// относительно головной. Виртуальные компоненты (по имени) не меняются; у чертежей («drawing:…») — путь после префикса.
    /// </summary>
    public static string RebaseKey(string key, string fromFolder, string toFolder)
    {
        const string virtualPrefix = "virtual:";
        const string drawingPrefix = "drawing:";
        if (key.StartsWith(virtualPrefix, StringComparison.Ordinal))
        {
            return key;
        }

        var isDrawing = key.StartsWith(drawingPrefix, StringComparison.Ordinal);
        var relative = isDrawing ? key[drawingPrefix.Length..] : key;
        var rebased = Path.GetRelativePath(toFolder, Path.GetFullPath(Path.Combine(fromFolder, relative)));
        return isDrawing ? drawingPrefix + rebased : rebased;
    }

    /// <summary>
    /// Номера позиций групповой спецификации, которые относятся к исполнению: постоянные данные (общие компоненты
    /// и их группы) и переменные данные этого исполнения (в том числе номера, взятые у основного исполнения). Ключи —
    /// как в головной спецификации. Нужны сборке-исполнению: её BOM и выноски на чертеже — с номерами групповой
    /// спецификации.
    /// </summary>
    public static Dictionary<string, int> PositionsFor(Specification spec, SpecGroup version)
    {
        var scope = Scope(spec, version).ToHashSet();
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in spec.Rows.Where(r => r.Position.HasValue && r.ComponentKey is not null && !r.IsRemoved))
        {
            if (scope.Contains(row.GroupId) || VersionOf(spec, row.GroupId) is null)
            {
                result.TryAdd(row.ComponentKey!, row.Position!.Value);
            }
        }

        return result;
    }

    /// <summary>Исполнение, к которому относится группа: само исполнение или исполнение группы внутри него.</summary>
    public static SpecGroup? VersionOf(Specification spec, string groupId)
    {
        var group = spec.Groups.FirstOrDefault(g => g.Id == groupId);
        return group is null ? null
            : group.IsVersion ? group
            : group.VersionId.Length > 0 ? spec.Groups.FirstOrDefault(g => g.Id == group.VersionId && g.IsVersion)
            : null;
    }

    /// <summary>Исполнение и его группы «устанавливаемые по …».</summary>
    public static IEnumerable<string> Scope(Specification spec, SpecGroup version) =>
        new[] { version.Id }.Concat(spec.Groups.Where(g => g.VersionId == version.Id).Select(g => g.Id));

    /// <summary>Группа относится к другому исполнению (не основному): её строки — только для чтения.</summary>
    public static bool IsReadOnly(Specification spec, string groupId) => VersionOf(spec, groupId) is { IsBaseVersion: false };

    /// <summary>
    /// Добавляет исполнение (сборку-исполнение). При первом добавлении создаётся и основное исполнение — с обозначением
    /// головной сборки. Та же сборка второй раз не добавляется (возвращается её исполнение).
    /// </summary>
    /// <param name="versionFile">Файл сборки-исполнения относительно папки головной сборки.</param>
    public static SpecGroup AddVersion(Specification spec, string baseDesignation, string designation, string versionFile)
    {
        if (Versions(spec).FirstOrDefault(g => string.Equals(g.VersionFile, versionFile, StringComparison.OrdinalIgnoreCase)) is { } existing)
        {
            existing.Title = designation;
            return existing;
        }

        if (Base(spec) is null)
        {
            spec.Groups.Add(new SpecGroup { Title = baseDesignation, IsVersion = true });
        }
        else
        {
            Base(spec)!.Title = baseDesignation;
        }

        var group = new SpecGroup { Title = designation, IsVersion = true, VersionFile = versionFile };
        spec.Groups.Add(group);
        return group;
    }

    /// <summary>
    /// Убирает исполнение вместе с его группами: его строки удаляются. Если исполнений больше нет, переменные данные
    /// основного исполнения (и его групп) возвращаются в постоянные данные.
    /// </summary>
    public static void RemoveVersion(Specification spec, SpecificationContext context, string groupId)
    {
        if (spec.Groups.FirstOrDefault(g => g.Id == groupId && g.IsVersion && !g.IsBaseVersion) is { } group)
        {
            var scope = Scope(spec, group).ToHashSet();
            spec.Rows.RemoveAll(r => scope.Contains(r.GroupId));
            spec.Groups.RemoveAll(g => scope.Contains(g.Id));
        }

        if (!HasVersions(spec) && !spec.ShowVariableData && Base(spec) is { } baseVersion)
        {
            // Группы основного исполнения становятся группами постоянных данных.
            foreach (var child in spec.Groups.Where(g => g.VersionId == baseVersion.Id).ToList())
            {
                spec.Groups.Remove(child);
                child.VersionId = "";
                var index = spec.Groups.FindIndex(g => g.IsVersion);
                spec.Groups.Insert(index < 0 ? spec.Groups.Count : index, child);
            }

            foreach (var row in spec.Rows.Where(r => r.GroupId == baseVersion.Id).ToList())
            {
                SpecificationEditor.MoveToGroup(spec, context, row, "", assignPositions: false);
            }

            spec.Groups.Remove(baseVersion);
            foreach (var row in spec.Rows)
            {
                row.ManualVersionPlacement = false;
            }
        }

        PositionAllocator.AssignMissing(spec, context);
    }

    /// <summary>
    /// Сравнивает состав головной сборки и сборок-исполнений и раскладывает строки: общие — в постоянные данные,
    /// различающиеся — в переменные данные своих исполнений (в те же группы «устанавливаемые по …», что в
    /// спецификации сборки-исполнения). Строки, перенесённые вручную, не трогаются.
    /// </summary>
    /// <param name="head">Состав головной сборки (как при обновлении).</param>
    /// <param name="versions">Состав каждой сборки-исполнения по <see cref="SpecGroup.Id"/> исполнения.</param>
    public static void Sync(
        Specification spec,
        SpecificationContext context,
        IEnumerable<ComponentData> head,
        IReadOnlyDictionary<string, VersionComponents> versions)
    {
        if (!HasVersions(spec) || Base(spec) is not { } baseVersion)
        {
            return;
        }

        var headSnapshots = SpecificationUpdater.Aggregate(head);
        var others = Versions(spec).Where(g => !g.IsBaseVersion).ToList();
        var versionSnapshots = others.ToDictionary(
            g => g.Id,
            g => SpecificationUpdater.Aggregate(versions.TryGetValue(g.Id, out var data) ? data.Components : Array.Empty<ComponentData>()));

        bool IsCommon(string key) =>
            headSnapshots.TryGetValue(key, out var own) &&
            versionSnapshots.Values.All(v => v.TryGetValue(key, out var other) && other.Quantity == own.Quantity);

        // Строки головной сборки: постоянные ↔ переменные данные основного исполнения. Строки в группах
        // («устанавливаемые по …» постоянных данных или основного исполнения) расставлены пользователем — не трогаем.
        foreach (var row in spec.Rows.Where(r => r.Kind == RowKind.Component && !r.IsRemoved && r.ComponentKey is not null).ToList())
        {
            if (row.ManualVersionPlacement || (row.GroupId.Length > 0 && row.GroupId != baseVersion.Id))
            {
                continue;
            }

            var target = IsCommon(row.ComponentKey!) ? "" : baseVersion.Id;
            if (row.GroupId != target)
            {
                SpecificationEditor.MoveToGroup(spec, context, row, target, assignPositions: false);
            }
        }

        // Строки исполнений: составные части, которые не общие, — в само исполнение или в его группу.
        foreach (var version in others)
        {
            var titles = versions.TryGetValue(version.Id, out var data) ? data.GroupTitles : null;
            var wanted = versionSnapshots[version.Id].Where(x => !IsCommon(x.Key))
                .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
            var scope = Scope(spec, version).ToHashSet();
            spec.Rows.RemoveAll(r => scope.Contains(r.GroupId) && r.Kind == RowKind.VersionItem && !wanted.ContainsKey(r.ComponentKey ?? ""));
            var existing = spec.Rows.Where(r => scope.Contains(r.GroupId) && r.Kind == RowKind.VersionItem)
                .ToDictionary(r => r.ComponentKey!, StringComparer.OrdinalIgnoreCase);
            SpecRow? row;

            foreach (var (key, snapshot) in wanted)
            {
                // Перенесена вручную в группу этого исполнения («устанавливаемые по …») — там и остаётся;
                // иначе — в ту же группу, что в спецификации сборки-исполнения.
                existing.TryGetValue(key, out row);
                var groupId = row is { ManualVersionPlacement: true } && scope.Contains(row.GroupId) ? row.GroupId
                    : titles is not null && titles.TryGetValue(key, out var title) && title.Trim().Length > 0
                        ? GroupIn(spec, version, title.Trim()).Id
                        : version.Id;
                if (row is not null)
                {
                    var moved = row.GroupId != groupId || !string.Equals(row.SectionId, snapshot.SectionId, StringComparison.OrdinalIgnoreCase);
                    row.Source = snapshot;
                    if (moved)
                    {
                        spec.Rows.Remove(row);
                        row.GroupId = groupId;
                        row.Position = null;
                        SpecificationEditor.InsertAtSectionEnd(spec, context, row);
                    }

                    continue;
                }

                SpecificationEditor.InsertAtSectionEnd(spec, context, new SpecRow
                {
                    Kind = RowKind.VersionItem,
                    ComponentKey = key,
                    Source = snapshot,
                    GroupId = groupId,
                });
            }

            // Группы, взятые из сборки-исполнения, без строк больше не нужны.
            spec.Groups.RemoveAll(g => g.VersionId == version.Id && g.FromVersionAssembly && spec.Rows.All(r => r.GroupId != g.Id));
        }

        MarkInherited(spec);
        PositionAllocator.AssignMissing(spec, context);
    }

    /// <summary>Группа «устанавливаемые по …» внутри исполнения с этим заголовком; нет — создаётся.</summary>
    private static SpecGroup GroupIn(Specification spec, SpecGroup version, string title) =>
        spec.Groups.FirstOrDefault(g => g.VersionId == version.Id && string.Equals(g.Title, title, StringComparison.OrdinalIgnoreCase))
        ?? Created(SpecificationEditor.AddGroup(spec, title, versionId: version.Id));

    private static SpecGroup Created(SpecGroup group)
    {
        group.FromVersionAssembly = true;
        return group;
    }

    /// <summary>
    /// Группа «устанавливаемые по …», которую можно переименовать и удалить: создана вручную (в постоянных данных или
    /// в любом исполнении), а не взята из сборки-исполнения.
    /// </summary>
    public static bool IsManualGroup(Specification spec, string groupId) =>
        spec.Groups.FirstOrDefault(g => g.Id == groupId) is { IsVersion: false, FromVersionAssembly: false };

    /// <summary>
    /// Строки исполнений получают номер позиции той же составной части в постоянных данных, у основного или
    /// у предыдущего исполнения (ГОСТ 2.113, п. 5.5.1 г). Та же составная часть, по порядку проверки:
    /// «Заменяет поз. …» (вручную); тот же файл; то же обозначение; другое исполнение того же изделия — то же
    /// обозначение без номера исполнения («АБВГ.10.10.000-01» — исполнение «АБВГ.10.10.000»). Каждая составная часть
    /// даёт номер в исполнении только одной строке; остальные — новые составные части со своими номерами.
    /// </summary>
    public static void MarkInherited(Specification spec)
    {
        // Чьи номера можно взять: строки головной сборки и собственные (не наследующие) строки предыдущих исполнений.
        var sources = spec.Rows.Where(r => r.Kind != RowKind.VersionItem && r.Kind != RowKind.Note && !r.IsRemoved && r.ComponentKey is not null).ToList();
        foreach (var version in Versions(spec).Where(g => !g.IsBaseVersion).ToList())
        {
            var scope = Scope(spec, version).ToHashSet();
            var rows = spec.Rows.Where(r => scope.Contains(r.GroupId) && r.Kind == RowKind.VersionItem).ToList();
            var matched = new Dictionary<SpecRow, string>();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Match(Func<SpecRow, string?> find)
            {
                foreach (var row in rows.Where(r => !matched.ContainsKey(r)))
                {
                    if (find(row) is { } key && used.Add(key))
                    {
                        matched[row] = key;
                    }
                }
            }

            static string Normalized(string? designation) => Sorting.NaturalComparer.Normalize(designation ?? "");
            string? ByDesignation(SpecRow row, Func<string?, string> form)
            {
                var designation = form(row.Get(SpecColumn.Designation));
                return designation.Length == 0 ? null
                    : sources.FirstOrDefault(s => !used.Contains(s.ComponentKey!) && form(s.Get(SpecColumn.Designation)) == designation)?.ComponentKey;
            }

            Match(r => r.ReplacesKey);
            Match(r => sources.Any(s => string.Equals(s.ComponentKey, r.ComponentKey, StringComparison.OrdinalIgnoreCase)) ? r.ComponentKey : null);
            Match(r => ByDesignation(r, d => Normalized(d)));
            Match(r => ByDesignation(r, d => Normalized(Designations.WithoutVersionNumber(d))));

            foreach (var row in rows)
            {
                var from = matched.TryGetValue(row, out var key) ? key : null;
                if (!string.Equals(from, row.InheritsFrom, StringComparison.OrdinalIgnoreCase))
                {
                    row.InheritsFrom = from;
                    row.Position = null;
                }
            }

            sources.AddRange(rows.Where(r => !r.InheritsPosition));
        }
    }

    /// <summary>
    /// Номера позиций строк, наследующих номер, и порядок строк исполнений по номерам (п. 5.5.1 г: в порядке номеров,
    /// определившихся для основного исполнения; новые составные части — в конце раздела). Вызывается после нумерации.
    /// </summary>
    public static void ApplyInheritedPositions(Specification spec)
    {
        var inherited = spec.Rows.Where(r => r.InheritsPosition).ToList();
        if (inherited.Count == 0)
        {
            return;
        }

        var byKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in spec.Rows.Where(r => !r.InheritsPosition && !r.IsRemoved && r.Position.HasValue && r.ComponentKey is not null))
        {
            byKey.TryAdd(row.ComponentKey!, row.Position!.Value);
        }

        foreach (var row in inherited)
        {
            row.Position = byKey.TryGetValue(row.InheritsFrom!, out var position) ? position : null;
        }

        // Внутри каждого раздела исполнения: сначала составные части с унаследованными номерами по возрастанию, затем новые.
        var readOnly = spec.Groups.Where(g => IsReadOnly(spec, g.Id)).Select(g => g.Id).ToHashSet();
        foreach (var block in spec.Rows.Where(r => readOnly.Contains(r.GroupId))
                     .Select(r => (r.GroupId, Section: r.SectionId.ToUpperInvariant())).Distinct().ToList())
        {
            var indices = spec.Rows
                .Select((r, i) => (Row: r, Index: i))
                .Where(x => x.Row.GroupId == block.GroupId && x.Row.SectionId.ToUpperInvariant() == block.Section)
                .ToList();
            var ordered = indices
                .OrderBy(x => x.Row.InheritsPosition ? 0 : 1)
                .ThenBy(x => x.Row.InheritsPosition ? x.Row.Position ?? int.MaxValue : x.Index)
                .Select(x => x.Row)
                .ToList();
            for (var k = 0; k < indices.Count; k++)
            {
                spec.Rows[indices[k].Index] = ordered[k];
            }
        }
    }

    /// <summary>
    /// Исполнение полностью совпадает с предыдущим (те же составные части в тех же количествах и группах) — его данные
    /// не повторяются, заголовок «… (тоже как для …)». <c>null</c> — не совпадает или пустое.
    /// </summary>
    public static SpecGroup? SameAs(Specification spec, SpecGroup version)
    {
        string Signature(SpecGroup v)
        {
            var titles = spec.Groups.ToDictionary(g => g.Id, g => g.IsVersion ? "" : g.Title);
            var scope = Scope(spec, v).ToHashSet();
            return string.Join("\n", spec.Rows
                .Where(r => scope.Contains(r.GroupId) && r.Kind != RowKind.Note && !r.IsRemoved)
                .Select(r => titles[r.GroupId] + "|" + (r.ComponentKey ?? r.Get(SpecColumn.Designation) + "|" + r.Get(SpecColumn.Name)) +
                             "=" + r.Get(SpecColumn.Quantity))
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        }

        var own = Signature(version);
        if (own.Length == 0)
        {
            return null;
        }

        return Versions(spec).TakeWhile(g => g.Id != version.Id).FirstOrDefault(earlier => Signature(earlier) == own);
    }
}
