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
            "THIRD-PARTY-NOTICES.html",
            Encoding.UTF8.GetBytes(ThirdPartyNoticeHtmlGenerator.Generate(
                context.ProductName,
                approvedGraph)));
        AddArtifact(
            artifacts,
            "SBOM/manifest.spdx.json",
            Encoding.UTF8.GetBytes(SpdxSbomGenerator.Generate(
                context,
                approvedGraph)));
        AddArtifact(
            artifacts,
            "THIRD-PARTY-COMPONENTS.json",
            Encoding.UTF8.GetBytes(ThirdPartyComponentsGenerator.Generate(
                context,
                approvedGraph)));

        foreach (var legalFileGroup in approvedGraph.Graph.Components
                     .SelectMany(component => component.LegalFiles)
                     .GroupBy(
                         file => file.RelativePath,
                         StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var legalFiles = legalFileGroup
                .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
                .ToArray();
            var legalFile = legalFiles[0];
            if (legalFiles.Skip(1).Any(file =>
                    file.Kind != legalFile.Kind ||
                    !string.Equals(
                        file.RelativePath,
                        legalFile.RelativePath,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        file.Sha256,
                        legalFile.Sha256,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(
                        file.Utf8Content,
                        legalFile.Utf8Content,
                        StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"Legal artifact path {legalFileGroup.Key} has conflicting payloads.");
            }

            AddArtifact(
                artifacts,
                legalFile.RelativePath,
                Encoding.UTF8.GetBytes(legalFile.Utf8Content));
        }

        AddArtifact(
            artifacts,
            "LEGAL-MANIFEST.sha256",
            Encoding.UTF8.GetBytes(
                LegalManifestGenerator.Generate(artifacts.Values)));

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
