using InventorGostSpec.Core.Model;
using InventorGostSpec.Core.Positions;
using InventorGostSpec.Core.Sections;
using InventorGostSpec.Core.Sorting;

namespace InventorGostSpec.Core.Checking;

public enum IssueSeverity
{
    /// <summary>Ошибка: документ выпускать нельзя (нет позиции, повтор позиции, нет наименования).</summary>
    Error,

    /// <summary>Предупреждение: скорее всего, нужно исправить.</summary>
    Warning,

    /// <summary>Замечание для сведения.</summary>
    Info,
}

/// <summary>Замечание проверки. <see cref="RowId"/> — строка, к которой оно относится (<c>null</c> — к документу).</summary>
public sealed record SpecIssue(IssueSeverity Severity, string Message, string? RowId = null);

/// <summary>Данные из Inventor для проверки; всё необязательно — без них соответствующие проверки пропускаются.</summary>
public sealed class CheckInput
{
    /// <summary>Текущий состав сборки: компоненты без раздела в iProperties.</summary>
    public IReadOnlyList<ComponentData>? Components { get; init; }

    /// <summary>Номера позиций в BOM Inventor (Item Number) по ключу компонента.</summary>
    public IReadOnlyDictionary<string, string>? BomItemNumbers { get; init; }

    /// <summary>Данные основной надписи.</summary>
    public TitleBlock? TitleBlock { get; init; }
}

/// <summary>Проверка спецификации перед выпуском: позиции, обязательные графы, повторы, расхождения с BOM.</summary>
public static class SpecificationChecker
{
    /// <summary>Разделы, где обязательно обозначение (ГОСТ 2.106: документы, комплексы, сборочные единицы, детали, комплекты).</summary>
    private static readonly HashSet<string> DesignationRequired = new(StringComparer.OrdinalIgnoreCase)
    {
        SectionIds.Documentation, SectionIds.Complexes, SectionIds.AssemblyUnits, SectionIds.Parts, SectionIds.Kits,
    };

    /// <summary>Разделы, где обязательна графа «Формат» (для детали без чертежа пишут «БЧ»).</summary>
    private static readonly HashSet<string> FormatRequired = new(StringComparer.OrdinalIgnoreCase)
    {
        SectionIds.Documentation, SectionIds.Complexes, SectionIds.AssemblyUnits, SectionIds.Parts,
    };

    public static List<SpecIssue> Check(Specification spec, SpecificationContext context, CheckInput? input = null)
    {
        input ??= new CheckInput();
        var issues = new List<SpecIssue>();
        var rows = SpecBlocks.All(spec, context)
            .SelectMany(block => spec.Rows.Where(block.Contains))
            .Where(r => r.Kind != RowKind.Note)
            .ToList();
        var active = rows.Where(r => !r.IsRemoved).ToList();

        CheckChanges(rows, issues);
        CheckPositions(rows, active, context, issues);
        CheckCells(active, issues);
        CheckDuplicateDesignations(active, issues);
        CheckBom(active, input.BomItemNumbers, issues);
        CheckComponentSections(active, input.Components, context, issues);
        CheckGroups(spec, issues);
        CheckTitleBlock(input.TitleBlock, issues);

        // Сначала ошибки, затем предупреждения и замечания; внутри — в порядке таблицы.
        var order = rows.Select((r, i) => (r.Id, i)).ToDictionary(x => x.Id, x => x.i);
        return issues
            .Select((issue, index) => (issue, index))
            .OrderBy(x => x.issue.Severity)
            .ThenBy(x => x.issue.RowId is { } id && order.TryGetValue(id, out var position) ? position : -1)
            .ThenBy(x => x.index)
            .Select(x => x.issue)
            .ToList();
    }

    /// <summary>Как строка называется в сообщении: «поз. 5 АБВГ.715431.001 Корпус».</summary>
    public static string Describe(SpecRow row)
    {
        var parts = new List<string>();
        if (row.Position is { } position)
        {
            parts.Add($"поз. {position}");
        }

        parts.AddRange(new[] { row.Get(SpecColumn.Designation), row.Get(SpecColumn.Name) }.Where(s => s.Length > 0));
        return parts.Count > 0 ? string.Join(" ", parts) : "(пустая строка)";
    }

    private static void CheckChanges(List<SpecRow> rows, List<SpecIssue> issues)
    {
        foreach (var row in rows.Where(r => r.IsRemoved))
        {
            issues.Add(new SpecIssue(IssueSeverity.Warning,
                $"{Describe(row)}: компонента нет в сборке. «Принять изменения» удалит строку.", row.Id));
        }

        var pending = rows.Where(r => !r.IsRemoved && r.Status != RowStatus.None).ToList();
        if (pending.Count > 0)
        {
            issues.Add(new SpecIssue(IssueSeverity.Info,
                $"Не приняты изменения из сборки (новые и изменённые строки): {pending.Count}.", pending[0].Id));
        }
    }

    private static void CheckPositions(List<SpecRow> rows, List<SpecRow> active, SpecificationContext context, List<SpecIssue> issues)
    {
        foreach (var row in active.Where(r => r.Position is null && PositionAllocator.IsNumbered(r, context)))
        {
            issues.Add(new SpecIssue(IssueSeverity.Error, $"{Describe(row)}: нет номера позиции.", row.Id));
        }

        // Номера удалённых строк тоже заняты: они остаются на выносках, пока изменения не приняты.
        foreach (var duplicate in rows.Where(r => r.Position.HasValue).GroupBy(r => r.Position!.Value).Where(g => g.Count() > 1))
        {
            var list = duplicate.ToList();
            foreach (var row in list.Skip(1))
            {
                issues.Add(new SpecIssue(IssueSeverity.Error,
                    $"{Describe(row)}: позиция {duplicate.Key} повторяется (уже у «{Describe(list[0])}»).", row.Id));
            }
        }

        // ГОСТ 2.106: позиции идут в порядке записи.
        var previous = 0;
        foreach (var row in active.Where(r => r.Position.HasValue))
        {
            if (row.Position!.Value < previous)
            {
                issues.Add(new SpecIssue(IssueSeverity.Warning,
                    $"{Describe(row)}: позиция меньше предыдущей ({previous}) — позиции не по порядку записи. Перенумеруйте.", row.Id));
            }

            previous = Math.Max(previous, row.Position.Value);
        }
    }

    private static void CheckCells(List<SpecRow> active, List<SpecIssue> issues)
    {
        foreach (var row in active)
        {
            if (row.Get(SpecColumn.Name).Length == 0)
            {
                issues.Add(new SpecIssue(IssueSeverity.Error, $"{Describe(row)}: нет наименования.", row.Id));
            }

            if (DesignationRequired.Contains(row.SectionId) && row.Get(SpecColumn.Designation).Length == 0)
            {
                issues.Add(new SpecIssue(IssueSeverity.Warning, $"{Describe(row)}: нет обозначения.", row.Id));
            }

            if (FormatRequired.Contains(row.SectionId) && row.Get(SpecColumn.Format).Length == 0)
            {
                issues.Add(new SpecIssue(IssueSeverity.Warning,
                    $"{Describe(row)}: не указан формат (для детали без чертежа — «БЧ»).", row.Id));
            }

            if (!string.Equals(row.SectionId, SectionIds.Documentation, StringComparison.OrdinalIgnoreCase) &&
                row.Get(SpecColumn.Quantity).Length == 0)
            {
                issues.Add(new SpecIssue(IssueSeverity.Warning, $"{Describe(row)}: не указано количество.", row.Id));
            }
        }
    }

    private static void CheckDuplicateDesignations(List<SpecRow> active, List<SpecIssue> issues)
    {
        var groups = active
            .Where(r => r.Get(SpecColumn.Designation).Length > 0)
            .GroupBy(r => NaturalComparer.Normalize(r.Get(SpecColumn.Designation)));
        foreach (var group in groups.Where(g => g.Count() > 1))
        {
            var list = group.ToList();
            foreach (var row in list.Skip(1))
            {
                issues.Add(new SpecIssue(IssueSeverity.Warning,
                    $"{Describe(row)}: обозначение повторяется (уже у «{Describe(list[0])}»).", row.Id));
            }
        }
    }

    private static void CheckBom(List<SpecRow> active, IReadOnlyDictionary<string, string>? bom, List<SpecIssue> issues)
    {
        if (bom is null)
        {
            return;
        }

        var lookup = new Dictionary<string, string>(bom, StringComparer.OrdinalIgnoreCase);
        foreach (var row in active.Where(r => r.Kind == RowKind.Component && r.Position.HasValue && r.ComponentKey is not null))
        {
            if (lookup.TryGetValue(row.ComponentKey!, out var item) && item.Trim() != row.Position!.Value.ToString())
            {
                var shown = item.Trim().Length > 0 ? item.Trim() : "пусто";
                issues.Add(new SpecIssue(IssueSeverity.Warning,
                    $"{Describe(row)}: в BOM Inventor позиция {shown}. Выноски на чертеже не совпадут — нажмите «Позиции в BOM».", row.Id));
            }
        }
    }

    private static void CheckComponentSections(
        List<SpecRow> active, IReadOnlyList<ComponentData>? components, SpecificationContext context, List<SpecIssue> issues)
    {
        if (components is null)
        {
            return;
        }

        var withoutSection = components
            .Where(c => c.Kind != ComponentKind.Document && string.IsNullOrWhiteSpace(c.SectionId))
            .Select(c => c.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var row in active.Where(r => r.ComponentKey is not null && withoutSection.Contains(r.ComponentKey)))
        {
            var section = context.Catalog.GetOrCreate(row.Source.SectionId).Name;
            issues.Add(new SpecIssue(IssueSeverity.Info,
                $"{Describe(row)}: раздел не задан в iProperties, определён автоматически — «{section}». Задайте командой «Свойства» или «Авторазделы».",
                row.Id));
        }
    }

    private static void CheckGroups(Specification spec, List<SpecIssue> issues)
    {
        foreach (var group in spec.Groups.Where(g => string.IsNullOrWhiteSpace(g.Title)))
        {
            issues.Add(new SpecIssue(IssueSeverity.Warning, "Группа «устанавливаемые по …» без заголовка."));
        }
    }

    private static void CheckTitleBlock(TitleBlock? title, List<SpecIssue> issues)
    {
        if (title is null)
        {
            return;
        }

        var required = new (string Field, string Value, IssueSeverity Severity)[]
        {
            ("обозначение (Part Number сборки)", title.Designation, IssueSeverity.Warning),
            ("наименование (Description сборки)", title.Name, IssueSeverity.Warning),
            ("«Разраб.» (Проектировщик)", title.Developer, IssueSeverity.Info),
            ("«Пров.» (Проверил)", title.Checker, IssueSeverity.Info),
        };
        foreach (var (field, value, severity) in required.Where(f => string.IsNullOrWhiteSpace(f.Value)))
        {
            issues.Add(new SpecIssue(severity, $"Основная надпись: не заполнено {field}."));
        }
    }
}
