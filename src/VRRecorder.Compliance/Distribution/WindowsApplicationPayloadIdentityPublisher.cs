using System.Text.Json;
using VRRecorder.Compliance.Staging;

namespace VRRecorder.Compliance.Distribution;

public sealed record WindowsApplicationPayloadIdentityPublication(
    string? IdentityPath,
    IReadOnlyList<ComplianceIssue> Issues)
{
    public bool IsPublished => IdentityPath is not null && Issues.Count == 0;
}

public static class WindowsApplicationPayloadIdentityPublisher
{
    public static async Task<WindowsApplicationPayloadIdentityPublication> PublishAsync(
        SealedWindowsApplicationPayload payload,
        string identityOutputPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(identityOutputPath);
        cancellationToken.ThrowIfCancellationRequested();

        var outputPath = Path.GetFullPath(identityOutputPath);
        if (!Path.IsPathFullyQualified(identityOutputPath) ||
            !string.Equals(outputPath, identityOutputPath, PathComparison))
        {
            return Reject(
                "application-payload-identity-output-invalid",
                identityOutputPath);
        }

        var outputParent = Path.GetDirectoryName(outputPath);
        if (outputParent is null ||
            !RepositoryEvidenceRoot.TryResolve(
                outputParent,
                out var canonicalParent) ||
            !string.Equals(outputParent, canonicalParent, PathComparison))
        {
            return Reject(
                "application-payload-identity-output-invalid",
                outputPath);
        }

        var payloadRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(payload.RootDirectory));
        if (IsWithinRoot(outputPath, payloadRoot))
        {
            return Reject(
                "application-payload-identity-output-overlaps-payload",
                outputPath);
        }

        if (File.Exists(outputPath) || Directory.Exists(outputPath))
        {
            return Reject(
                "application-payload-identity-output-exists",
                outputPath);
        }

        var temporaryPath = Path.Combine(
            canonicalParent,
            $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var bytes = Generate(payload);
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 81920,
                             FileOptions.Asynchronous |
                             FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, outputPath);
            return new WindowsApplicationPayloadIdentityPublication(
                outputPath,
                []);
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or
            InvalidOperationException or ArgumentException)
        {
            return Reject(
                "application-payload-identity-publication-failed",
                outputPath);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception exception) when (exception is
                IOException or UnauthorizedAccessException)
            {
                // The publication result remains fail-closed even if cleanup
                // of an uncommitted sibling cannot complete.
            }
        }
    }

    internal static byte[] Generate(SealedWindowsApplicationPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(
                   output,
                   new JsonWriterOptions
                   {
                       Indented = true,
                       SkipValidation = false,
                   }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("productVersion", payload.ProductVersion);
            writer.WriteString("sourceRevision", payload.SourceRevision);
            writer.WriteString(
                "runtimeIdentifier",
                payload.RuntimeIdentifier);
            writer.WriteString("entryPoint", payload.EntryPoint);
            writer.WriteString(
                "applicationExecutableSha256",
                payload.EntryPointSha256);
            writer.WriteString(
                "payloadInventorySha256",
                payload.InventorySha256);
            writer.WriteString("legalBundleId", payload.LegalBundleId);
            writer.WriteString(
                "legalManifestSha256",
                payload.LegalManifestSha256);
            writer.WriteStartArray("files");
            foreach (var file in payload.Files
                         .OrderBy(
                             file => file.RelativePath,
                             StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("path", file.RelativePath);
                writer.WriteNumber("length", file.Length);
                writer.WriteString("sha256", file.Sha256);
                writer.WriteString("kind", Kind(file.Kind));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        output.WriteByte((byte)'\n');
        return output.ToArray();
    }

    private static string Kind(StagedArtifactKind kind) => kind switch
    {
        StagedArtifactKind.NativeLibrary => "nativeLibrary",
        StagedArtifactKind.Executable => "executable",
        StagedArtifactKind.Asset => "asset",
        _ => throw new InvalidOperationException(
            "The sealed payload contains an unknown artifact kind."),
    };

    private static bool IsWithinRoot(string path, string root)
    {
        var prefix = root + Path.DirectorySeparatorChar;
        return string.Equals(path, root, PathComparison) ||
               path.StartsWith(prefix, PathComparison);
    }

    private static WindowsApplicationPayloadIdentityPublication Reject(
        string code,
        string subject) => new(
        null,
        [new ComplianceIssue(code, subject)]);

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
}
