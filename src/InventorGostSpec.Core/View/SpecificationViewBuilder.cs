using InventorGostSpec.Core.Editing;
using InventorGostSpec.Core.Model;
using InventorGostSpec.Core.Positions;

namespace InventorGostSpec.Core.View;

public enum DisplayRowKind
{
    SectionHeader,
    Row,

    /// <summary>Пустая строка с резервным номером позиции.</summary>
    Reserve,
}

public sealed record DisplayRow(DisplayRowKind Kind, string SectionId, string SectionName, SpecRow? Row, int? ReservedPosition);

/// <summary>Строки для отображения: заголовки разделов, строки и резервные позиции.</summary>
public static class SpecificationViewBuilder
{
    public static IReadOnlyList<DisplayRow> Build(Specification spec, SpecificationContext context)
    {
        var result = new List<DisplayRow>();
        foreach (var section in SpecificationEditor.OrderedSections(spec, context))
        {
            var rows = spec.Rows
                .Where(r => string.Equals(r.SectionId, section.Id, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var isCustom = spec.CustomSections.Any(s => string.Equals(s.Id, section.Id, StringComparison.OrdinalIgnoreCase));
            if (rows.Count == 0 && !isCustom)
            {
                continue;
            }

            result.Add(new DisplayRow(DisplayRowKind.SectionHeader, section.Id, section.Name, null, null));
            result.AddRange(rows.Select(r => new DisplayRow(DisplayRowKind.Row, section.Id, section.Name, r, null)));

            if (spec.ShowReserveRows && section.HasPositions)
            {
                result.AddRange(PositionAllocator.GetReservedNumbers(spec, context, section.Id)
                    .Select(n => new DisplayRow(DisplayRowKind.Reserve, section.Id, section.Name, null, n)));
            }
        }

        return result;
    }
}
