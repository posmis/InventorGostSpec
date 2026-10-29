using InventorGostSpec.Core.Editing;
using InventorGostSpec.Core.Model;
using InventorGostSpec.Core.Positions;
using InventorGostSpec.Core.Versions;

namespace InventorGostSpec.Core.View;

public enum DisplayRowKind
{
    /// <summary>Заголовок группы «устанавливаемые по …».</summary>
    GroupHeader,

    SectionHeader,
    Row,

    /// <summary>Пустая строка с резервным номером позиции.</summary>
    Reserve,

    /// <summary>Общий заголовок «Переменные данные для исполнений» (ГОСТ 2.113, вариант А).</summary>
    VersionsHeader,

    /// <summary>Служебная надпись в графе «Наименование»: «Отсутствуют», «Различия исполнений … по сборочному чертежу».</summary>
    Notice,

    /// <summary>
    /// «Общие компоненты (постоянные данные)» — уровень исполнений в окне. Только в окне: в Excel и PDF не выводится.
    /// </summary>
    CommonHeader,
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
        var blocks = SpecBlocks.All(spec, context);
        var versions = VersionSync.IsShown(spec) ? VersionSync.Versions(spec).ToList() : new List<SpecGroup>();
        var versionsHeaderShown = false;
        var hidden = new HashSet<string>();

        // Есть ли у исполнения данные: в нём самом или в его группах «устанавливаемые по …».
        if (versions.Count > 0)
        {
            result.Add(new DisplayRow(DisplayRowKind.CommonHeader, "", "", VersionSync.CommonTitle, null, null));
        }

        bool HasData(SpecGroup version) =>
            VersionSync.Scope(spec, version).Any(id => blocks.Any(b => b.GroupId == id && SpecBlocks.IsVisible(spec, b)));

        foreach (var groupId in new[] { "" }.Concat(context.Groups.Select(g => g.Id)))
        {
            var group = context.FindGroup(groupId);
            var groupBlocks = blocks.Where(b => b.GroupId == groupId).ToList();
            if (group is { IsVersion: true })
            {
                // Основное исполнение без других исполнений не показывается отдельно (его строки — постоянные данные).
                if (versions.Count == 0)
                {
                    AddBlocks(spec, context, groupBlocks, result);
                    continue;
                }

                if (!versionsHeaderShown)
                {
                    versionsHeaderShown = true;
                    if (VersionSync.HasVersions(spec) && versions.All(v => !HasData(v)))
                    {
                        // Переменных данных нет ни у одного исполнения (п. 5.5.1 е).
                        result.Add(new DisplayRow(DisplayRowKind.Notice, "", "", DifferencesNotice(versions), null, null));
                        break;
                    }

                    result.Add(new DisplayRow(DisplayRowKind.VersionsHeader, "", "", VersionSync.VariableDataTitle, null, null));
                }

                var sameAs = VersionSync.SameAs(spec, group);
                result.Add(new DisplayRow(DisplayRowKind.GroupHeader, group.Id, "",
                    sameAs is null ? group.Title : $"{group.Title} (тоже как для {sameAs.Title})", null, null));
                if (sameAs is not null)
                {
                    hidden.Add(group.Id);
                    continue;
                }

                if (!HasData(group))
                {
                    result.Add(new DisplayRow(DisplayRowKind.Notice, group.Id, "", "Отсутствуют", null, null));
                    continue;
                }

                AddBlocks(spec, context, groupBlocks, result);
                continue;
            }

            // Группа внутри исполнения, данные которого не повторяются («тоже как для …»), не показывается.
            if (group is { VersionId.Length: > 0 } && hidden.Contains(group.VersionId))
            {
                continue;
            }

            if (group is not null)
            {
                result.Add(new DisplayRow(DisplayRowKind.GroupHeader, group.Id, "", group.Title, null, null));
            }

            AddBlocks(spec, context, groupBlocks, result);
        }

        return result;
    }

    /// <summary>«Различия исполнений А и А-01 по сборочному чертежу».</summary>
    private static string DifferencesNotice(IReadOnlyList<SpecGroup> versions)
    {
        var names = versions.Select(v => v.Title).ToList();
        var list = names.Count <= 1 ? string.Join("", names) : string.Join(", ", names.Take(names.Count - 1)) + " и " + names[^1];
        return $"Различия исполнений {list} по сборочному чертежу";
    }

    private static void AddBlocks(Specification spec, SpecificationContext context, IEnumerable<SpecBlock> blocks, List<DisplayRow> result)
    {
        foreach (var block in blocks)
        {
            var section = block.Section;
            var rows = spec.Rows.Where(block.Contains).ToList();
            if (rows.Count == 0 && !SpecBlocks.IsShownWhenEmpty(spec, block))
            {
                continue;
            }

            result.Add(new DisplayRow(DisplayRowKind.SectionHeader, block.GroupId, section.Id, section.Name, null, null));
            result.AddRange(rows.Select(r => new DisplayRow(DisplayRowKind.Row, block.GroupId, section.Id, section.Name, r, null)));

            if (!spec.ShowReserveRows)
            {
                continue;
            }

            if (section.HasPositions)
            {
                result.AddRange(PositionAllocator.GetReservedNumbers(spec, context, block.GroupId, section.Id)
                    .Select(n => new DisplayRow(DisplayRowKind.Reserve, block.GroupId, section.Id, section.Name, null, n)));
            }
            else
            {
                // Раздел без позиций («Документация»): резерв — свободные строки без номера.
                var count = Math.Max(0, context.GetReserve(block.GroupId, section.Id).Count);
                result.AddRange(Enumerable.Range(0, count)
                    .Select(_ => new DisplayRow(DisplayRowKind.Reserve, block.GroupId, section.Id, section.Name, null, null)));
            }
        }
    }
}
