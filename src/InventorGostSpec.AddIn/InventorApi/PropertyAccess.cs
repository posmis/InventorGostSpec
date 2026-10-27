
namespace InventorGostSpec.AddIn.InventorApi;

/// <summary>Чтение и запись iProperties.</summary>
internal static class PropertyAccess
{
    public const string DesignTrackingSet = "Design Tracking Properties";
    public const string DocumentSummarySet = "Inventor Document Summary Information";
    public const string SummarySet = "Inventor Summary Information";
    public const string UserDefinedSet = "Inventor User Defined Properties";

    public const string PartNumber = "Part Number";
    public const string Description = "Description";
    public const string Title = "Title";
    public const string Comments = "Comments";
    public const string Designer = "Designer";
    public const string CheckedBy = "Checked By";
    public const string EngineeringApprovedBy = "Engr Approved By";
    public const string Company = "Company";

    /// <summary>Как <see cref="Get"/>, но без исключения, если набора или свойства нет.</summary>
    public static string TryGet(PropertySets sets, string setName, string propertyName)
    {
        try
        {
            return Get(sets, setName, propertyName);
        }
        catch (Exception)
        {
            return "";
        }
    }

    public static string Get(PropertySets sets, string setName, string propertyName)
    {
        var property = Find(sets[setName], propertyName);
        return property?.Value?.ToString()?.Trim() ?? "";
    }

    public static string GetCustom(PropertySets sets, string propertyName) => Get(sets, UserDefinedSet, propertyName);

    /// <summary>Записывает существующее стандартное свойство (например, Description).</summary>
    public static void Set(PropertySets sets, string setName, string propertyName, string value)
    {
        var property = Find(sets[setName], propertyName)
                       ?? throw new InvalidOperationException($"Свойство «{propertyName}» не найдено.");
        if (property.Value?.ToString() != value)
        {
            property.Value = value;
        }
    }

    /// <summary>Записывает пользовательское свойство. Пустое значение удаляет свойство.</summary>
    public static void SetCustom(PropertySets sets, string propertyName, string value)
    {
        var set = sets[UserDefinedSet];
        var property = Find(set, propertyName);
        if (string.IsNullOrWhiteSpace(value))
        {
            property?.Delete();
            return;
        }

        if (property is null)
        {
            set.Add(value, propertyName);
        }
        else if (property.Value?.ToString() != value)
        {
            property.Value = value;
        }
    }

    private static Property? Find(PropertySet set, string propertyName)
    {
        foreach (Property property in set)
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                return property;
            }
        }

        return null;
    }
}
