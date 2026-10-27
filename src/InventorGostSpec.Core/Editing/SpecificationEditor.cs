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
        if (targetRowId is not null && (target is null || !SpecBlocks.Same(target, row)))
        {
            return false;
        }

        var positions = NumberedPositions(spec, row);
        var oldIndex = spec.Rows.IndexOf(row);
        var targetIndex = target is null ? -1 : spec.Rows.IndexOf(target);

        // Строка занимает место цели: при перемещении вверх встаёт перед ней, при перемещении вниз — после.
        spec.Rows.RemoveAt(oldIndex);
        var blockEnd = LastIndexOfBlock(spec, row);
        spec.Rows.Insert(target is not null ? targetIndex : blockEnd >= 0 ? blockEnd : oldIndex, row);

        ReapplyPositions(spec, row, positions);
        return true;
    }

    /// <summary>Сдвигает строку на одну строку вверх (-1) или вниз (+1) внутри раздела.</summary>
    public static bool MoveRowBy(Specification spec, string rowId, int direction)
    {
        var row = spec.FindRow(rowId) ?? throw new ArgumentException($"Row '{rowId}' not found.", nameof(rowId));
        var index = spec.Rows.IndexOf(row) + Math.Sign(direction);
        if (index < 0 || index >= spec.Rows.Count || !SpecBlocks.Same(spec.Rows[index], row))
        {
            return false;
        }

        return MoveRow(spec, rowId, spec.Rows[index].Id);
    }

    /// <summary>Добавляет текстовую строку (без позиции) после указанной строки или в конец раздела.</summary>
    /// <param name="groupId">Группа «устанавливаемые по …»; пусто — основная часть.</param>
    public static SpecRow AddNote(
        Specification spec,
        SpecificationContext context,
        string sectionId,
        string text,
        string? afterRowId = null,
        string groupId = "")
    {
        var row = new SpecRow { Kind = RowKind.Note, GroupId = groupId };
        row.Source.SectionId = sectionId;
        row.Source.Name = text.Trim();
        Insert(spec, context, row, afterRowId);
        return row;
    }

    /// <summary>
    /// Добавляет строку изделия вручную (без модели). Номер позиции выдаётся из резерва раздела.
    /// </summary>
    /// <param name="groupId">Группа «устанавливаемые по …»; пусто — основная часть.</param>
    public static SpecRow AddManualItem(
        Specification spec,
        SpecificationContext context,
        ComponentSnapshot values,
        string? afterRowId = null,
        string groupId = "")
    {
        var row = new SpecRow { Kind = RowKind.ManualItem, Source = values.Clone(), GroupId = groupId };
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
        NormalizeGroups(spec);
        var result = new List<SpecRow>(spec.Rows.Count);
        foreach (var specBlock in SpecBlocks.All(spec, context))
        {
            var comparer = new GostRowComparer(specBlock.Section.SortRule);
            var rows = spec.Rows.Where(specBlock.Contains);
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

    /// <summary>Упорядочивает строки по группам и разделам, сохраняя порядок внутри раздела.</summary>
    public static void Normalize(Specification spec, SpecificationContext context)
    {
        NormalizeGroups(spec);
        SpecBlocks.All(spec, context);
        spec.Rows = spec.Rows
            .Select((row, index) => (row, index))
            .OrderBy(x => SpecBlocks.Order(x.row, context))
            .ThenBy(x => x.index)
            .Select(x => x.row)
            .ToList();
    }

    /// <summary>Строки удалённых групп возвращаются в основную часть.</summary>
    public static void NormalizeGroups(Specification spec)
    {
        foreach (var row in spec.Rows.Where(r => r.GroupId.Length > 0 && spec.Groups.All(g => g.Id != r.GroupId)))
        {
            row.GroupId = "";
        }
    }

    /// <summary>Добавляет группу «устанавливаемые по …» в конец спецификации.</summary>
    public static SpecGroup AddGroup(Specification spec, string title)
    {
        var group = new SpecGroup { Title = title.Trim() };
        spec.Groups.Add(group);
        return group;
    }

    /// <summary>Удаляет группу; её строки переходят в конец своих разделов основной части и получают новые номера.</summary>
    public static void RemoveGroup(Specification spec, SpecificationContext context, string groupId)
    {
        var rows = spec.Rows.Where(r => r.GroupId == groupId).ToList();
        spec.Groups.RemoveAll(g => g.Id == groupId);
        foreach (var row in rows)
        {
            MoveToGroup(spec, context, row, "", assignPositions: false);
        }

        PositionAllocator.AssignMissing(spec, context);
    }

    /// <summary>
    /// Переносит строку в группу «устанавливаемые по …» (или в основную часть, если <paramref name="groupId"/> пуст):
    /// в конец того же раздела группы, с новым номером позиции. Хранится только в спецификации.
    /// </summary>
    public static void MoveToGroup(Specification spec, SpecificationContext context, SpecRow row, string groupId) =>
        MoveToGroup(spec, context, row, groupId, assignPositions: true);

    /// <summary>Вставляет строку в конец её раздела (в её группе).</summary>
    internal static void InsertAtSectionEnd(Specification spec, SpecificationContext context, SpecRow row)
    {
        var last = LastIndexOfBlock(spec, row);
        if (last >= 0)
        {
            spec.Rows.Insert(last, row);
            return;
        }

        SpecBlocks.All(spec, context);
        var order = SpecBlocks.Order(row, context);
        var index = spec.Rows.FindIndex(r => SpecBlocks.Order(r, context) > order);
        spec.Rows.Insert(index < 0 ? spec.Rows.Count : index, row);
    }

    private static void Insert(Specification spec, SpecificationContext context, SpecRow row, string? afterRowId)
    {
        var after = afterRowId is null ? null : spec.FindRow(afterRowId);
        if (after is not null && SpecBlocks.Same(after, row))
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

    private static void MoveToGroup(Specification spec, SpecificationContext context, SpecRow row, string groupId, bool assignPositions)
    {
        if (groupId.Length > 0 && context.FindGroup(groupId) is null)
        {
            throw new ArgumentException($"Group '{groupId}' not found.", nameof(groupId));
        }

        if (row.GroupId == groupId && spec.Rows.Contains(row))
        {
            return;
        }

        spec.Rows.Remove(row);
        row.GroupId = groupId;
        row.Position = null;
        InsertAtSectionEnd(spec, context, row);
        if (assignPositions)
        {
            PositionAllocator.AssignMissing(spec, context);
        }
    }

    /// <summary>Индекс сразу после последней строки того же раздела той же группы (или -1, если таких строк нет).</summary>
    private static int LastIndexOfBlock(Specification spec, SpecRow row)
    {
        var last = spec.Rows.FindLastIndex(r => SpecBlocks.Same(r, row));
        return last < 0 ? -1 : last + 1;
    }

    private static List<int> NumberedPositions(Specification spec, SpecRow row) =>
        BlockNumberedRows(spec, row).Select(r => r.Position!.Value).OrderBy(p => p).ToList();

    private static void ReapplyPositions(Specification spec, SpecRow row, List<int> positions)
    {
        var rows = BlockNumberedRows(spec, row).ToList();
        for (var i = 0; i < rows.Count && i < positions.Count; i++)
        {
            rows[i].Position = positions[i];
        }
    }

    private static IEnumerable<SpecRow> BlockNumberedRows(Specification spec, SpecRow row) =>
        spec.Rows.Where(r => r.Position.HasValue && !r.IsRemoved && SpecBlocks.Same(r, row));

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
