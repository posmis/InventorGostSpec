using Inventor;

namespace InventorGostSpec.AddIn.InventorApi;

internal sealed record BomItem(BOMRow Row, ComponentTarget Target, string Key);

/// <summary>
/// Строки первого уровня структурированной спецификации Inventor (BOM).
/// В ней уже учтены типы BOM: справочные компоненты исключены, у фантомных
/// вместо них стоят их дочерние компоненты, одинаковые вхождения объединены.
/// </summary>
internal static class BomAccess
{
    public static IReadOnlyList<BomItem> GetFirstLevel(AssemblyDocument assembly)
    {
        var bom = assembly.ComponentDefinition.BOM;
        if (!bom.StructuredViewEnabled)
        {
            bom.StructuredViewEnabled = true;
        }

        var view = FindStructuredView(bom) ?? throw new InvalidOperationException("Не найдена структурированная спецификация (BOM) сборки.");
        var folder = AssemblyFolder(assembly);
        var result = new List<BomItem>();

        foreach (BOMRow row in view.BOMRows)
        {
            if (row.BOMStructure is BOMStructureEnum.kReferenceBOMStructure or BOMStructureEnum.kPhantomBOMStructure)
            {
                continue;
            }

            if (row.ComponentDefinitions.Count == 0)
            {
                continue;
            }

            var target = ComponentTarget.FromDefinition(row.ComponentDefinitions[1]);
            if (target is not null)
            {
                result.Add(new BomItem(row, target, target.GetKey(folder)));
            }
        }

        return result;
    }

    public static string AssemblyFolder(AssemblyDocument assembly) =>
        System.IO.Path.GetDirectoryName(assembly.FullFileName) ?? "";

    private static BOMView? FindStructuredView(BOM bom)
    {
        // Имена видов локализованы, поэтому ищем по типу.
        foreach (BOMView view in bom.BOMViews)
        {
            if (view.ViewType == BOMViewTypeEnum.kStructuredBOMViewType)
            {
                return view;
            }
        }

        return null;
    }
}
