using Inventor;
using InventorGostSpec.Core.Model;
using InventorGostSpec.Core.Storage;

namespace InventorGostSpec.AddIn.InventorApi;

/// <summary>
/// Компонент, в iProperties которого записываются данные спецификации:
/// файл детали или сборки либо виртуальный компонент.
/// </summary>
internal sealed class ComponentTarget
{
    public static readonly string[] SpecProperties =
    {
        PropertyNames.Section,
        PropertyNames.Format,
        PropertyNames.Unit,
        PropertyNames.Group,
        PropertyNames.SortName,
        PropertyNames.StandardNumber,
        PropertyNames.Parameters,
        PropertyNames.MaterialKind,
    };

    private ComponentTarget(PropertySets propertySets, ComponentKind kind, bool isContentCenter, string displayName, string? fullFileName)
    {
        PropertySets = propertySets;
        Kind = kind;
        IsContentCenter = isContentCenter;
        DisplayName = displayName;
        FullFileName = fullFileName;
    }

    public PropertySets PropertySets { get; }

    public ComponentKind Kind { get; }

    public bool IsContentCenter { get; }

    public string DisplayName { get; }

    /// <summary>Файл компонента; у виртуальных компонентов отсутствует.</summary>
    public string? FullFileName { get; }

    /// <summary>
    /// Ключ строки спецификации: путь к файлу относительно папки сборки
    /// (не ломается при переносе папки проекта) или имя виртуального компонента.
    /// </summary>
    public string GetKey(string assemblyFolder) =>
        FullFileName is null
            ? "virtual:" + DisplayName
            : System.IO.Path.GetRelativePath(assemblyFolder, FullFileName);

    public string Get(string propertyName) => PropertyAccess.GetCustom(PropertySets, propertyName);

    public void Set(string propertyName, string value) => PropertyAccess.SetCustom(PropertySets, propertyName, value);

    public string PartNumber => PropertyAccess.Get(PropertySets, PropertyAccess.DesignTrackingSet, PropertyAccess.PartNumber);

    /// <summary>Наименование: Description, а если оно пустое — Title.</summary>
    public string Description
    {
        get
        {
            var description = PropertyAccess.Get(PropertySets, PropertyAccess.DesignTrackingSet, PropertyAccess.Description);
            return description.Length > 0
                ? description
                : PropertyAccess.Get(PropertySets, PropertyAccess.SummarySet, PropertyAccess.Title);
        }
    }

    public static ComponentTarget FromDocument(Document document)
    {
        var kind = document.DocumentType == DocumentTypeEnum.kAssemblyDocumentObject ? ComponentKind.Assembly : ComponentKind.Part;
        var isContentCenter = document is PartDocument part && part.ComponentDefinition.IsContentMember;
        return new ComponentTarget(document.PropertySets, kind, isContentCenter, document.DisplayName, document.FullFileName);
    }

    public static ComponentTarget? FromDefinition(ComponentDefinition definition)
    {
        if (definition is VirtualComponentDefinition virtualDefinition)
        {
            return new ComponentTarget(virtualDefinition.PropertySets, ComponentKind.Virtual, false, virtualDefinition.DisplayName, null);
        }

        return definition.Document is Document document ? FromDocument(document) : null;
    }
}
