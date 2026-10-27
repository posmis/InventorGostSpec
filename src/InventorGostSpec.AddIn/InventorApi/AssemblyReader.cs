using System.Globalization;
using InventorGostSpec.Core.Model;
using InventorGostSpec.Core.Storage;

namespace InventorGostSpec.AddIn.InventorApi;

/// <summary>Чтение состава первого уровня сборки для спецификации.</summary>
internal static class AssemblyReader
{
    public static List<ComponentData> Read(Inventor.Application application, AssemblyDocument assembly)
    {
        var result = new List<ComponentData>();
        var items = BomAccess.GetFirstLevel(assembly).ToList();

        // Один поиск на всю сборку: список чертежей и их содержимое читаются один раз.
        var drawings = new DrawingSearch(application, items
            .Where(i => i.Target.Kind == ComponentKind.Assembly)
            .Select(i => System.IO.Path.GetDirectoryName(i.Target.FullFileName ?? "") ?? "")
            .Prepend(BomAccess.AssemblyFolder(assembly))
            .Distinct(StringComparer.OrdinalIgnoreCase));

        var drawing = ReadAssemblyDrawing(application, assembly, drawings);
        if (drawing is not null)
        {
            result.Add(drawing);
        }

        foreach (var item in items)
        {
            result.Add(ToComponentData(item.Target, item.Key, Quantity(item.Row), drawings));
        }

        return result;
    }

    /// <param name="drawings">Поиск сборочного чертежа у сборок; <c>null</c> — не искать (формат сборочной единицы — А4).</param>
    public static ComponentData ToComponentData(ComponentTarget target, string key, decimal quantity, DrawingSearch? drawings = null) => new()
    {
        HasDrawing = drawings is not null && target.Kind == ComponentKind.Assembly && target.FullFileName is { } file
            ? drawings.Find(file, target.PartNumber, isAssembly: true) is not null
            : null,
        Key = key,
        Kind = target.Kind,
        IsContentCenter = target.IsContentCenter,
        SectionId = target.Get(PropertyNames.Section),
        Designation = target.PartNumber,
        Name = target.Description,
        Format = target.Get(PropertyNames.Format),
        Unit = target.Get(PropertyNames.Unit),
        Group = target.Get(PropertyNames.Group),
        SortName = target.Get(PropertyNames.SortName),
        StandardNumber = target.Get(PropertyNames.StandardNumber),
        Parameters = target.Get(PropertyNames.Parameters),
        MaterialKind = target.Get(PropertyNames.MaterialKind),
        Quantity = quantity,
    };

    /// <summary>
    /// Сборочный чертёж — см. <see cref="DrawingSearch"/> (в рабочей папке проекта: по имени сборки, по обозначению,
    /// по содержимому чертежа). Обозначение — обозначение сборки с кодом «СБ»
    /// (если в обозначении сборки «СБ» уже есть, второй не добавляется).
    /// </summary>
    private static ComponentData? ReadAssemblyDrawing(Inventor.Application application, AssemblyDocument assembly, DrawingSearch drawings)
    {
        var partNumber = PropertyAccess.Get(assembly.PropertySets, PropertyAccess.DesignTrackingSet, PropertyAccess.PartNumber);
        var path = drawings.Find(assembly.FullFileName, partNumber, isAssembly: true);
        if (path is null)
        {
            return null;
        }

        return new ComponentData
        {
            Key = "drawing:" + System.IO.Path.GetRelativePath(BomAccess.AssemblyFolder(assembly), path),
            Kind = ComponentKind.Document,
            Designation = Designations.AssemblyDrawing(partNumber),
            Name = "Сборочный чертеж",
            // Формат сборочного чертежа: SPEC_Format сборки, а если не задан — форматы листов чертежа.
            Format = PropertyAccess.GetCustom(assembly.PropertySets, PropertyNames.Format) is { Length: > 0 } format
                ? format
                : DrawingAccess.ReadFormat(application, path),
        };
    }

    internal static decimal Quantity(BOMRow row)
    {
        try
        {
            return Convert.ToDecimal(row.ItemQuantity, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return 1;
        }
    }
}
