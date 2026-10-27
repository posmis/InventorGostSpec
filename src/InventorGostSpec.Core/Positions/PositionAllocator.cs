using InventorGostSpec.Core.Model;
using InventorGostSpec.Core.Sections;

namespace InventorGostSpec.Core.Positions;

public sealed class PositionAssignmentResult
{
    /// <summary>Разделы, в которых для новых строк не хватило резервных номеров.</summary>
    public List<string> ExhaustedSectionIds { get; } = new();

    /// <summary>Строки, оставшиеся без номера позиции.</summary>
    public List<SpecRow> UnassignedRows { get; } = new();

    public bool IsComplete => UnassignedRows.Count == 0;
}

/// <summary>Нумерация позиций с резервом номеров между разделами.</summary>
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

        foreach (var section in context.Catalog.Sections.Where(s => s.HasPositions))
        {
            var rows = SectionRows(spec, section).ToList();
            if (rows.Count == 0)
            {
                continue;
            }

            var reserve = context.GetReserve(section.Id);
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
        var ordered = context.Catalog.Sections
            .Where(s => s.HasPositions)
            .SelectMany(s => SectionRows(spec, s).Select(r => (Section: s, Row: r)))
            .ToList();

        var used = new HashSet<int>(ordered.Where(x => x.Row.Position.HasValue).Select(x => x.Row.Position!.Value));
        var previousMax = 0;
        string? currentSection = null;
        var sectionHasNumbers = false;

        for (var i = 0; i < ordered.Count; i++)
        {
            var (section, row) = ordered[i];
            if (!string.Equals(section.Id, currentSection, StringComparison.OrdinalIgnoreCase))
            {
                currentSection = section.Id;
                sectionHasNumbers = false;
            }

            if (row.Position is { } existing)
            {
                previousMax = Math.Max(previousMax, existing);
                sectionHasNumbers = true;
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
            if (!sectionHasNumbers && previousMax > 0)
            {
                var rounded = RoundUp(lower, context.GetReserve(section.Id).RoundStartTo);
                if (rounded <= upper)
                {
                    lower = rounded;
                }
            }

            var candidate = lower;
            while (candidate <= upper && used.Contains(candidate))
            {
                candidate++;
            }

            if (candidate > upper)
            {
                result.UnassignedRows.Add(row);
                if (!result.ExhaustedSectionIds.Contains(section.Id))
                {
                    result.ExhaustedSectionIds.Add(section.Id);
                }

                continue;
            }

            row.Position = candidate;
            used.Add(candidate);
            previousMax = candidate;
            sectionHasNumbers = true;
        }

        foreach (var row in spec.Rows.Where(r => !IsNumbered(r, context)))
        {
            row.Position = null;
        }

        return result;
    }

    /// <summary>
    /// Свободные номера после последней позиции раздела и до первой позиции следующих разделов.
    /// Для последнего раздела — столько номеров, сколько задано резервом.
    /// </summary>
    public static IReadOnlyList<int> GetReservedNumbers(Specification spec, SpecificationContext context, string sectionId)
    {
        var sections = context.Catalog.Sections.Where(s => s.HasPositions).ToList();
        var index = sections.FindIndex(s => string.Equals(s.Id, sectionId, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return Array.Empty<int>();
        }

        var positions = SectionRows(spec, sections[index]).Where(r => r.Position.HasValue).Select(r => r.Position!.Value).ToList();
        if (positions.Count == 0)
        {
            return Array.Empty<int>();
        }

        var used = new HashSet<int>(spec.Rows.Where(r => r.Position.HasValue).Select(r => r.Position!.Value));
        var start = positions.Max() + 1;
        var later = sections.Skip(index + 1)
            .SelectMany(s => SectionRows(spec, s))
            .Where(r => r.Position.HasValue)
            .Select(r => r.Position!.Value)
            .DefaultIfEmpty(0)
            .Min();

        var end = later > 0 ? later - 1 : start + Math.Max(0, context.GetReserve(sectionId).Count) - 1;
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

    private static IEnumerable<SpecRow> SectionRows(Specification spec, SectionDefinition section) =>
        spec.Rows.Where(r => r.CanHavePosition && string.Equals(r.SectionId, section.Id, StringComparison.OrdinalIgnoreCase));

    private static int RoundUp(int value, int step)
    {
        if (step <= 1 || value % step == 0)
        {
            return value;
        }

        return (value / step + 1) * step;
    }
}
