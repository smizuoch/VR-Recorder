using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using VRRecorder.Compliance.Staging;

namespace VRRecorder.Compliance.Distribution;

internal sealed record WindowsApplicationPayloadIdentityDocument(
    int SchemaVersion,
    ValidatedPayloadIdentity Payload,
    string EntryPoint,
    IReadOnlyList<StagedPayloadFile> Files,
    string DocumentSha256);

internal static class WindowsApplicationPayloadIdentityReader
{
    private const int SchemaVersion = 1;
    private const int MaximumDocumentBytes = 32 * 1024 * 1024;
    private const int MaximumFileCount = 32_768;
    private const string RuntimeIdentifier = "win-x64";
    private const string EntryPoint = "VRRecorder.App.exe";
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private static readonly Regex ProductVersionPattern = new(
        "^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)" +
        "(?:-[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?" +
        "(?:\\+[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly string[] RootProperties =
    [
        "schemaVersion",
        "productVersion",
        "sourceRevision",
        "runtimeIdentifier",
        "entryPoint",
        "applicationExecutableSha256",
        "payloadInventorySha256",
        "legalBundleId",
        "legalManifestSha256",
        "files",
    ];
    private static readonly string[] FileProperties =
    [
        "path",
        "length",
        "sha256",
        "kind",
    ];

    public static WindowsApplicationPayloadIdentityDocument Read(
        byte[] utf8Content)
    {
        ArgumentNullException.ThrowIfNull(utf8Content);
        if (utf8Content.Length is <= 0 or > MaximumDocumentBytes)
        {
            throw Invalid();
        }

        try
        {
            using var document = JsonDocument.Parse(
                StrictUtf8.GetString(utf8Content),
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 8,
                });
            var root = document.RootElement;
            RequireExactProperties(root, RootProperties);
            if (RequiredInt32(root, "schemaVersion") != SchemaVersion)
            {
                throw Invalid();
            }

            var productVersion = RequiredString(root, "productVersion");
            var sourceRevision = RequiredString(root, "sourceRevision");
            var runtimeIdentifier = RequiredString(
                root,
                "runtimeIdentifier");
            var entryPoint = WindowsRuntimeRelativePath.RequireCanonical(
                RequiredString(root, "entryPoint"),
                "entryPoint");
            var executableSha256 = RequiredString(
                root,
                "applicationExecutableSha256");
            var inventorySha256 = RequiredString(
                root,
                "payloadInventorySha256");
            var legalBundleId = RequiredString(root, "legalBundleId");
            var legalManifestSha256 = RequiredString(
                root,
                "legalManifestSha256");
            if (productVersion.Length > 64 ||
                !ProductVersionPattern.IsMatch(productVersion) ||
                !IsCanonicalSourceRevision(sourceRevision) ||
                runtimeIdentifier != RuntimeIdentifier ||
                entryPoint != EntryPoint ||
                !IsSha256(executableSha256) ||
                !IsSha256(inventorySha256) ||
                !IsCanonicalLegalBundleId(legalBundleId) ||
                !IsSha256(legalManifestSha256))
            {
                throw Invalid();
            }

            var filesElement = root.GetProperty("files");
            if (filesElement.ValueKind != JsonValueKind.Array)
            {
                throw Invalid();
            }

            var files = filesElement.EnumerateArray()
                .Select(ParseFile)
                .ToArray();
            if (files.Length is <= 0 or > MaximumFileCount ||
                !files.Select(file => file.RelativePath)
                    .SequenceEqual(
                        files.Select(file => file.RelativePath)
                            .OrderBy(path => path, StringComparer.Ordinal),
                        StringComparer.Ordinal) ||
                files.Select(file => file.RelativePath)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
                files.Length)
            {
                throw Invalid();
            }

            RequireNoFileParentConflicts(
                files.Select(file => file.RelativePath));
            var executableMatches = files.Where(file => string.Equals(
                    file.RelativePath,
                    entryPoint,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (executableMatches.Length != 1 ||
                executableMatches[0].RelativePath != entryPoint ||
                executableMatches[0].Kind != StagedArtifactKind.Executable ||
                executableMatches[0].Sha256 != executableSha256 ||
                WindowsPublishInventoryDigest.Compute(files) != inventorySha256)
            {
                throw Invalid();
            }

            return new WindowsApplicationPayloadIdentityDocument(
                SchemaVersion,
                new ValidatedPayloadIdentity(
                    productVersion,
                    sourceRevision,
                    runtimeIdentifier,
                    executableSha256,
                    inventorySha256,
                    legalBundleId,
                    legalManifestSha256),
                entryPoint,
                files,
                Convert.ToHexString(SHA256.HashData(utf8Content))
                    .ToLowerInvariant());
        }
        catch (Exception exception) when (exception is
            JsonException or DecoderFallbackException or
            InvalidOperationException or KeyNotFoundException or
            ArgumentException)
        {
            throw new InvalidDataException(
                "The Windows application payload identity is invalid.",
                exception);
        }
    }

    private static StagedPayloadFile ParseFile(JsonElement element)
    {
        RequireExactProperties(element, FileProperties);
        var path = WindowsRuntimeRelativePath.RequireCanonical(
            RequiredString(element, "path"),
            "path");
        var length = RequiredInt64(element, "length");
        var sha256 = RequiredString(element, "sha256");
        var kind = RequiredString(element, "kind") switch
        {
            "nativeLibrary" => StagedArtifactKind.NativeLibrary,
            "executable" => StagedArtifactKind.Executable,
            "asset" => StagedArtifactKind.Asset,
            _ => throw Invalid(),
        };
        if (length < 0 || !IsSha256(sha256))
        {
            throw Invalid();
        }

        return new StagedPayloadFile(path, sha256, length, kind);
    }

    private static void RequireExactProperties(
        JsonElement element,
        string[] required)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Invalid();
        }

        var actual = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!actual.Add(property.Name))
            {
                throw Invalid();
            }
        }

        if (actual.Count != required.Length ||
            required.Any(property => !actual.Contains(property)))
        {
            throw Invalid();
        }
    }

    private static string RequiredString(JsonElement parent, string name)
    {
        var property = parent.GetProperty(name);
        if (property.ValueKind != JsonValueKind.String)
        {
            throw Invalid();
        }

        return property.GetString() is { Length: > 0 } value &&
               !string.IsNullOrWhiteSpace(value)
            ? value
            : throw Invalid();
    }

    private static int RequiredInt32(JsonElement parent, string name)
    {
        var property = parent.GetProperty(name);
        return property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt32(out var value)
            ? value
            : throw Invalid();
    }

    private static long RequiredInt64(JsonElement parent, string name)
    {
        var property = parent.GetProperty(name);
        return property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt64(out var value)
            ? value
            : throw Invalid();
    }

    private static bool IsCanonicalSourceRevision(string value) =>
        value.Length is 40 or 64 && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsCanonicalLegalBundleId(string value) =>
        value.Length is > 0 and <= 2048 &&
        value.All(character => character is >= '!' and <= '~');

    private static void RequireNoFileParentConflicts(
        IEnumerable<string> paths)
    {
        var values = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in values)
        {
            var separator = path.IndexOf('/');
            while (separator >= 0)
            {
                if (values.Contains(path[..separator]))
                {
                    throw Invalid();
                }

                separator = path.IndexOf('/', separator + 1);
            }
        }
    }

    private static InvalidDataException Invalid() => new(
        "The Windows application payload identity is invalid.");
}
