using InventorGostSpec.Core.Editing;
using InventorGostSpec.Core.Model;
using InventorGostSpec.Core.Positions;

namespace InventorGostSpec.Core.Update;

public sealed class UpdateResult
{
    public List<SpecRow> Added { get; } = new();

    public List<SpecRow> Changed { get; } = new();

    public List<SpecRow> Removed { get; } = new();

    /// <summary>Результат выдачи номеров новым строкам (недостаток резерва и т. п.).</summary>
    public PositionAssignmentResult Positions { get; set; } = new();

    /// <summary>Спецификация сформирована впервые: строки отсортированы по ГОСТ и пронумерованы.</summary>
    public bool IsInitialBuild { get; set; }

    public bool HasChanges => Added.Count > 0 || Changed.Count > 0 || Removed.Count > 0;
}

/// <summary>
/// Обновление спецификации по текущему составу сборки. Выполняется только по команде пользователя.
/// Ручной порядок, ручные строки, примечания и переопределения сохраняются.
/// </summary>
public static class SpecificationUpdater
{
    /// <summary>Колонки, изменение которых в компоненте подсвечивается.</summary>
    private static readonly SpecColumn[] TrackedColumns = SpecRow.SourceColumns.ToArray();

    public static UpdateResult Update(
        Specification spec,
        IEnumerable<ComponentData> components,
        SpecificationContext context,
        DateTime nowUtc)
    {
        var result = new UpdateResult { IsInitialBuild = spec.LastUpdatedUtc is null && spec.Rows.Count == 0 };
        var snapshots = Aggregate(components);
        SpecificationEditor.NormalizeGroups(spec);

        foreach (var row in spec.Rows)
        {
            // Подсветка относится только к последнему обновлению.
            row.Status &= RowStatus.Removed;
            row.PreviousValues.Clear();
        }

        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in spec.Rows.Where(r => r.Kind == RowKind.Component).ToList())
        {
            if (row.ComponentKey is null || !snapshots.TryGetValue(row.ComponentKey, out var snapshot))
            {
                if (!row.IsRemoved)
                {
                    row.Status = RowStatus.Removed;
                    result.Removed.Add(row);
                }

                continue;
            }

            matched.Add(row.ComponentKey);
            if (row.IsRemoved)
            {
                // Компонент вернули в сборку.
                row.Status = RowStatus.New;
                result.Added.Add(row);
            }

            ApplySnapshot(spec, context, row, snapshot, result);
        }

        foreach (var (key, snapshot) in snapshots)
        {
            if (matched.Contains(key))
            {
                continue;
            }

            var row = new SpecRow
            {
                Kind = RowKind.Component,
                ComponentKey = key,
                Source = snapshot,
                Status = RowStatus.New,
            };
            SpecificationEditor.InsertAtSectionEnd(spec, context, row);
            result.Added.Add(row);
        }

        if (result.IsInitialBuild)
        {
            SpecificationEditor.Reset(spec, context);
            foreach (var row in spec.Rows)
            {
                row.Status = RowStatus.None;
            }

            result.Added.Clear();
        }
        else
        {
            result.Positions = PositionAllocator.AssignMissing(spec, context);
        }

        spec.LastUpdatedUtc = nowUtc;
        return result;
    }

    private static void ApplySnapshot(
        Specification spec,
        SpecificationContext context,
        SpecRow row,
        ComponentSnapshot snapshot,
        UpdateResult result)
    {
        var oldSection = row.SectionId;
        var changed = false;

        // Ручной перенос модели в «Документацию» (из старых версий) не действует: там только документы.
        if (SpecificationEditor.IsModelRow(row) && row.Overrides.TryGetValue(SpecColumn.Section, out var overridden) &&
            SectionResolver.IsDocumentation(overridden))
        {
            row.Overrides.Remove(SpecColumn.Section);
        }
        foreach (var column in TrackedColumns)
        {
            var oldValue = row.Source.Get(column);
            if (oldValue != snapshot.Get(column))
            {
                row.PreviousValues[column] = oldValue;
                changed = true;
            }
        }

        row.Source = snapshot;
        if (changed && !row.Status.HasFlag(RowStatus.New))
        {
            row.Status |= RowStatus.Changed;
            result.Changed.Add(row);
        }

        if (!string.Equals(oldSection, row.SectionId, StringComparison.OrdinalIgnoreCase))
        {
            // Компонент перенесли в другой раздел: строка уходит в конец нового раздела и получает новый номер.
            spec.Rows.Remove(row);
            row.Position = null;
            SpecificationEditor.InsertAtSectionEnd(spec, context, row);
        }
    }

    private static Dictionary<string, ComponentSnapshot> Aggregate(IEnumerable<ComponentData> components)
    {
        var quantities = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var first = new Dictionary<string, ComponentData>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();

        foreach (var component in components)
        {
            if (string.IsNullOrWhiteSpace(component.Key))
            {
                throw new ArgumentException("Component key must not be empty.", nameof(components));
            }

            if (first.TryAdd(component.Key, component))
            {
                quantities[component.Key] = 0;
                order.Add(component.Key);
            }

            quantities[component.Key] += component.Quantity;
        }

        var result = new Dictionary<string, ComponentSnapshot>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in order)
        {
            var snapshot = ComponentSnapshot.FromComponent(first[key]);
            snapshot.Quantity = ComponentSnapshot.IsDocument(snapshot.SectionId) ? "" : ComponentSnapshot.FormatQuantity(quantities[key]);
            result[key] = snapshot;
        }

        return result;
    }
}
