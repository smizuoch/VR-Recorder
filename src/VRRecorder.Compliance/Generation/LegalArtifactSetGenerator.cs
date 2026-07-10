using System.Security.Cryptography;
using System.Text;

namespace VRRecorder.Compliance.Generation;

public static class LegalArtifactSetGenerator
{
    public static GeneratedLegalArtifactSet Generate(
        SpdxGenerationContext context,
        ApprovedReleaseGraph approvedGraph)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(approvedGraph);

        var artifacts = new Dictionary<string, GeneratedLegalArtifact>(
            StringComparer.Ordinal);
        AddArtifact(
            artifacts,
            "THIRD-PARTY-NOTICES.txt",
            Encoding.UTF8.GetBytes(ThirdPartyNoticeGenerator.Generate(
                context.ProductName,
                approvedGraph)));
        AddArtifact(
            artifacts,
            "SBOM/manifest.spdx.json",
            Encoding.UTF8.GetBytes(SpdxSbomGenerator.Generate(
                context,
                approvedGraph)));

        foreach (var legalFile in approvedGraph.Graph.Components
                     .SelectMany(component => component.LegalFiles)
                     .OrderBy(file => file.RelativePath, StringComparer.Ordinal))
        {
            AddArtifact(
                artifacts,
                legalFile.RelativePath,
                Encoding.UTF8.GetBytes(legalFile.Utf8Content));
        }

        return new GeneratedLegalArtifactSet(artifacts.Values
            .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
            .ToArray());
    }

    private static void AddArtifact(
        IDictionary<string, GeneratedLegalArtifact> artifacts,
        string relativePath,
        byte[] content)
    {
        var hash = Convert
            .ToHexString(SHA256.HashData(content))
            .ToLowerInvariant();
        if (!artifacts.TryAdd(
                relativePath,
                new GeneratedLegalArtifact(relativePath, content, hash)))
        {
            throw new InvalidOperationException(
                $"Legal artifact path {relativePath} appears more than once.");
        }
    }
}
