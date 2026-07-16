using System.Text.Json;
using System.Text.RegularExpressions;
using VRRecorder.Application.Settings;
using VRRecorder.Infrastructure.Storage;

namespace VRRecorder.IntegrationTests.Storage;

public sealed class SettingsJsonSchemaTests
{
    [Fact]
    public async Task PackagedV2SchemaValidatesPersistedDesignDefaults()
    {
        using var directory = TemporaryDirectory.Create();
        var settingsPath = Path.Combine(directory.Path, "settings.json");
        var store = new JsonFileSettingsStore(settingsPath);
        var defaults = VRRecorderSettings.CreateDefault();
        var settingsWithProfile = defaults with
        {
            Vr = defaults.Vr with
            {
                PlacementProfiles =
                [
                    new VrOverlayPlacementProfile(
                        new VrDeviceProfile(
                            "lighthouse",
                            "index-hmd",
                            "/input/index_controller_profile.json"),
                        VrHand.Left,
                        OverlayPlacementMode.WristDock,
                        new OverlayTransform(
                            [0.03, 0.05, -0.08],
                            [25, 0, 10])),
                ],
            },
        };
        await store.SaveAsync(settingsWithProfile, CancellationToken.None);
        var schemaPath = Path.Combine(
            AppContext.BaseDirectory,
            "Schemas",
            "vr-recorder-settings-v2.schema.json");
        var legacySchemaPath = Path.Combine(
            AppContext.BaseDirectory,
            "Schemas",
            "vr-recorder-settings-v1.schema.json");

        Assert.True(
            File.Exists(schemaPath),
            $"The packaged settings schema was not found at {schemaPath}.");
        Assert.True(
            File.Exists(legacySchemaPath),
            $"The legacy settings schema was not found at {legacySchemaPath}.");
        Assert.Contains(
            "VRRecorder.Settings.v1.schema.json",
            typeof(JsonFileSettingsStore).Assembly.GetManifestResourceNames());
        Assert.Contains(
            "VRRecorder.Settings.v2.schema.json",
            typeof(JsonFileSettingsStore).Assembly.GetManifestResourceNames());
        using var schema = JsonDocument.Parse(
            await File.ReadAllBytesAsync(schemaPath));
        using var settings = JsonDocument.Parse(
            await File.ReadAllBytesAsync(settingsPath));

        Assert.Equal(
            "https://json-schema.org/draft/2020-12/schema",
            schema.RootElement.GetProperty("$schema").GetString());
        Assert.Equal(
            2,
            schema.RootElement
                .GetProperty("properties")
                .GetProperty("schemaVersion")
                .GetProperty("const")
                .GetInt32());
        Assert.Empty(JsonSchemaSubsetValidator.Validate(
            schema.RootElement,
            settings.RootElement));
    }

    private static class JsonSchemaSubsetValidator
    {
        public static List<string> Validate(
            JsonElement schema,
            JsonElement instance)
        {
            var errors = new List<string>();
            Validate(schema, schema, instance, "$", errors);
            return errors;
        }

        private static void Validate(
            JsonElement rootSchema,
            JsonElement schema,
            JsonElement instance,
            string path,
            List<string> errors)
        {
            if (schema.TryGetProperty("$ref", out var reference))
            {
                Validate(
                    rootSchema,
                    ResolveLocalReference(rootSchema, reference.GetString()!),
                    instance,
                    path,
                    errors);
                return;
            }

            if (schema.TryGetProperty("anyOf", out var alternatives))
            {
                var matched = alternatives.EnumerateArray().Any(alternative =>
                {
                    var alternativeErrors = new List<string>();
                    Validate(
                        rootSchema,
                        alternative,
                        instance,
                        path,
                        alternativeErrors);
                    return alternativeErrors.Count == 0;
                });
                if (!matched)
                {
                    errors.Add($"{path} did not match any allowed schema.");
                }

                return;
            }

            if (schema.TryGetProperty("type", out var type) &&
                !MatchesType(type.GetString(), instance))
            {
                errors.Add(
                    $"{path} has type {instance.ValueKind}, expected {type.GetString()}.");
                return;
            }

            if (schema.TryGetProperty("const", out var constant) &&
                !JsonElement.DeepEquals(constant, instance))
            {
                errors.Add($"{path} does not equal its constant value.");
            }

            if (schema.TryGetProperty("enum", out var allowed) &&
                !allowed.EnumerateArray().Any(value =>
                    JsonElement.DeepEquals(value, instance)))
            {
                errors.Add($"{path} is not an allowed enum value.");
            }

            switch (instance.ValueKind)
            {
                case JsonValueKind.Object:
                    ValidateObject(rootSchema, schema, instance, path, errors);
                    break;
                case JsonValueKind.Array:
                    ValidateArray(rootSchema, schema, instance, path, errors);
                    break;
                case JsonValueKind.String:
                    ValidateString(schema, instance, path, errors);
                    break;
                case JsonValueKind.Number:
                    ValidateNumber(schema, instance, path, errors);
                    break;
            }
        }

        private static void ValidateObject(
            JsonElement rootSchema,
            JsonElement schema,
            JsonElement instance,
            string path,
            List<string> errors)
        {
            var properties = schema.TryGetProperty("properties", out var value)
                ? value
                : default;
            if (schema.TryGetProperty("required", out var required))
            {
                foreach (var name in required.EnumerateArray()
                             .Select(item => item.GetString()!))
                {
                    if (!instance.TryGetProperty(name, out _))
                    {
                        errors.Add($"{path}.{name} is required.");
                    }
                }
            }

            foreach (var property in instance.EnumerateObject())
            {
                if (properties.ValueKind == JsonValueKind.Object &&
                    properties.TryGetProperty(property.Name, out var propertySchema))
                {
                    Validate(
                        rootSchema,
                        propertySchema,
                        property.Value,
                        $"{path}.{property.Name}",
                        errors);
                }
                else if (schema.TryGetProperty(
                             "additionalProperties",
                             out var additionalProperties) &&
                         additionalProperties.ValueKind == JsonValueKind.False)
                {
                    errors.Add($"{path}.{property.Name} is not allowed.");
                }
            }
        }

        private static void ValidateArray(
            JsonElement rootSchema,
            JsonElement schema,
            JsonElement instance,
            string path,
            List<string> errors)
        {
            var length = instance.GetArrayLength();
            if (schema.TryGetProperty("minItems", out var minimum) &&
                length < minimum.GetInt32())
            {
                errors.Add($"{path} has too few items.");
            }

            if (schema.TryGetProperty("maxItems", out var maximum) &&
                length > maximum.GetInt32())
            {
                errors.Add($"{path} has too many items.");
            }

            if (schema.TryGetProperty("items", out var itemSchema))
            {
                var index = 0;
                foreach (var item in instance.EnumerateArray())
                {
                    Validate(
                        rootSchema,
                        itemSchema,
                        item,
                        $"{path}[{index}]",
                        errors);
                    index++;
                }
            }
        }

        private static void ValidateString(
            JsonElement schema,
            JsonElement instance,
            string path,
            List<string> errors)
        {
            var text = instance.GetString()!;
            if (schema.TryGetProperty("minLength", out var minimum) &&
                text.Length < minimum.GetInt32())
            {
                errors.Add($"{path} is too short.");
            }

            if (schema.TryGetProperty("maxLength", out var maximum) &&
                text.Length > maximum.GetInt32())
            {
                errors.Add($"{path} is too long.");
            }

            if (schema.TryGetProperty("pattern", out var pattern) &&
                !Regex.IsMatch(
                    text,
                    pattern.GetString()!,
                    RegexOptions.CultureInvariant,
                    TimeSpan.FromMilliseconds(100)))
            {
                errors.Add($"{path} does not match its required pattern.");
            }
        }

        private static void ValidateNumber(
            JsonElement schema,
            JsonElement instance,
            string path,
            List<string> errors)
        {
            var number = instance.GetDouble();
            if (schema.TryGetProperty("minimum", out var minimum) &&
                number < minimum.GetDouble())
            {
                errors.Add($"{path} is below its minimum.");
            }

            if (schema.TryGetProperty("maximum", out var maximum) &&
                number > maximum.GetDouble())
            {
                errors.Add($"{path} is above its maximum.");
            }
        }

        private static bool MatchesType(
            string? type,
            JsonElement instance) => type switch
            {
                "object" => instance.ValueKind == JsonValueKind.Object,
                "array" => instance.ValueKind == JsonValueKind.Array,
                "string" => instance.ValueKind == JsonValueKind.String,
                "integer" => instance.ValueKind == JsonValueKind.Number &&
                             instance.TryGetInt64(out _),
                "number" => instance.ValueKind == JsonValueKind.Number,
                "boolean" => instance.ValueKind is JsonValueKind.True or
                    JsonValueKind.False,
                "null" => instance.ValueKind == JsonValueKind.Null,
                _ => false,
            };

        private static JsonElement ResolveLocalReference(
            JsonElement rootSchema,
            string reference)
        {
            if (!reference.StartsWith("#/", StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Only local JSON schema references are supported: {reference}");
            }

            var current = rootSchema;
            foreach (var token in reference[2..].Split('/'))
            {
                var propertyName = token
                    .Replace("~1", "/", StringComparison.Ordinal)
                    .Replace("~0", "~", StringComparison.Ordinal);
                current = current.GetProperty(propertyName);
            }

            return current;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"vr-recorder-schema-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
