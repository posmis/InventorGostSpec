using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using InventorGostSpec.Core.Model;

namespace InventorGostSpec.Core.Storage;

/// <summary>JSON-сериализация спецификации (для хранения в атрибутах сборки) и настроек.</summary>
public static class SpecificationSerializer
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static string Serialize(Specification spec) => JsonSerializer.Serialize(spec, Options);

    public static Specification Deserialize(string json)
    {
        var spec = JsonSerializer.Deserialize<Specification>(json, Options)
                   ?? throw new JsonException("Specification JSON is empty.");
        if (spec.SchemaVersion > Specification.CurrentSchemaVersion)
        {
            throw new NotSupportedException(
                $"Спецификация сохранена более новой версией приложения (схема {spec.SchemaVersion}).");
        }

        spec.SchemaVersion = Specification.CurrentSchemaVersion;
        Repair(spec);
        return spec;
    }

    /// <summary>
    /// Чтение спецификации из сборки: основная копия, а если она повреждена — предыдущая (резервная).
    /// Спецификация более новой версии приложения не читается (исключение), чтобы её не испортить сохранением.
    /// </summary>
    /// <returns><c>null</c> — спецификации нет.</returns>
    public static Specification? Load(string? json, string? backupJson, out bool fromBackup)
    {
        fromBackup = false;
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return Deserialize(json);
        }
        catch (JsonException) when (!string.IsNullOrWhiteSpace(backupJson))
        {
            var backup = Deserialize(backupJson!);
            fromBackup = true;
            return backup;
        }
    }

    /// <summary>Пустые (null) коллекции и значения из файла — пустыми значениями: старые и правленые вручную данные.</summary>
    private static void Repair(Specification spec)
    {
        spec.Rows ??= new List<SpecRow>();
        spec.Groups ??= new List<SpecGroup>();
        spec.CustomSections ??= new List<Sections.SectionDefinition>();
        spec.ReserveOverrides ??= new Dictionary<string, ReserveSettings>();
        spec.ShownSections ??= new List<ShownSection>();
        spec.ShownSections.RemoveAll(s => s is null || string.IsNullOrWhiteSpace(s.SectionId));
        foreach (var shown in spec.ShownSections)
        {
            shown.GroupId ??= "";
        }

        spec.Rows.RemoveAll(r => r is null);
        spec.Groups.RemoveAll(g => g is null);
        spec.CustomSections.RemoveAll(s => s is null || string.IsNullOrWhiteSpace(s.Id));
        foreach (var group in spec.Groups)
        {
            group.Reserves ??= new Dictionary<string, ReserveSettings>();
            group.Title ??= "";
        }

        foreach (var row in spec.Rows)
        {
            row.Id = string.IsNullOrEmpty(row.Id) ? Guid.NewGuid().ToString("N") : row.Id;
            row.Source ??= new ComponentSnapshot();
            row.Overrides ??= new Dictionary<SpecColumn, string>();
            row.PreviousValues ??= new Dictionary<SpecColumn, string>();
            row.Zone ??= "";
            row.Note ??= "";
            row.GroupId ??= "";
            row.Source.SectionId ??= "";
            if (row.SectionId.Trim().Length == 0)
            {
                // Строка без раздела не может быть показана — переносим в «Прочие изделия».
                row.Overrides.Remove(SpecColumn.Section);
                if (row.Source.SectionId.Trim().Length == 0)
                {
                    row.Source.SectionId = Sections.SectionIds.OtherProducts;
                }
            }
        }

        // Одинаковые Id (например, после ручной правки) ломают выбор и перенос строк.
        var ids = new HashSet<string>();
        foreach (var row in spec.Rows.Where(r => !ids.Add(r.Id)))
        {
            row.Id = Guid.NewGuid().ToString("N");
        }
    }

    public static string SerializeSettings(SpecSettings settings) => JsonSerializer.Serialize(settings, Options);

    public static SpecSettings DeserializeSettings(string json)
    {
        var settings = JsonSerializer.Deserialize<SpecSettings>(json, Options) ?? new SpecSettings();
        settings.CustomSections ??= new List<Sections.SectionDefinition>();
        settings.CustomSections.RemoveAll(s => s is null || string.IsNullOrWhiteSpace(s.Id));
        settings.DefaultReserves ??= new Dictionary<string, ReserveSettings>();
        settings.NotePropertyName = string.IsNullOrWhiteSpace(settings.NotePropertyName) ? "Примечание" : settings.NotePropertyName;
        return settings;
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
