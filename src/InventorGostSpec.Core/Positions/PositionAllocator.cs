using InventorGostSpec.Core.Model;
using InventorGostSpec.Core.Versions;

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
            var reserve = context.GetReserve(block.GroupId, block.Section.Id);
            if (rows.Count == 0)
            {
                // Пустой вставленный раздел: под него отводится только его резерв.
                if (SpecBlocks.IsShownWhenEmpty(spec, block))
                {
                    if (!first)
                    {
                        next = RoundUp(next, reserve.RoundStartTo);
                    }

                    next += Math.Max(0, reserve.Count);
                    first = false;
                }

                continue;
            }

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

        VersionSync.ApplyInheritedPositions(spec);
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

        // Резерв пустых вставленных разделов занят — его номера получат только строки этих разделов.
        var emptyReserves = ReserveLayout(spec, context)
            .Where(x => x.Value.Empty)
            .ToDictionary(x => x.Key, x => x.Value.Numbers);
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
            var key = Key(block);
            bool Taken(int n) => used.Contains(n) || emptyReserves.Any(x => x.Key != key && x.Value.Contains(n));
            while (candidate <= upper && Taken(candidate))
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

        VersionSync.ApplyInheritedPositions(spec);
        return result;
    }

    /// <summary>
    /// Свободные номера после последней позиции раздела основной части и до первой позиции следующих разделов.
    /// </summary>
    public static IReadOnlyList<int> GetReservedNumbers(Specification spec, SpecificationContext context, string sectionId) =>
        GetReservedNumbers(spec, context, "", sectionId);

    /// <summary>
    /// Резервные номера раздела в группе (или основной части): свободные номера после его последней позиции
    /// до следующего раздела, для последнего раздела — сколько задано резервом. У пустого вставленного раздела —
    /// номера, отведённые под его резерв (после резерва предыдущего раздела).
    /// </summary>
    public static IReadOnlyList<int> GetReservedNumbers(Specification spec, SpecificationContext context, string groupId, string sectionId) =>
        ReserveLayout(spec, context).TryGetValue(Key(groupId, sectionId), out var reserve) ? reserve.Numbers : Array.Empty<int>();

    /// <summary>
    /// Резерв всех разделов за один проход, в порядке спецификации, так же как при перенумерации:
    /// после раздела — его резерв, пустой вставленный раздел занимает номера своего резерва.
    /// </summary>
    private static Dictionary<string, (List<int> Numbers, bool Empty)> ReserveLayout(Specification spec, SpecificationContext context)
    {
        var used = spec.Rows.Where(r => r.Position.HasValue).Select(r => r.Position!.Value).ToHashSet();
        var segments = NumberedBlocks(spec, context)
            .Select(b => (Block: b, Positions: BlockRows(spec, b).Where(r => r.Position.HasValue).Select(r => r.Position!.Value).ToList()))
            .Where(s => s.Positions.Count > 0 || SpecBlocks.IsShownWhenEmpty(spec, s.Block))
            .ToList();

        List<int> Free(int from, int to) =>
            from > to ? new List<int>() : Enumerable.Range(from, to - from + 1).Where(n => !used.Contains(n)).ToList();
        ReserveSettings Reserve(SpecBlock block) => context.GetReserve(block.GroupId, block.Section.Id);

        var result = new Dictionary<string, (List<int>, bool)>();
        (SpecBlock Block, int Max)? pending = null;
        var cursor = 0;
        for (var i = 0; i < segments.Count; i++)
        {
            var (block, positions) = segments[i];
            if (positions.Count > 0)
            {
                if (pending is { } previous)
                {
                    result[Key(previous.Block)] = (Free(previous.Max + 1, positions.Min() - 1), false);
                }

                pending = (block, positions.Max());
                cursor = Math.Max(cursor, positions.Max());
                continue;
            }

            // Пустой вставленный раздел: номера — до первой позиции следующих заполненных разделов.
            var limit = segments.Skip(i + 1).SelectMany(s => s.Positions).DefaultIfEmpty(int.MaxValue).Min() - 1;
            int start;
            if (pending is { } before)
            {
                var reserveEnd = before.Max + Math.Max(0, Reserve(before.Block).Count);
                result[Key(before.Block)] = (Free(before.Max + 1, Math.Min(reserveEnd, limit)), false);
                start = reserveEnd + 1;
                pending = null;
            }
            else
            {
                start = cursor + 1;
            }

            var reserve = Reserve(block);
            if (i > 0)
            {
                start = RoundUp(start, reserve.RoundStartTo);
            }

            var end = (int)Math.Min((long)start + Math.Max(0, reserve.Count) - 1, limit);
            result[Key(block)] = (Free(start, end), true);
            cursor = Math.Max(cursor, Math.Max(end, start - 1));
        }

        if (pending is { } last)
        {
            result[Key(last.Block)] = (Free(last.Max + 1, last.Max + Math.Max(0, Reserve(last.Block).Count)), false);
        }

        return result;
    }

    private static string Key(SpecBlock block) => Key(block.GroupId, block.Section.Id);

    private static string Key(string groupId, string sectionId) => groupId + "\n" + sectionId.ToUpperInvariant();

    /// <summary>Получает ли строка номер позиции: не текстовая строка и раздел с позициями.</summary>
    public static bool IsNumbered(SpecRow row, SpecificationContext context) =>
        row.CanHavePosition && context.Catalog.GetOrCreate(row.SectionId).HasPositions;

    private static IEnumerable<SpecBlock> NumberedBlocks(Specification spec, SpecificationContext context) =>
        SpecBlocks.All(spec, context).Where(b => b.Section.HasPositions);

    /// <summary>Строки блока, которые нумеруются сами (номер строк исполнений, наследующих номер, копируется потом).</summary>
    private static IEnumerable<SpecRow> BlockRows(Specification spec, SpecBlock block) =>
        spec.Rows.Where(r => r.CanHavePosition && !r.InheritsPosition && block.Contains(r));

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
