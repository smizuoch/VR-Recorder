using System.Security.Cryptography;

namespace VRRecorder.Compliance.Generation;

public static class LegalManifestGenerator
{
    public static string Generate(
        IEnumerable<GeneratedLegalArtifact> payloadArtifacts)
    {
        ArgumentNullException.ThrowIfNull(payloadArtifacts);

        return string.Concat(payloadArtifacts
            .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
            .Select(artifact =>
                $"{Hash(artifact.Content.Span)}  {artifact.RelativePath}\n"));
    }

    private static string Hash(ReadOnlySpan<byte> content) =>
        Convert
            .ToHexString(SHA256.HashData(content))
            .ToLowerInvariant();
}
