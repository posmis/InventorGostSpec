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
        return spec;
    }

    public static string SerializeSettings(SpecSettings settings) => JsonSerializer.Serialize(settings, Options);

    public static SpecSettings DeserializeSettings(string json) =>
        JsonSerializer.Deserialize<SpecSettings>(json, Options) ?? new SpecSettings();

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
