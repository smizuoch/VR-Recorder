using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace VRRecorder.Compliance.Distribution;

internal sealed record WindowsAppCertificationReport(
    string OverallResult,
    IReadOnlyList<string> NonPassingResults)
{
    public bool IsPassed =>
        string.Equals(OverallResult, "PASS", StringComparison.Ordinal) &&
        NonPassingResults.Count == 0;
}

internal static class WindowsAppCertificationReportReader
{
    private const int MaximumReportBytes = 32 * 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static WindowsAppCertificationReport Read(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length == 0 || content.Length > MaximumReportBytes)
        {
            throw Invalid();
        }

        try
        {
            using var textReader = new StringReader(
                StrictUtf8.GetString(content));
            using var xmlReader = XmlReader.Create(
                textReader,
                new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = MaximumReportBytes,
                    IgnoreComments = false,
                    IgnoreProcessingInstructions = false,
                });
            var document = XDocument.Load(
                xmlReader,
                LoadOptions.PreserveWhitespace);
            var root = document.Root;
            if (root is null ||
                !string.Equals(
                    root.Name.LocalName,
                    "REPORT",
                    StringComparison.Ordinal) ||
                root.Name.Namespace != XNamespace.None)
            {
                throw Invalid();
            }

            var overallAttributes = root.Attributes()
                .Where(attribute => string.Equals(
                    attribute.Name.LocalName,
                    "OVERALL_RESULT",
                    StringComparison.Ordinal))
                .ToArray();
            if (overallAttributes.Length != 1)
            {
                throw Invalid();
            }

            var overallResult = Normalize(overallAttributes[0].Value);
            if (overallResult.Length == 0)
            {
                throw Invalid();
            }

            var nonPassingResults = root
                .DescendantsAndSelf()
                .SelectMany(element => element.Attributes()
                    .Where(attribute =>
                        attribute.Name.LocalName.EndsWith(
                            "RESULT",
                            StringComparison.Ordinal)))
                .Select(attribute => Normalize(attribute.Value))
                .Where(result => result.Length == 0 || !IsPassing(result))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(result => result, StringComparer.Ordinal)
                .ToArray();
            return new WindowsAppCertificationReport(
                overallResult,
                nonPassingResults);
        }
        catch (Exception exception) when (exception is
            XmlException or DecoderFallbackException or
            InvalidOperationException or ArgumentException)
        {
            throw new InvalidDataException(
                "The Windows App Certification Kit report is invalid.",
                exception);
        }
    }

    private static bool IsPassing(string value) => value is
        "PASS" or "PASSED" or "SUCCESS";

    private static string Normalize(string value) =>
        value.Trim()
            .Replace('-', '_')
            .Replace(' ', '_')
            .ToUpperInvariant();

    private static InvalidDataException Invalid() => new(
        "The Windows App Certification Kit report is invalid.");
}
