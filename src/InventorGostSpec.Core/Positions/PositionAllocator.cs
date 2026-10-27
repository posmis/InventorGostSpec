using InventorGostSpec.Core.Model;

namespace InventorGostSpec.Core.Positions;

public sealed class PositionAssignmentResult
{
    /// <summary>Разделы, в которых для новых строк не хватило резервных номеров.</summary>
    public List<string> ExhaustedSectionIds { get; } = new();

    /// <summary>Блоки (раздел основной части или группы), в которых не хватило резервных номеров.</summary>
    public List<SpecBlock> ExhaustedBlocks { get; } = new();

    /// <summary>Строки, оставшиеся без номера позиции.</summary>
    public List<SpecRow> UnassignedRows { get; } = new();

    public bool IsComplete => UnassignedRows.Count == 0;
}

/// <summary>
/// Нумерация позиций с резервом номеров между разделами. Сначала нумеруется основная часть,
/// затем группы «устанавливаемые по …» — номера в них продолжаются подряд.
/// </summary>
public static class PositionAllocator
{
    /// <summary>
    /// Перенумеровывает все позиции в текущем порядке строк.
    /// После каждого раздела пропускается резерв, начало раздела при необходимости округляется.
    /// Строки удалённых компонентов номер теряют.
    /// </summary>
    public static void RenumberAll(Specification spec, SpecificationContext context)
    {
        var next = 1;
        var first = true;

        foreach (var row in spec.Rows.Where(r => !IsNumbered(r, context)))
        {
            row.Position = null;
        }

        foreach (var block in NumberedBlocks(spec, context))
        {
            var rows = BlockRows(spec, block).ToList();
            if (rows.Count == 0)
            {
                continue;
            }

            var reserve = context.GetReserve(block.GroupId, block.Section.Id);
            if (!first)
            {
                next = RoundUp(next, reserve.RoundStartTo);
            }

            foreach (var row in rows)
            {
                row.Position = row.IsRemoved ? null : next++;
            }

            next += Math.Max(0, reserve.Count);
            first = false;
        }
    }

    /// <summary>
    /// Выдаёт номера строкам без позиции, не трогая уже выданные номера.
    /// Номер берётся из свободных между предыдущей и следующей пронумерованной строкой,
    /// то есть из резерва своего раздела. Номера удалённых компонентов считаются занятыми.
    /// </summary>
    public static PositionAssignmentResult AssignMissing(Specification spec, SpecificationContext context)
    {
        var result = new PositionAssignmentResult();
        var ordered = NumberedBlocks(spec, context)
            .SelectMany(b => BlockRows(spec, b).Select(r => (Block: b, Row: r)))
            .ToList();

        var used = new HashSet<int>(ordered.Where(x => x.Row.Position.HasValue).Select(x => x.Row.Position!.Value));
        var previousMax = 0;
        SpecBlock? currentBlock = null;
        SpecBlock? lastNumberedBlock = null;
        var blockHasNumbers = false;

        for (var i = 0; i < ordered.Count; i++)
        {
            var (block, row) = ordered[i];
            if (block != currentBlock)
            {
                currentBlock = block;
                blockHasNumbers = false;
            }

            if (row.Position is { } existing)
            {
                previousMax = Math.Max(previousMax, existing);
                lastNumberedBlock = block;
                blockHasNumbers = true;
                continue;
            }

            if (row.IsRemoved)
            {
                continue;
            }

            var upper = int.MaxValue;
            for (var k = i + 1; k < ordered.Count; k++)
            {
                if (ordered[k].Row.Position is { } later)
                {
                    upper = Math.Min(upper, later - 1);
                }
            }

            var lower = previousMax + 1;
            if (!blockHasNumbers && lastNumberedBlock is not null)
            {
                // Первая строка раздела: резерв предыдущего раздела не занимаем — как при перенумерации.
                var reserve = Math.Max(0, context.GetReserve(lastNumberedBlock.GroupId, lastNumberedBlock.Section.Id).Count);
                if (lower + reserve <= upper)
                {
                    lower += reserve;
                }

                lower = RoundUpWithin(lower, context.GetReserve(block.GroupId, block.Section.Id).RoundStartTo, upper);
            }

            var candidate = lower;
            while (candidate <= upper && used.Contains(candidate))
            {
                candidate++;
            }

            if (candidate > upper)
            {
                result.UnassignedRows.Add(row);
                if (!result.ExhaustedBlocks.Contains(block))
                {
                    result.ExhaustedBlocks.Add(block);
                }

                if (!result.ExhaustedSectionIds.Contains(block.Section.Id))
                {
                    result.ExhaustedSectionIds.Add(block.Section.Id);
                }

                continue;
            }

            row.Position = candidate;
            used.Add(candidate);
            previousMax = candidate;
            lastNumberedBlock = block;
            blockHasNumbers = true;
        }

        foreach (var row in spec.Rows.Where(r => !IsNumbered(r, context)))
        {
            row.Position = null;
        }

        return result;
    }

    /// <summary>
    /// Свободные номера после последней позиции раздела основной части и до первой позиции следующих разделов.
    /// </summary>
    public static IReadOnlyList<int> GetReservedNumbers(Specification spec, SpecificationContext context, string sectionId) =>
        GetReservedNumbers(spec, context, "", sectionId);

    /// <summary>
    /// Свободные номера после последней позиции раздела в группе (или основной части) и до первой позиции
    /// следующих блоков. Для последнего блока — столько номеров, сколько задано резервом.
    /// </summary>
    public static IReadOnlyList<int> GetReservedNumbers(Specification spec, SpecificationContext context, string groupId, string sectionId)
    {
        var blocks = NumberedBlocks(spec, context).ToList();
        var index = blocks.FindIndex(b => b.GroupId == groupId && string.Equals(b.Section.Id, sectionId, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return Array.Empty<int>();
        }

        var positions = BlockRows(spec, blocks[index]).Where(r => r.Position.HasValue).Select(r => r.Position!.Value).ToList();
        if (positions.Count == 0)
        {
            return Array.Empty<int>();
        }

        var used = new HashSet<int>(spec.Rows.Where(r => r.Position.HasValue).Select(r => r.Position!.Value));
        var start = positions.Max() + 1;
        var later = blocks.Skip(index + 1)
            .SelectMany(b => BlockRows(spec, b))
            .Where(r => r.Position.HasValue)
            .Select(r => r.Position!.Value)
            .DefaultIfEmpty(0)
            .Min();

        var end = later > 0 ? later - 1 : start + Math.Max(0, context.GetReserve(groupId, sectionId).Count) - 1;
        var result = new List<int>();
        for (var n = start; n <= end; n++)
        {
            if (!used.Contains(n))
            {
                result.Add(n);
            }
        }

        return result;
    }

    /// <summary>Получает ли строка номер позиции: не текстовая строка и раздел с позициями.</summary>
    public static bool IsNumbered(SpecRow row, SpecificationContext context) =>
        row.CanHavePosition && context.Catalog.GetOrCreate(row.SectionId).HasPositions;

    private static IEnumerable<SpecBlock> NumberedBlocks(Specification spec, SpecificationContext context) =>
        SpecBlocks.All(spec, context).Where(b => b.Section.HasPositions);

    private static IEnumerable<SpecRow> BlockRows(Specification spec, SpecBlock block) =>
        spec.Rows.Where(r => r.CanHavePosition && block.Contains(r));

    private static int RoundUpWithin(int value, int step, int upper)
    {
        var rounded = RoundUp(value, step);
        return rounded <= upper ? rounded : value;
    }

    private static int RoundUp(int value, int step)
    {
        if (step <= 1 || value % step == 0)
        {
            return value;
        }

        return (value / step + 1) * step;
    }
}
