using System.Text.Json;
using Json.Schema;

namespace VRRecorder.Infrastructure.Storage;

internal interface ISettingsJsonSchemaValidator
{
    void Validate(ReadOnlyMemory<byte> documentBytes);
}

internal sealed class SettingsJsonSchemaValidator : ISettingsJsonSchemaValidator
{
    private const int FirstSupportedSchemaVersion = 1;
    private const int LastSupportedSchemaVersion = 3;
    private readonly Dictionary<int, JsonSchema> _schemas;

    private SettingsJsonSchemaValidator()
    {
        _schemas = Enumerable
            .Range(
                FirstSupportedSchemaVersion,
                LastSupportedSchemaVersion - FirstSupportedSchemaVersion + 1)
            .ToDictionary(version => version, LoadSchema);
    }

    public static SettingsJsonSchemaValidator Default { get; } = new();

    public void Validate(ReadOnlyMemory<byte> documentBytes)
    {
        using var document = JsonDocument.Parse(documentBytes);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("schemaVersion", out var versionValue) ||
            !versionValue.TryGetInt32(out var version) ||
            !_schemas.TryGetValue(version, out var schema))
        {
            throw new InvalidDataException(
                "The settings document has no supported schema version.");
        }

        var result = schema.Evaluate(
            document.RootElement,
            new EvaluationOptions
            {
                OutputFormat = OutputFormat.Flag,
                RequireFormatValidation = true,
            });
        if (!result.IsValid)
        {
            throw new InvalidDataException(
                $"The settings document does not conform to schema v{version}.");
        }
    }

    private static JsonSchema LoadSchema(int version)
    {
        var resourceName = $"VRRecorder.Settings.v{version}.schema.json";
        var assembly = typeof(SettingsJsonSchemaValidator).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName) ??
                           throw new InvalidOperationException(
                               $"The embedded settings schema {resourceName} is missing.");
        using var reader = new StreamReader(stream);
        return JsonSchema.FromText(reader.ReadToEnd());
    }
}
