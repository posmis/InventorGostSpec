using InventorGostSpec.Core.Model;
using InventorGostSpec.Core.Positions;
using InventorGostSpec.Core.Sections;
using InventorGostSpec.Core.Sorting;

namespace InventorGostSpec.Core.Editing;

/// <summary>Ручное редактирование спецификации.</summary>
public static class SpecificationEditor
{
    /// <summary>
    /// Задаёт значение ячейки. У строк компонентов значения из компонента не меняются,
    /// а переопределяются только в этой спецификации; значение, равное значению компонента,
    /// снимает переопределение. У ручных строк значение записывается напрямую.
    /// </summary>
    public static void SetCell(Specification spec, SpecificationContext context, SpecRow row, SpecColumn column, string? value)
    {
        value = value?.Trim() ?? "";
        var oldSection = row.SectionId;

        switch (column)
        {
            case SpecColumn.Zone:
                row.Zone = value;
                return;
            case SpecColumn.Note:
                row.Note = value;
                return;
            case SpecColumn.Position:
                row.Position = int.TryParse(value, out var position) && position > 0 ? position : null;
                return;
        }

        if (row.Kind == RowKind.Component)
        {
            if (value == row.Source.Get(column))
            {
                row.Overrides.Remove(column);
            }
            else
            {
                row.Overrides[column] = value;
            }
        }
        else
        {
            row.Source.Set(column, value);
        }

        if (column == SpecColumn.Section && !string.Equals(oldSection, row.SectionId, StringComparison.OrdinalIgnoreCase))
        {
            OnSectionChanged(spec, context, row);
        }
    }

    /// <summary>Снимает ручное переопределение ячейки строки компонента.</summary>
    public static void ClearOverride(Specification spec, SpecificationContext context, SpecRow row, SpecColumn column)
    {
        var oldSection = row.SectionId;
        if (row.Overrides.Remove(column) && column == SpecColumn.Section &&
            !string.Equals(oldSection, row.SectionId, StringComparison.OrdinalIgnoreCase))
        {
            OnSectionChanged(spec, context, row);
        }
    }

    /// <summary>
    /// Перемещает строку внутри её раздела на место строки <paramref name="targetRowId"/>
    /// (или в конец раздела, если цель не указана). Номера позиций раздела переставляются
    /// так, чтобы они по-прежнему шли по возрастанию; другие разделы не затрагиваются.
    /// </summary>
    /// <returns><c>false</c>, если цель находится в другом разделе.</returns>
    public static bool MoveRow(Specification spec, string rowId, string? targetRowId)
    {
        var row = spec.FindRow(rowId) ?? throw new ArgumentException($"Row '{rowId}' not found.", nameof(rowId));
        if (rowId == targetRowId)
        {
            return true;
        }

        var target = targetRowId is null ? null : spec.FindRow(targetRowId);
        if (targetRowId is not null && (target is null || !SameSection(target, row)))
        {
            return false;
        }

        var positions = NumberedPositions(spec, row.SectionId);
        var oldIndex = spec.Rows.IndexOf(row);
        var targetIndex = target is null ? -1 : spec.Rows.IndexOf(target);

        // Строка занимает место цели: при перемещении вверх встаёт перед ней, при перемещении вниз — после.
        spec.Rows.RemoveAt(oldIndex);
        var sectionEnd = LastIndexOfSection(spec, row.SectionId);
        spec.Rows.Insert(target is not null ? targetIndex : sectionEnd >= 0 ? sectionEnd : oldIndex, row);

        ReapplyPositions(spec, row.SectionId, positions);
        return true;
    }

    /// <summary>Сдвигает строку на одну строку вверх (-1) или вниз (+1) внутри раздела.</summary>
    public static bool MoveRowBy(Specification spec, string rowId, int direction)
    {
        var row = spec.FindRow(rowId) ?? throw new ArgumentException($"Row '{rowId}' not found.", nameof(rowId));
        var index = spec.Rows.IndexOf(row) + Math.Sign(direction);
        if (index < 0 || index >= spec.Rows.Count || !SameSection(spec.Rows[index], row))
        {
            return false;
        }

        return MoveRow(spec, rowId, spec.Rows[index].Id);
    }

    /// <summary>Добавляет текстовую строку (без позиции) после указанной строки или в конец раздела.</summary>
    public static SpecRow AddNote(Specification spec, SpecificationContext context, string sectionId, string text, string? afterRowId = null)
    {
        var row = new SpecRow { Kind = RowKind.Note };
        row.Source.SectionId = sectionId;
        row.Source.Name = text.Trim();
        Insert(spec, context, row, afterRowId);
        return row;
    }

    /// <summary>
    /// Добавляет строку изделия вручную (без модели). Номер позиции выдаётся из резерва раздела.
    /// </summary>
    public static SpecRow AddManualItem(
        Specification spec,
        SpecificationContext context,
        ComponentSnapshot values,
        string? afterRowId = null)
    {
        var row = new SpecRow { Kind = RowKind.ManualItem, Source = values.Clone() };
        if (string.IsNullOrWhiteSpace(row.Source.SectionId))
        {
            throw new ArgumentException("Section must be specified.", nameof(values));
        }

        Insert(spec, context, row, afterRowId);
        PositionAllocator.AssignMissing(spec, context);
        return row;
    }

    /// <summary>
    /// Удаляет строку. Строки компонентов, которые есть в сборке, удалить нельзя —
    /// они вернутся при следующем обновлении.
    /// </summary>
    public static bool DeleteRow(Specification spec, string rowId)
    {
        var row = spec.FindRow(rowId);
        if (row is null || (row.Kind == RowKind.Component && !row.IsRemoved))
        {
            return false;
        }

        spec.Rows.Remove(row);
        return true;
    }

    /// <summary>Добавляет раздел только в эту спецификацию.</summary>
    public static void AddCustomSection(Specification spec, SectionDefinition section)
    {
        if (SectionCatalog.CreateBuiltIn().Any(s => string.Equals(s.Id, section.Id, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException($"Section '{section.Id}' is built-in.", nameof(section));
        }

        spec.CustomSections.RemoveAll(s => string.Equals(s.Id, section.Id, StringComparison.OrdinalIgnoreCase));
        var copy = section.Clone();
        copy.IsBuiltIn = false;
        spec.CustomSections.Add(copy);
    }

    /// <summary>Снимает подсветку новых и изменённых строк и удаляет строки компонентов, которых нет в сборке.</summary>
    public static void AcceptChanges(Specification spec)
    {
        spec.Rows.RemoveAll(r => r.Kind == RowKind.Component && r.IsRemoved);
        foreach (var row in spec.Rows)
        {
            row.Status = RowStatus.None;
            row.PreviousValues.Clear();
        }
    }

    /// <summary>
    /// Полный сброс: строки сортируются по ГОСТ, позиции перенумеровываются с учётом резервов.
    /// Текстовые строки остаются после строки, за которой стояли.
    /// </summary>
    public static void Reset(Specification spec, SpecificationContext context)
    {
        var result = new List<SpecRow>(spec.Rows.Count);
        foreach (var section in OrderedSections(spec, context))
        {
            var comparer = new GostRowComparer(section.SortRule);
            var rows = spec.Rows.Where(r => string.Equals(r.SectionId, section.Id, StringComparison.OrdinalIgnoreCase));
            var blocks = SplitIntoBlocks(rows);

            // Блок без ведущей строки (тексты в начале раздела) остаётся первым.
            var leading = blocks.Where(b => b.Head is null).SelectMany(b => b.Rows);
            var sorted = blocks.Where(b => b.Head is not null)
                .Select((block, index) => (block, index))
                .OrderBy(x => x.block.Head!, comparer)
                .ThenBy(x => x.index)
                .SelectMany(x => x.block.Rows);

            result.AddRange(leading);
            result.AddRange(sorted);
        }

        spec.Rows = result;
        PositionAllocator.RenumberAll(spec, context);
    }

    /// <summary>Упорядочивает строки по разделам, сохраняя порядок внутри раздела.</summary>
    public static void Normalize(Specification spec, SpecificationContext context)
    {
        var sections = OrderedSections(spec, context).Select(s => s.Id).ToList();
        spec.Rows = spec.Rows
            .Select((row, index) => (row, index))
            .OrderBy(x => sections.FindIndex(id => string.Equals(id, x.row.SectionId, StringComparison.OrdinalIgnoreCase)))
            .ThenBy(x => x.index)
            .Select(x => x.row)
            .ToList();
    }

    /// <summary>Вставляет строку в конец её раздела.</summary>
    internal static void InsertAtSectionEnd(Specification spec, SpecificationContext context, SpecRow row)
    {
        var last = LastIndexOfSection(spec, row.SectionId);
        if (last >= 0)
        {
            spec.Rows.Insert(last, row);
            return;
        }

        var order = context.Catalog.IndexOf(row.SectionId);
        var index = spec.Rows.FindIndex(r => context.Catalog.IndexOf(r.SectionId) > order);
        spec.Rows.Insert(index < 0 ? spec.Rows.Count : index, row);
    }

    internal static IEnumerable<SectionDefinition> OrderedSections(Specification spec, SpecificationContext context)
    {
        // Регистрируем разделы строк, которых нет в каталоге, чтобы строки не пропали.
        foreach (var row in spec.Rows)
        {
            context.Catalog.GetOrCreate(row.SectionId);
        }

        return context.Catalog.Sections.ToList();
    }

    private static void Insert(Specification spec, SpecificationContext context, SpecRow row, string? afterRowId)
    {
        var after = afterRowId is null ? null : spec.FindRow(afterRowId);
        if (after is not null && SameSection(after, row))
        {
            spec.Rows.Insert(spec.Rows.IndexOf(after) + 1, row);
        }
        else
        {
            InsertAtSectionEnd(spec, context, row);
        }
    }

    private static void OnSectionChanged(Specification spec, SpecificationContext context, SpecRow row)
    {
        spec.Rows.Remove(row);
        row.Position = null;
        InsertAtSectionEnd(spec, context, row);
        PositionAllocator.AssignMissing(spec, context);
    }

    /// <summary>Индекс сразу после последней строки раздела (или -1, если строк раздела нет).</summary>
    private static int LastIndexOfSection(Specification spec, string sectionId)
    {
        var last = spec.Rows.FindLastIndex(r => string.Equals(r.SectionId, sectionId, StringComparison.OrdinalIgnoreCase));
        return last < 0 ? -1 : last + 1;
    }

    private static bool SameSection(SpecRow a, SpecRow b) =>
        string.Equals(a.SectionId, b.SectionId, StringComparison.OrdinalIgnoreCase);

    private static List<int> NumberedPositions(Specification spec, string sectionId) =>
        SectionNumberedRows(spec, sectionId).Select(r => r.Position!.Value).OrderBy(p => p).ToList();

    private static void ReapplyPositions(Specification spec, string sectionId, List<int> positions)
    {
        var rows = SectionNumberedRows(spec, sectionId).ToList();
        for (var i = 0; i < rows.Count && i < positions.Count; i++)
        {
            rows[i].Position = positions[i];
        }
    }

    private static IEnumerable<SpecRow> SectionNumberedRows(Specification spec, string sectionId) =>
        spec.Rows.Where(r => r.Position.HasValue && !r.IsRemoved &&
                             string.Equals(r.SectionId, sectionId, StringComparison.OrdinalIgnoreCase));

    private static List<Block> SplitIntoBlocks(IEnumerable<SpecRow> rows)
    {
        var blocks = new List<Block>();
        Block? current = null;
        foreach (var row in rows)
        {
            if (row.Kind == RowKind.Note && current is not null)
            {
                current.Rows.Add(row);
                continue;
            }

            current = new Block(row.Kind == RowKind.Note ? null : row);
            current.Rows.Add(row);
            blocks.Add(current);
        }

        return blocks;
    }

    private sealed class Block
    {
        public Block(SpecRow? head)
        {
            Head = head;
        }

        public SpecRow? Head { get; }

        public List<SpecRow> Rows { get; } = new();
    }
}
