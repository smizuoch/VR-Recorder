using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace VRRecorder.Compliance.Coverage;

public static class NativeCoverageCommand
{
    private const int MaximumArtifactCharacters = 64 * 1024 * 1024;

    public static int Run(
        IReadOnlyList<string> arguments,
        TextWriter output,
        TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        if (arguments.Count < 3 ||
            !string.Equals(
                arguments[0],
                "--source-fragment",
                StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(arguments[1]))
        {
            error.WriteLine(
                "Usage: --source-fragment <path-fragment> <gcov.json.gz> [...]");
            return 2;
        }

        try
        {
            var documents = arguments
                .Skip(2)
                .Select(ReadArtifact)
                .ToArray();
            var summary = NativeGcovCoverageGate.Evaluate(
                documents,
                arguments[1]);
            var report = string.Create(
                CultureInfo.InvariantCulture,
                $"native coverage: line={summary.LinePercentage:0.00}% ({summary.CoveredLines}/{summary.TotalLines}), branch={summary.BranchPercentage:0.00}% ({summary.CoveredBranches}/{summary.TotalBranches})");
            if (summary.LinePercentage <
                NativeGcovCoverageGate.ReleaseThresholdPercentage)
            {
                error.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{report}; line coverage {summary.LinePercentage:0.00}% is below {NativeGcovCoverageGate.ReleaseThresholdPercentage:0.00}%."));
                return 1;
            }

            if (summary.TotalBranches == 0 ||
                summary.BranchPercentage <
                NativeGcovCoverageGate.ReleaseThresholdPercentage)
            {
                error.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{report}; branch coverage {summary.BranchPercentage:0.00}% is below {NativeGcovCoverageGate.ReleaseThresholdPercentage:0.00}%."));
                return 1;
            }

            output.WriteLine(report);
            return 0;
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                InvalidDataException or
                JsonException)
        {
            error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static string ReadArtifact(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var file = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.SequentialScan);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(
            gzip,
            new UTF8Encoding(false, true),
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 81920,
            leaveOpen: false);
        var buffer = new char[8192];
        var content = new StringBuilder();
        while (true)
        {
            var count = reader.Read(buffer, 0, buffer.Length);
            if (count == 0)
            {
                return content.ToString();
            }

            if (content.Length > MaximumArtifactCharacters - count)
            {
                throw new InvalidDataException(
                    "A gcov JSON artifact exceeds the 64 MiB limit.");
            }

            content.Append(buffer, 0, count);
        }
    }
}
