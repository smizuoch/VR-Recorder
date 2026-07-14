using System.Text;
using System.Xml;

namespace VRRecorder.Compliance.Staging;

internal static class ApprovedWindowsRuntimePropsGenerator
{
    private const int SchemaVersion = 1;
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
            manifest.Entries is null ||
            manifest.Entries.Count is <= 0 or > MaximumEntryCount)
        {
            throw Invalid();
        }

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
