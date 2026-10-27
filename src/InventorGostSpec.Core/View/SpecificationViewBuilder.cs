using InventorGostSpec.Core.Editing;
using InventorGostSpec.Core.Model;
using InventorGostSpec.Core.Positions;

namespace InventorGostSpec.Core.View;

public enum DisplayRowKind
{
    /// <summary>Заголовок группы «устанавливаемые по …».</summary>
    GroupHeader,

    SectionHeader,
    Row,

    /// <summary>Пустая строка с резервным номером позиции.</summary>
    Reserve,
}

/// <param name="GroupId">Группа строки; пусто — основная часть.</param>
/// <param name="SectionName">Название раздела; у заголовка группы — заголовок группы.</param>
public sealed record DisplayRow(
    DisplayRowKind Kind,
    string GroupId,
    string SectionId,
    string SectionName,
    SpecRow? Row,
    int? ReservedPosition);

/// <summary>
/// Строки для отображения: разделы основной части, затем группы «устанавливаемые по …» со своими разделами,
/// заголовки и резервные позиции.
/// </summary>
public static class SpecificationViewBuilder
{
    public static IReadOnlyList<DisplayRow> Build(Specification spec, SpecificationContext context)
    {
        SpecificationEditor.NormalizeGroups(spec);
        var result = new List<DisplayRow>();
        string? currentGroup = null;

        foreach (var block in SpecBlocks.All(spec, context))
        {
            var section = block.Section;
            if (block.GroupId != currentGroup)
            {
                currentGroup = block.GroupId;
                if (context.FindGroup(block.GroupId) is { } group)
                {
                    result.Add(new DisplayRow(DisplayRowKind.GroupHeader, group.Id, "", group.Title, null, null));
                }
            }

            var rows = spec.Rows.Where(block.Contains).ToList();
            var isCustom = block.GroupId.Length == 0 &&
                           spec.CustomSections.Any(s => string.Equals(s.Id, section.Id, StringComparison.OrdinalIgnoreCase));
            if (rows.Count == 0 && !isCustom)
            {
                continue;
            }

            result.Add(new DisplayRow(DisplayRowKind.SectionHeader, block.GroupId, section.Id, section.Name, null, null));
            result.AddRange(rows.Select(r => new DisplayRow(DisplayRowKind.Row, block.GroupId, section.Id, section.Name, r, null)));

            if (spec.ShowReserveRows && section.HasPositions)
            {
                result.AddRange(PositionAllocator.GetReservedNumbers(spec, context, block.GroupId, section.Id)
                    .Select(n => new DisplayRow(DisplayRowKind.Reserve, block.GroupId, section.Id, section.Name, null, n)));
            }
        }

        return result;
    }
}
