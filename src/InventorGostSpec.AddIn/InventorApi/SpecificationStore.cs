using InventorGostSpec.Core.Model;
using InventorGostSpec.Core.Storage;
using InvAttribute = Inventor.Attribute;

namespace InventorGostSpec.AddIn.InventorApi;

/// <summary>
/// Хранение спецификации внутри файла сборки (AttributeSet). Спецификация переносится вместе с файлом.
/// Предыдущая версия сохраняется в отдельном атрибуте на случай ошибки.
/// </summary>
internal static class SpecificationStore
{
    private const string SetName = "InventorGostSpec";
    private const string ValueName = "Specification";
    private const string BackupName = "SpecificationBackup";

    public static Specification? Load(Document document)
    {
        var set = FindSet(document);
        var json = set is null ? null : FindAttribute(set, ValueName)?.Value as string;
        return string.IsNullOrWhiteSpace(json) ? null : SpecificationSerializer.Deserialize(json);
    }

    public static void Save(Document document, Specification specification)
    {
        var json = SpecificationSerializer.Serialize(specification);
        var set = FindSet(document) ?? document.AttributeSets.Add(SetName);

        var current = FindAttribute(set, ValueName);
        if (current?.Value is string previous && previous == json)
        {
            return;
        }

        if (current?.Value is string old)
        {
            Write(set, BackupName, old);
        }

        Write(set, ValueName, json);
    }

    private static void Write(AttributeSet set, string name, string value)
    {
        var attribute = FindAttribute(set, name);
        if (attribute is null)
        {
            set.Add(name, ValueTypeEnum.kStringType, value);
        }
        else
        {
            attribute.Value = value;
        }
    }

    private static AttributeSet? FindSet(Document document)
    {
        foreach (AttributeSet set in document.AttributeSets)
        {
            if (set.Name == SetName)
            {
                return set;
            }
        }

        return null;
    }

    private static InvAttribute? FindAttribute(AttributeSet set, string name)
    {
        foreach (InvAttribute attribute in set)
        {
            if (attribute.Name == name)
            {
                return attribute;
            }
        }

        return null;
    }
}
