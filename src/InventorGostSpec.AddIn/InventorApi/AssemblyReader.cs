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

        var drawing = ReadAssemblyDrawing(application, assembly);
        if (drawing is not null)
        {
            result.Add(drawing);
        }

        foreach (var item in BomAccess.GetFirstLevel(assembly))
        {
            result.Add(ToComponentData(item.Target, item.Key, Quantity(item.Row)));
        }

        return result;
    }

    public static ComponentData ToComponentData(ComponentTarget target, string key, decimal quantity) => new()
    {
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
    private static ComponentData? ReadAssemblyDrawing(Inventor.Application application, AssemblyDocument assembly)
    {
        var partNumber = PropertyAccess.Get(assembly.PropertySets, PropertyAccess.DesignTrackingSet, PropertyAccess.PartNumber);
        var path = new DrawingSearch(application, new[] { BomAccess.AssemblyFolder(assembly) })
            .Find(assembly.FullFileName, partNumber, isAssembly: true);
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
