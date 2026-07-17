using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace VRRecorder.Compliance.Coverage;

public static class ManagedCoberturaCoverageCommand
{
    public const double ReleaseThresholdPercentage = 80;
    private const long MaximumReportBytes = 64 * 1024 * 1024;
    private static readonly string[] RequiredAssemblies =
    [
        "VRRecorder.Application",
        "VRRecorder.Compliance",
        "VRRecorder.Domain",
        "VRRecorder.Infrastructure.Media",
        "VRRecorder.Infrastructure.Osc",
        "VRRecorder.Infrastructure.SteamVr",
        "VRRecorder.Infrastructure.Storage",
        "VRRecorder.Presentation.Wrist",
    ];

    public static int Run(
        IReadOnlyList<string> arguments,
        TextWriter output,
        TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        if (arguments.Count != 1 ||
            string.IsNullOrWhiteSpace(arguments[0]))
        {
            error.WriteLine("Usage: <coverage.cobertura.xml>");
            return 2;
        }

        try
        {
            var rates = ReadRates(arguments[0]);
            foreach (var assembly in RequiredAssemblies)
            {
                if (!rates.TryGetValue(assembly, out var rate))
                {
                    error.WriteLine(
                        $"required managed coverage package is missing: {assembly}.");
                    return 1;
                }

                if (rate.LinePercentage < ReleaseThresholdPercentage)
                {
                    error.WriteLine(string.Create(
                        CultureInfo.InvariantCulture,
                        $"{assembly} line coverage {rate.LinePercentage:0.00}% is below {ReleaseThresholdPercentage:0.00}%."));
                    return 1;
                }

                if (rate.BranchPercentage < ReleaseThresholdPercentage)
                {
                    error.WriteLine(string.Create(
                        CultureInfo.InvariantCulture,
                        $"{assembly} branch coverage {rate.BranchPercentage:0.00}% is below {ReleaseThresholdPercentage:0.00}%."));
                    return 1;
                }
            }

            foreach (var assembly in RequiredAssemblies)
            {
                var rate = rates[assembly];
                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{assembly}: line={rate.LinePercentage:0.00}%, branch={rate.BranchPercentage:0.00}%"));
            }
            return 0;
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or XmlException or
            InvalidDataException or InvalidOperationException or
            ArgumentException)
        {
            error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static Dictionary<string, CoverageRate> ReadRates(
        string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length is <= 0 or > MaximumReportBytes)
        {
            throw Invalid();
        }

        using var reader = XmlReader.Create(
            path,
            new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                MaxCharactersInDocument = MaximumReportBytes,
                XmlResolver = null,
            });
        var document = XDocument.Load(reader, LoadOptions.None);
        if (document.Root is not { Name.LocalName: "coverage" } root)
        {
            throw Invalid();
        }

        var packages = root.Elements()
            .Single(element => element.Name.LocalName == "packages")
            .Elements()
            .Where(element => element.Name.LocalName == "package");
        var result = new Dictionary<string, CoverageRate>(
            StringComparer.Ordinal);
        foreach (var package in packages)
        {
            var name = RequiredAttribute(package, "name");
            var lineRate = ParseRate(RequiredAttribute(package, "line-rate"));
            var branchRate = ParseRate(
                RequiredAttribute(package, "branch-rate"));
            if (!result.TryAdd(
                    name,
                    new CoverageRate(lineRate * 100, branchRate * 100)))
            {
                throw Invalid();
            }
        }

        return result;
    }

    private static string RequiredAttribute(XElement element, string name) =>
        element.Attribute(name)?.Value is { Length: > 0 } value
            ? value
            : throw Invalid();

    private static double ParseRate(string value) =>
        double.TryParse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var rate) &&
        double.IsFinite(rate) &&
        rate is >= 0 and <= 1
            ? rate
            : throw Invalid();

    private static InvalidDataException Invalid() => new(
        "The managed Cobertura coverage report is invalid.");

    private sealed record CoverageRate(
        double LinePercentage,
        double BranchPercentage);
}
