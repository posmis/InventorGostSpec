using InventorGostSpec.Core.Editing;
using InventorGostSpec.Core.Model;
using InventorGostSpec.Core.Positions;
using InventorGostSpec.Core.Update;

namespace InventorGostSpec.Core.Versions;

/// <summary>
/// Групповая спецификация исполнений по ГОСТ 2.113, вариант А: постоянные данные, затем «Переменные данные
/// для исполнений» — для каждого исполнения отдельно, под его обозначением. Исполнения выбирает пользователь;
/// состав сравнивает программа: составная часть (тот же файл, у виртуальных — то же наименование) с одинаковым
/// количеством во всех исполнениях — постоянная, остальные — переменные данные своего исполнения.
/// </summary>
public static class VersionSync
{
    public const string VariableDataTitle = "Переменные данные для исполнений";

    public static IEnumerable<SpecGroup> Versions(Specification spec) => spec.Groups.Where(g => g.IsVersion);

    /// <summary>Основное исполнение — сама сборка (её переменные данные); <c>null</c> — исполнений нет.</summary>
    public static SpecGroup? Base(Specification spec) => spec.Groups.FirstOrDefault(g => g.IsBaseVersion);

    /// <summary>Есть ли исполнения, кроме основного.</summary>
    public static bool HasVersions(Specification spec) => Versions(spec).Any(g => !g.IsBaseVersion);

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
    /// Убирает исполнение: его строки удаляются. Если исполнений больше нет, переменные данные основного исполнения
    /// возвращаются в постоянные данные.
    /// </summary>
    public static void RemoveVersion(Specification spec, SpecificationContext context, string groupId)
    {
        spec.Rows.RemoveAll(r => r.GroupId == groupId && r.Kind == RowKind.VersionItem);
        var group = spec.Groups.FirstOrDefault(g => g.Id == groupId && g.IsVersion && !g.IsBaseVersion);
        if (group is not null)
        {
            spec.Groups.Remove(group);
        }

        if (!HasVersions(spec) && Base(spec) is { } baseVersion)
        {
            foreach (var row in spec.Rows.Where(r => r.GroupId == baseVersion.Id).ToList())
            {
                row.ManualVersionPlacement = false;
                SpecificationEditor.MoveToGroup(spec, context, row, "");
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
    /// различающиеся — в переменные данные своих исполнений. Строки, перенесённые вручную, не трогаются.
    /// </summary>
    /// <param name="head">Состав головной сборки (как при обновлении).</param>
    /// <param name="versions">Состав каждой сборки-исполнения по <see cref="SpecGroup.Id"/>, ключи — относительно
    /// папки головной сборки (как у <paramref name="head"/>).</param>
    public static void Sync(
        Specification spec,
        SpecificationContext context,
        IEnumerable<ComponentData> head,
        IReadOnlyDictionary<string, IReadOnlyList<ComponentData>> versions)
    {
        if (!HasVersions(spec) || Base(spec) is not { } baseVersion)
        {
            return;
        }

        var headSnapshots = SpecificationUpdater.Aggregate(head);
        var others = Versions(spec).Where(g => !g.IsBaseVersion).ToList();
        var versionSnapshots = others.ToDictionary(
            g => g.Id,
            g => SpecificationUpdater.Aggregate(versions.TryGetValue(g.Id, out var list) ? list : Array.Empty<ComponentData>()));

        bool IsCommon(string key) =>
            headSnapshots.TryGetValue(key, out var own) &&
            versionSnapshots.Values.All(v => v.TryGetValue(key, out var other) && other.Quantity == own.Quantity);

        // Строки головной сборки: постоянные ↔ переменные данные основного исполнения.
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

        // Строки исполнений: составные части, которые не общие.
        foreach (var group in others)
        {
            var wanted = versionSnapshots[group.Id].Where(x => !IsCommon(x.Key)).ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
            spec.Rows.RemoveAll(r => r.GroupId == group.Id && r.Kind == RowKind.VersionItem && !wanted.ContainsKey(r.ComponentKey ?? ""));
            var existing = spec.Rows.Where(r => r.GroupId == group.Id && r.Kind == RowKind.VersionItem)
                .ToDictionary(r => r.ComponentKey!, StringComparer.OrdinalIgnoreCase);
            foreach (var (key, snapshot) in wanted)
            {
                if (existing.TryGetValue(key, out var row))
                {
                    var moved = !string.Equals(row.SectionId, snapshot.SectionId, StringComparison.OrdinalIgnoreCase);
                    row.Source = snapshot;
                    if (moved)
                    {
                        spec.Rows.Remove(row);
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
                    GroupId = group.Id,
                });
            }
        }

        MarkInherited(spec);
        PositionAllocator.AssignMissing(spec, context);
    }

    /// <summary>
    /// Строки исполнений, чья составная часть уже есть в постоянных данных, у основного или у предыдущего исполнения,
    /// получают её номер позиции (ГОСТ 2.113, п. 5.5.1 г); заменяющие («Заменяет поз. …») — номер заменяемой.
    /// </summary>
    public static void MarkInherited(Specification spec)
    {
        var known = new HashSet<string>(
            spec.Rows.Where(r => r.Kind != RowKind.VersionItem && r.ComponentKey is not null).Select(r => r.ComponentKey!),
            StringComparer.OrdinalIgnoreCase);
        foreach (var group in Versions(spec).Where(g => !g.IsBaseVersion))
        {
            var rows = spec.Rows.Where(r => r.GroupId == group.Id && r.Kind == RowKind.VersionItem).ToList();
            foreach (var row in rows)
            {
                var inherits = row.ReplacesPosition is not null || known.Contains(row.ComponentKey ?? "");
                if (inherits != row.InheritsPosition)
                {
                    row.InheritsPosition = inherits;
                    row.Position = null;
                }
            }

            known.UnionWith(rows.Select(r => r.ComponentKey ?? ""));
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
            row.Position = row.ReplacesPosition ?? (byKey.TryGetValue(row.ComponentKey ?? "", out var position) ? position : null);
        }

        // Внутри каждого раздела исполнения: сначала составные части с унаследованными номерами по возрастанию, затем новые.
        foreach (var group in Versions(spec).Where(g => !g.IsBaseVersion))
        {
            foreach (var section in spec.Rows.Where(r => r.GroupId == group.Id).Select(r => r.SectionId).Distinct(StringComparer.OrdinalIgnoreCase).ToList())
            {
                var indices = spec.Rows
                    .Select((r, i) => (Row: r, Index: i))
                    .Where(x => x.Row.GroupId == group.Id && string.Equals(x.Row.SectionId, section, StringComparison.OrdinalIgnoreCase))
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
    }

    /// <summary>
    /// Исполнение полностью совпадает с предыдущим (те же составные части в тех же количествах) — его данные не
    /// повторяются, заголовок «… (тоже как для …)». <c>null</c> — не совпадает или пустое.
    /// </summary>
    public static SpecGroup? SameAs(Specification spec, SpecGroup group)
    {
        static string Signature(IEnumerable<SpecRow> rows) => string.Join("\n", rows
            .Where(r => r.Kind != RowKind.Note && !r.IsRemoved)
            .Select(r => (r.ComponentKey ?? r.Get(SpecColumn.Designation) + "|" + r.Get(SpecColumn.Name)) + "=" + r.Get(SpecColumn.Quantity))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase));

        var own = Signature(spec.Rows.Where(r => r.GroupId == group.Id));
        if (own.Length == 0)
        {
            return null;
        }

        foreach (var earlier in Versions(spec).TakeWhile(g => g.Id != group.Id))
        {
            if (Signature(spec.Rows.Where(r => r.GroupId == earlier.Id)) == own)
            {
                return earlier;
            }
        }

        return null;
    }
}
