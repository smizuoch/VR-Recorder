using System.Globalization;
using System.Text.Json;

namespace VRRecorder.Compliance.Coverage;

public static class NativeGcovCoverageGate
{
    public const double ReleaseThresholdPercentage = 90;

    public static NativeCoverageSummary Evaluate(
        IEnumerable<string> jsonDocuments,
        string firstPartySourcePathFragment)
    {
        ArgumentNullException.ThrowIfNull(jsonDocuments);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            firstPartySourcePathFragment);
        var fragment = NormalizePath(firstPartySourcePathFragment);
        var lines = new Dictionary<LineKey, bool>();
        var branches = new Dictionary<BranchKey, bool>();

        foreach (var json in jsonDocuments)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(json);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("files", out var files) ||
                files.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException(
                    "The gcov JSON document has no files array.");
            }

            foreach (var file in files.EnumerateArray())
            {
                var path = RequiredString(file, "file");
                if (!NormalizePath(path).Contains(
                        fragment,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                var sourceLines = RequiredArray(file, "lines");
                foreach (var line in sourceLines.EnumerateArray())
                {
                    var lineNumber = RequiredInt32(line, "line_number");
                    var count = RequiredInt64(line, "count");
                    var lineKey = new LineKey(path, lineNumber);
                    lines[lineKey] = lines.GetValueOrDefault(lineKey) ||
                                     count > 0;

                    var sourceBranches = RequiredArray(line, "branches");
                    var branchIndex = 0;
                    foreach (var branch in sourceBranches.EnumerateArray())
                    {
                        var branchCount = RequiredInt64(branch, "count");
                        var branchKey = new BranchKey(
                            path,
                            lineNumber,
                            branchIndex++);
                        branches[branchKey] =
                            branches.GetValueOrDefault(branchKey) ||
                            branchCount > 0;
                    }
                }
            }
        }

        if (lines.Count == 0)
        {
            throw new InvalidDataException(
                "The gcov documents contain no first-party source lines.");
        }

        return new NativeCoverageSummary(
            lines.Count,
            lines.Count(pair => pair.Value),
            branches.Count,
            branches.Count(pair => pair.Value),
            Percentage(lines.Count(pair => pair.Value), lines.Count),
            Percentage(
                branches.Count(pair => pair.Value),
                branches.Count));
    }

    public static NativeCoverageSummary EnsureReleaseThreshold(
        IEnumerable<string> jsonDocuments,
        string firstPartySourcePathFragment)
    {
        var summary = Evaluate(jsonDocuments, firstPartySourcePathFragment);
        if (summary.LinePercentage < ReleaseThresholdPercentage)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"Native line coverage {summary.LinePercentage:0.00}% is below {ReleaseThresholdPercentage:0.00}%."));
        }

        if (summary.TotalBranches == 0 ||
            summary.BranchPercentage < ReleaseThresholdPercentage)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"Native branch coverage {summary.BranchPercentage:0.00}% is below {ReleaseThresholdPercentage:0.00}%."));
        }

        return summary;
    }

    private static double Percentage(int covered, int total) =>
        total == 0 ? 0 : covered * 100.0 / total;

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/');

    private static JsonElement RequiredArray(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Array
            ? value
            : throw new InvalidDataException(
                $"The gcov property '{name}' must be an array.");

    private static string RequiredString(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? throw new InvalidDataException(
                $"The gcov property '{name}' cannot be null.")
            : throw new InvalidDataException(
                $"The gcov property '{name}' must be a string.");

    private static int RequiredInt32(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.TryGetInt32(out var result)
            ? result
            : throw new InvalidDataException(
                $"The gcov property '{name}' must be an integer.");

    private static long RequiredInt64(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.TryGetInt64(out var result)
            ? result
            : throw new InvalidDataException(
                $"The gcov property '{name}' must be an integer.");

    private readonly record struct LineKey(string File, int Line);

    private readonly record struct BranchKey(
        string File,
        int Line,
        int Index);
}
