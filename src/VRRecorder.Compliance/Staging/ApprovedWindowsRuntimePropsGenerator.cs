using System.Text;
using System.Xml;
using System.Globalization;

namespace VRRecorder.Compliance.Staging;

internal static class ApprovedWindowsRuntimePropsGenerator
{
    private const int SchemaVersion = 2;
    private const string Profile =
        "full-production-hardware-validation-v1";
    private const string RuntimeIdentifier = "win-x64";
    private const int MaximumEntryCount = 4096;
    private const string PayloadPrefix =
        "$(MSBuildThisFileDirectory)payload/";
    private static readonly UTF8Encoding Utf8WithoutBom = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static byte[] Generate(
        WindowsRuntimeStagingManifest manifest,
        string inventorySha256)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var manifestSha256 = NormalizeSha256(manifest.ManifestSha256);
        var normalizedInventorySha256 = NormalizeSha256(inventorySha256);
        if (manifest.SchemaVersion != SchemaVersion ||
            !string.Equals(
                manifest.Profile,
                Profile,
                StringComparison.Ordinal) ||
            !string.Equals(
                manifest.RuntimeIdentifier,
                RuntimeIdentifier,
                StringComparison.Ordinal) ||
            manifest.LegalBundle is null ||
            manifest.Entries is null ||
            manifest.Entries.Count is <= 0 or > MaximumEntryCount)
        {
            throw Invalid();
        }

        var legalManifestSha256 = NormalizeSha256(
            manifest.LegalBundle.ManifestSha256);
        if (string.IsNullOrWhiteSpace(manifest.LegalBundle.BundleId) ||
            manifest.LegalBundle.BundleId.Length > 2048 ||
            manifest.LegalBundle.BundleId.Any(character =>
                character is < '!' or > '~'))
        {
            throw Invalid();
        }

        RequireXmlText(manifest.LegalBundle.BundleId);

        var entries = manifest.Entries.ToArray();
        foreach (var entry in entries)
        {
            if (entry is null)
            {
                throw Invalid();
            }

            RequireCanonicalPath(entry.Source, "source");
            RequireCanonicalPath(entry.Target, "target");
            RequireXmlText(entry.Target);
            _ = NormalizeSha256(entry.Sha256);
            if (entry.Length < 0 ||
                !Enum.IsDefined(entry.DeploymentKind))
            {
                throw Invalid();
            }
        }

        RequireUnique(entries.Select(entry => entry.Source));
        RequireUnique(entries.Select(entry => entry.Target));
        RequireNoFileParentConflicts(entries.Select(entry => entry.Source));
        RequireNoFileParentConflicts(entries.Select(entry => entry.Target));

        using var output = new MemoryStream();
        using (var writer = XmlWriter.Create(
                   output,
                   new XmlWriterSettings
                   {
                       Encoding = Utf8WithoutBom,
                       OmitXmlDeclaration = true,
                       Indent = true,
                       IndentChars = "  ",
                       NewLineChars = "\n",
                       NewLineHandling = NewLineHandling.Replace,
                       CloseOutput = false,
                   }))
        {
            writer.WriteStartElement("Project");
            writer.WriteStartElement("PropertyGroup");
            writer.WriteElementString(
                "VRRecorderApprovedWindowsRuntimeImported",
                "true");
            writer.WriteElementString(
                "VRRecorderApprovedWindowsRuntimeManifestSha256",
                manifestSha256);
            writer.WriteElementString(
                "VRRecorderApprovedWindowsRuntimeInventorySha256",
                normalizedInventorySha256);
            writer.WriteElementString(
                "VRRecorderApprovedWindowsRuntimeProfile",
                manifest.Profile);
            writer.WriteElementString(
                "VRRecorderApprovedWindowsRuntimeIdentifier",
                manifest.RuntimeIdentifier);
            writer.WriteElementString(
                "VRRecorderApprovedLegalBundleId",
                manifest.LegalBundle.BundleId);
            writer.WriteElementString(
                "VRRecorderApprovedLegalManifestSha256",
                legalManifestSha256);
            writer.WriteElementString(
                "LegalBundleId",
                manifest.LegalBundle.BundleId);
            writer.WriteElementString(
                "LegalManifestSha256",
                legalManifestSha256);
            writer.WriteEndElement();

            writer.WriteStartElement("ItemGroup");
            foreach (var entry in entries.OrderBy(
                         entry => entry.Target,
                         StringComparer.Ordinal))
            {
                writer.WriteStartElement("Content");
                writer.WriteAttributeString(
                    "Include",
                    PayloadPrefix + entry.Target);
                writer.WriteElementString("Link", entry.Target);
                writer.WriteElementString("TargetPath", entry.Target);
                writer.WriteElementString(
                    "VRRecorderSha256",
                    NormalizeSha256(entry.Sha256));
                writer.WriteElementString(
                    "VRRecorderLength",
                    entry.Length.ToString(CultureInfo.InvariantCulture));
                writer.WriteElementString(
                    "VRRecorderKind",
                    ArtifactKind(entry.DeploymentKind));
                writer.WriteElementString(
                    "CopyToOutputDirectory",
                    "IfDifferent");
                writer.WriteElementString(
                    "CopyToPublishDirectory",
                    "IfDifferent");
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteWhitespace("\n");
        }

        return output.ToArray();
    }

    private static string NormalizeSha256(string value)
    {
        if (value is null ||
            value.Length != 64 ||
            value.Any(character =>
                character is not (>= '0' and <= '9') and
                    not (>= 'a' and <= 'f') and
                    not (>= 'A' and <= 'F')))
        {
            throw Invalid();
        }

        return value.ToLowerInvariant();
    }

    private static string ArtifactKind(
        WindowsRuntimeDeploymentKind deploymentKind) => deploymentKind switch
        {
            WindowsRuntimeDeploymentKind.NativeLibrary =>
                StagedArtifactKind.NativeLibrary.ToString(),
            WindowsRuntimeDeploymentKind.Executable =>
                StagedArtifactKind.Executable.ToString(),
            WindowsRuntimeDeploymentKind.Asset or
                WindowsRuntimeDeploymentKind.Evidence =>
                StagedArtifactKind.Asset.ToString(),
            _ => throw Invalid(),
        };

    private static void RequireCanonicalPath(
        string path,
        string propertyName)
    {
        try
        {
            WindowsRuntimeRelativePath.RequireCanonical(path, propertyName);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                "The Windows runtime staging manifest is invalid.",
                exception);
        }
    }

    private static void RequireXmlText(string value)
    {
        try
        {
            XmlConvert.VerifyXmlChars(value);
        }
        catch (XmlException exception)
        {
            throw new InvalidDataException(
                "The Windows runtime staging manifest is invalid.",
                exception);
        }
    }

    private static void RequireUnique(IEnumerable<string> paths)
    {
        var values = paths.ToArray();
        if (values.Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
            values.Length)
        {
            throw Invalid();
        }
    }

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
        "The Windows runtime staging manifest is invalid.");
}
