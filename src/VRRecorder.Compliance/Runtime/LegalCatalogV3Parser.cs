using System.Globalization;
using System.Text;
using System.Text.Json;
using VRRecorder.Compliance.Generation;

namespace VRRecorder.Compliance.Runtime;

internal static class LegalCatalogV3Parser
{
    private const string ManifestFileName = "LEGAL-MANIFEST.sha256";
    private const string AssetManifestFileName =
        "MATERIAL-SYMBOLS-MANIFEST.json";
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private static readonly string[] RootProperties =
    [
        "schemaVersion",
        "bundleId",
        "productVersion",
        "generatedAtUtc",
        "integrityManifest",
        "components",
    ];
    private static readonly string[] IntegrityProperties =
    [
        "path",
        "algorithm",
    ];
    private static readonly string[] ComponentProperties =
    [
        "id",
        "displayName",
        "version",
        "licenseExpression",
        "usage",
        "linkage",
        "modified",
        "sourceInfo",
        "copyrightNotice",
        "legalDocuments",
    ];
    private static readonly string[] DocumentProperties =
    [
        "kind",
        "path",
    ];

    public static ParsedLegalCatalog Parse(
        byte[] bytes,
        string expectedBundleId,
        IReadOnlySet<string> manifestPaths,
        string bundleDirectory)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedBundleId);
        ArgumentNullException.ThrowIfNull(manifestPaths);
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleDirectory);

        try
        {
            using var document = JsonDocument.Parse(
                StrictUtf8.GetString(bytes),
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 16,
                });
            var root = document.RootElement;
            RequireExactProperties(root, RootProperties);
            if (RequiredInt32(root, "schemaVersion") != 3)
            {
                throw new InvalidDataException();
            }

            var bundleId = RequiredString(root, "bundleId");
            if (!Uri.TryCreate(bundleId, UriKind.Absolute, out _) ||
                !string.Equals(
                    bundleId,
                    expectedBundleId,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException();
            }

            var productVersion = RequiredString(root, "productVersion");
            var generatedAtUtc = RequiredString(root, "generatedAtUtc");
            if (!DateTimeOffset.TryParseExact(
                    generatedAtUtc,
                    "yyyy-MM-dd'T'HH:mm:ss'Z'",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal |
                    DateTimeStyles.AdjustToUniversal,
                    out _))
            {
                throw new InvalidDataException();
            }

            var integrity = RequiredObject(root, "integrityManifest");
            RequireExactProperties(integrity, IntegrityProperties);
            if (!string.Equals(
                    RequiredString(integrity, "path"),
                    ManifestFileName,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    RequiredString(integrity, "algorithm"),
                    "SHA-256",
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException();
            }

            var componentIds = new HashSet<string>(StringComparer.Ordinal);
            var components = new List<ParsedLegalCatalogComponent>();
            foreach (var element in RequiredArray(root, "components")
                         .EnumerateArray())
            {
                RequireExactProperties(element, ComponentProperties);
                var id = RequiredString(element, "id");
                if (!IsCanonicalComponentId(id) || !componentIds.Add(id))
                {
                    throw new InvalidDataException();
                }

                var paths = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
                var references = new HashSet<ParsedLegalDocumentReference>();
                var legalDocuments = new List<ParsedLegalDocumentReference>();
                foreach (var referenceElement in RequiredArray(
                             element,
                             "legalDocuments").EnumerateArray())
                {
                    RequireExactProperties(
                        referenceElement,
                        DocumentProperties);
                    var kind = ParseKind(RequiredString(
                        referenceElement,
                        "kind"));
                    var path = RequiredString(referenceElement, "path");
                    _ = LegalArtifactPath.Resolve(bundleDirectory, path);
                    if (!manifestPaths.Contains(path) ||
                        !paths.Add(path))
                    {
                        throw new InvalidDataException();
                    }

                    if (kind == LegalFileKind.AssetManifest &&
                        !string.Equals(
                            path,
                            AssetManifestFileName,
                            StringComparison.Ordinal))
                    {
                        throw new InvalidDataException();
                    }

                    var reference = new ParsedLegalDocumentReference(
                        kind,
                        path);
                    if (!references.Add(reference))
                    {
                        throw new InvalidDataException();
                    }

                    legalDocuments.Add(reference);
                }

                if (legalDocuments.Count(reference =>
                        reference.Kind == LegalFileKind.License) != 1)
                {
                    throw new InvalidDataException();
                }

                components.Add(new ParsedLegalCatalogComponent(
                    id,
                    RequiredString(element, "displayName"),
                    RequiredString(element, "version"),
                    RequiredString(element, "licenseExpression"),
                    RequiredString(element, "usage"),
                    RequiredString(element, "linkage"),
                    RequiredBoolean(element, "modified"),
                    RequiredString(element, "sourceInfo"),
                    RequiredString(element, "copyrightNotice"),
                    legalDocuments
                        .OrderBy(reference => KindRank(reference.Kind))
                        .ThenBy(
                            reference => reference.Path,
                            StringComparer.Ordinal)
                        .ToArray()));
            }

            return new ParsedLegalCatalog(
                bundleId,
                productVersion,
                components
                    .OrderBy(component => component.Id, StringComparer.Ordinal)
                    .ToArray());
        }
        catch (Exception exception) when (
            exception is JsonException or
                DecoderFallbackException or
                InvalidOperationException or
                KeyNotFoundException or
                ArgumentException)
        {
            throw new InvalidDataException(
                "The authenticated legal catalog does not satisfy schema v3.",
                exception);
        }
    }

    private static void RequireExactProperties(
        JsonElement element,
        string[] requiredProperties)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException();
        }

        var actual = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!actual.Add(property.Name))
            {
                throw new InvalidDataException();
            }
        }

        if (actual.Count != requiredProperties.Length ||
            requiredProperties.Any(property => !actual.Contains(property)))
        {
            throw new InvalidDataException();
        }
    }

    private static string RequiredString(JsonElement parent, string name)
    {
        var value = parent.GetProperty(name);
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException();
        }

        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text)
            ? throw new InvalidDataException()
            : text;
    }

    private static int RequiredInt32(JsonElement parent, string name)
    {
        var value = parent.GetProperty(name);
        return value.ValueKind == JsonValueKind.Number &&
               value.TryGetInt32(out var result)
            ? result
            : throw new InvalidDataException();
    }

    private static bool RequiredBoolean(JsonElement parent, string name)
    {
        var value = parent.GetProperty(name);
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new InvalidDataException(),
        };
    }

    private static JsonElement RequiredObject(JsonElement parent, string name)
    {
        var value = parent.GetProperty(name);
        return value.ValueKind == JsonValueKind.Object
            ? value
            : throw new InvalidDataException();
    }

    private static JsonElement RequiredArray(JsonElement parent, string name)
    {
        var value = parent.GetProperty(name);
        return value.ValueKind == JsonValueKind.Array
            ? value
            : throw new InvalidDataException();
    }

    private static bool IsCanonicalComponentId(string id) =>
        id.Length > 0 &&
        id[0] is >= 'a' and <= 'z' or >= '0' and <= '9' &&
        id.All(character =>
            character is >= 'a' and <= 'z' or
                >= '0' and <= '9' or '.' or '_' or '-');

    private static LegalFileKind ParseKind(string kind) => kind switch
    {
        "license" => LegalFileKind.License,
        "notice" => LegalFileKind.Notice,
        "copyright" => LegalFileKind.Copyright,
        "attribution" => LegalFileKind.Attribution,
        "asset-manifest" => LegalFileKind.AssetManifest,
        _ => throw new InvalidDataException(),
    };

    private static int KindRank(LegalFileKind kind) => kind switch
    {
        LegalFileKind.License => 0,
        LegalFileKind.Notice => 1,
        LegalFileKind.Copyright => 2,
        LegalFileKind.Attribution => 3,
        LegalFileKind.AssetManifest => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}

internal sealed record ParsedLegalCatalog(
    string BundleId,
    string ProductVersion,
    IReadOnlyList<ParsedLegalCatalogComponent> Components);

internal sealed record ParsedLegalCatalogComponent(
    string Id,
    string DisplayName,
    string Version,
    string LicenseExpression,
    string Usage,
    string Linkage,
    bool Modified,
    string SourceInformation,
    string CopyrightNotice,
    IReadOnlyList<ParsedLegalDocumentReference> LegalDocuments);

internal sealed record ParsedLegalDocumentReference(
    LegalFileKind Kind,
    string Path);
