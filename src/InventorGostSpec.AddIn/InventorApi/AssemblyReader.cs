using System.Globalization;
using InventorGostSpec.Core.Model;
using InventorGostSpec.Core.Storage;

namespace InventorGostSpec.AddIn.InventorApi;

/// <summary>Чтение состава первого уровня сборки для спецификации.</summary>
internal static class AssemblyReader
{
    public const string AssemblyDrawingCode = "СБ";

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
    /// Сборочный чертёж — чертёж с тем же именем рядом со сборкой.
    /// Обозначение — обозначение сборки с кодом «СБ».
    /// </summary>
    private static ComponentData? ReadAssemblyDrawing(Inventor.Application application, AssemblyDocument assembly)
    {
        var path = DrawingAccess.FindDrawing(assembly.FullFileName);
        if (path is null)
        {
            return null;
        }

        var partNumber = PropertyAccess.Get(assembly.PropertySets, PropertyAccess.DesignTrackingSet, PropertyAccess.PartNumber);
        return new ComponentData
        {
            Key = "drawing:" + System.IO.Path.GetRelativePath(BomAccess.AssemblyFolder(assembly), path),
            Kind = ComponentKind.Document,
            Designation = partNumber.Length > 0 ? partNumber + AssemblyDrawingCode : "",
            Name = "Сборочный чертеж",
            Format = DrawingAccess.ReadFormat(application, path),
        };
    }

    private static decimal Quantity(BOMRow row)
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
