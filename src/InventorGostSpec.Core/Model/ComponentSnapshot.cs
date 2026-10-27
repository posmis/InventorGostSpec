using System.Globalization;
using InventorGostSpec.Core.Parsing;
using InventorGostSpec.Core.Sections;

namespace InventorGostSpec.Core.Model;

/// <summary>
/// Значения строки, взятые из компонента при последнем обновлении спецификации.
/// Для ручных строк это значения, введённые пользователем.
/// </summary>
public sealed class ComponentSnapshot
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");

    /// <summary>Формат спецификации — его указывают в графе «Формат» у сборочных единиц и комплексов.</summary>
    public const string SpecificationFormat = "А4";

    private static readonly HashSet<string> SectionsWithSpecification = new(StringComparer.OrdinalIgnoreCase)
    {
        SectionIds.AssemblyUnits,
        SectionIds.Complexes,
    };

    private static readonly HashSet<string> SectionsWithoutDesignation = new(StringComparer.OrdinalIgnoreCase)
    {
        SectionIds.StandardProducts,
        SectionIds.OtherProducts,
        SectionIds.Materials,
    };

    public string SectionId { get; set; } = "";
    public string Format { get; set; } = "";
    public string Designation { get; set; } = "";
    public string Name { get; set; } = "";
    public string Quantity { get; set; } = "";
    public string Unit { get; set; } = "";
    public string Group { get; set; } = "";
    public string SortName { get; set; } = "";
    public string StandardNumber { get; set; } = "";
    public string Parameters { get; set; } = "";
    public string MaterialKind { get; set; } = "";

    public static ComponentSnapshot FromComponent(ComponentData component)
    {
        var snapshot = new ComponentSnapshot
        {
            SectionId = SectionResolver.Resolve(component),
            Format = Clean(component.Format),
            // У сборок «СБ» в обозначении нужен для чертежей, в спецификации его быть не должно.
            Designation = component.Kind == ComponentKind.Assembly
                ? Designations.WithoutAssemblyDrawingCode(component.Designation)
                : Clean(component.Designation),
            Name = Clean(component.Name),
            Quantity = FormatQuantity(component.Quantity),
            Unit = Clean(component.Unit),
            Group = Clean(component.Group),
            SortName = Clean(component.SortName),
            StandardNumber = Clean(component.StandardNumber),
            Parameters = Clean(component.Parameters),
            MaterialKind = Clean(component.MaterialKind),
        };

        // У сборочной единицы (комплекса) в графе «Формат» — формат её спецификации, то есть А4.
        if (snapshot.Format.Length == 0 && SectionsWithSpecification.Contains(snapshot.SectionId))
        {
            snapshot.Format = SpecificationFormat;
        }

        // ГОСТ 2.106: в разделах стандартных, прочих изделий и материалов графа «Обозначение» не заполняется.
        // Если наименования нет (у Content Center оно часто только в Part Number), берём его из обозначения.
        if (SectionsWithoutDesignation.Contains(snapshot.SectionId))
        {
            if (snapshot.Name.Length == 0)
            {
                snapshot.Name = snapshot.Designation;
            }

            snapshot.Designation = "";
        }

        // Если поля для сортировки не заполнены, пытаемся разобрать наименование,
        // например «Болт М12х60 ГОСТ 7798-70».
        if (snapshot.SortName.Length == 0 || snapshot.StandardNumber.Length == 0)
        {
            var parsed = StandardDesignationParser.Parse(snapshot.Name);
            if (snapshot.SortName.Length == 0)
            {
                snapshot.SortName = parsed.Name;
            }

            if (snapshot.StandardNumber.Length == 0)
            {
                snapshot.StandardNumber = parsed.StandardNumber;
            }

            if (snapshot.Parameters.Length == 0)
            {
                snapshot.Parameters = parsed.Parameters;
            }
        }

        return snapshot;
    }

    public static string FormatQuantity(decimal quantity) =>
        quantity == 0 ? "" : quantity.ToString("0.###", RuCulture);

    public string Get(SpecColumn column) => column switch
    {
        SpecColumn.Section => SectionId,
        SpecColumn.Format => Format,
        SpecColumn.Designation => Designation,
        SpecColumn.Name => Name,
        SpecColumn.Quantity => Quantity,
        SpecColumn.Unit => Unit,
        _ => throw new ArgumentOutOfRangeException(nameof(column), column, "Column is not stored in a snapshot."),
    };

    public void Set(SpecColumn column, string value)
    {
        value = Clean(value);
        switch (column)
        {
            case SpecColumn.Section: SectionId = value; break;
            case SpecColumn.Format: Format = value; break;
            case SpecColumn.Designation: Designation = value; break;
            case SpecColumn.Name: Name = value; break;
            case SpecColumn.Quantity: Quantity = value; break;
            case SpecColumn.Unit: Unit = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(column), column, "Column is not stored in a snapshot.");
        }
    }

    public ComponentSnapshot Clone() => (ComponentSnapshot)MemberwiseClone();

    private static string Clean(string? value) => value?.Trim() ?? "";
}

/// <summary>Определение раздела компонента.</summary>
public static class SectionResolver
{
    /// <summary>Раздел из iProperty, а если он не задан — по правилам по умолчанию.</summary>
    public static string Resolve(ComponentData component) =>
        string.IsNullOrWhiteSpace(component.SectionId)
            ? GetDefault(component.Kind, component.IsContentCenter)
            : component.SectionId.Trim();

    /// <summary>
    /// Раздел по умолчанию: документы — «Документация», Content Center — «Стандартные изделия»,
    /// сборки — «Сборочные единицы», детали — «Детали», виртуальные компоненты — «Прочие изделия».
    /// </summary>
    public static string GetDefault(ComponentKind kind, bool isContentCenter) => kind switch
    {
        ComponentKind.Document => SectionIds.Documentation,
        _ when isContentCenter => SectionIds.StandardProducts,
        ComponentKind.Assembly => SectionIds.AssemblyUnits,
        ComponentKind.Part => SectionIds.Parts,
        _ => SectionIds.OtherProducts,
    };
}
