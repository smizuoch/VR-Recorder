namespace VRRecorder.Compliance.Repository;

public static class ReadmeBilingualParityValidator
{
    private const string JapaneseSection = "日本語";
    private const string EnglishSection = "English";

    private static readonly string[] RequiredReadmePaths =
    [
        "README.md",
        "ui-template/README.md",
        "legal-template/README.md",
    ];

    public static IReadOnlyList<ComplianceIssue> VerifyRequiredReadmes(
        string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var root = Path.GetFullPath(repositoryRoot);
        var issues = new List<ComplianceIssue>();

        foreach (var relativePath in RequiredReadmePaths)
        {
            var path = Path.Combine(
                root,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                issues.Add(new ComplianceIssue("missing-readme", relativePath));
                continue;
            }

            try
            {
                issues.AddRange(ValidateDocument(
                    relativePath,
                    File.ReadAllText(path)));
            }
            catch (IOException)
            {
                issues.Add(new ComplianceIssue("unreadable-readme", relativePath));
            }
            catch (UnauthorizedAccessException)
            {
                issues.Add(new ComplianceIssue("unreadable-readme", relativePath));
            }
        }

        return issues;
    }

    public static IReadOnlyList<ComplianceIssue> ValidateDocument(
        string relativePath,
        string markdown)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(markdown);

        var issues = new List<ComplianceIssue>();
        var sections = Parse(relativePath, markdown, issues);

        ValidateLanguageSection(relativePath, JapaneseSection, sections, issues);
        ValidateLanguageSection(relativePath, EnglishSection, sections, issues);

        if (sections.FirstSectionName is not null &&
            !string.Equals(
                sections.FirstSectionName,
                JapaneseSection,
                StringComparison.Ordinal))
        {
            issues.Add(new ComplianceIssue(
                "language-section-order-mismatch",
                relativePath));
        }

        if (sections.ByName.TryGetValue(JapaneseSection, out var japanese) &&
            sections.ByName.TryGetValue(EnglishSection, out var english))
        {
            CompareHeadings(relativePath, japanese, english, issues);
            CompareReleaseInformation(relativePath, japanese, english, issues);
        }

        return issues;
    }

    private static ParsedDocument Parse(
        string relativePath,
        string markdown,
        List<ComplianceIssue> issues)
    {
        var sections = new ParsedDocument();
        LanguageContent? current = null;
        string? pendingParityKey = null;
        var pendingParityLine = 0;
        var lineNumber = 0;
        var fence = new CodeFence();

        using var reader = new StringReader(markdown);
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            if (fence.TryConsume(line))
            {
                continue;
            }

            if (TryParseHeading(line, out var level, out var title))
            {
                if (level == 2)
                {
                    ReportOrphanMarker(
                        relativePath,
                        current,
                        pendingParityKey,
                        pendingParityLine,
                        issues);
                    pendingParityKey = null;
                    current = EnterSection(
                        relativePath,
                        title,
                        sections,
                        issues);
                    continue;
                }

                if (current is not null && level == 3)
                {
                    AddMajorHeading(
                        relativePath,
                        current,
                        pendingParityKey,
                        title,
                        lineNumber,
                        issues);
                    pendingParityKey = null;
                    continue;
                }

                if (pendingParityKey is not null)
                {
                    issues.Add(new ComplianceIssue(
                        "invalid-major-heading-level",
                        FormatSubject(
                            relativePath,
                            current?.Name,
                            pendingParityKey,
                            lineNumber)));
                    pendingParityKey = null;
                }

                continue;
            }

            var trimmed = line.Trim();
            if (TryParseMarker(trimmed, "readme-parity", out var parityValue))
            {
                ReportOrphanMarker(
                    relativePath,
                    current,
                    pendingParityKey,
                    pendingParityLine,
                    issues);
                pendingParityKey = null;

                if (current is null || !IsParityKey(parityValue))
                {
                    issues.Add(new ComplianceIssue(
                        "invalid-heading-parity-marker",
                        FormatSubject(
                            relativePath,
                            current?.Name,
                            parityValue,
                            lineNumber)));
                }
                else
                {
                    pendingParityKey = parityValue;
                    pendingParityLine = lineNumber;
                }

                continue;
            }

            if (TryParseMarker(trimmed, "readme-release", out var releaseValue))
            {
                ReportOrphanMarker(
                    relativePath,
                    current,
                    pendingParityKey,
                    pendingParityLine,
                    issues);
                pendingParityKey = null;
                AddReleaseInformation(
                    relativePath,
                    current,
                    releaseValue,
                    lineNumber,
                    issues);
                continue;
            }

            if (trimmed.StartsWith("<!-- readme-", StringComparison.Ordinal))
            {
                ReportOrphanMarker(
                    relativePath,
                    current,
                    pendingParityKey,
                    pendingParityLine,
                    issues);
                pendingParityKey = null;
                issues.Add(new ComplianceIssue(
                    "invalid-readme-marker",
                    $"{relativePath}:{lineNumber}"));
                continue;
            }

            if (trimmed.Length > 0 && pendingParityKey is not null)
            {
                ReportOrphanMarker(
                    relativePath,
                    current,
                    pendingParityKey,
                    pendingParityLine,
                    issues);
                pendingParityKey = null;
            }
        }

        ReportOrphanMarker(
            relativePath,
            current,
            pendingParityKey,
            pendingParityLine,
            issues);
        return sections;
    }

    private static LanguageContent? EnterSection(
        string relativePath,
        string title,
        ParsedDocument document,
        List<ComplianceIssue> issues)
    {
        if (!string.Equals(title, JapaneseSection, StringComparison.Ordinal) &&
            !string.Equals(title, EnglishSection, StringComparison.Ordinal))
        {
            issues.Add(new ComplianceIssue(
                "unexpected-readme-section",
                $"{relativePath}:{title}"));
            return null;
        }

        document.FirstSectionName ??= title;
        if (document.ByName.ContainsKey(title))
        {
            issues.Add(new ComplianceIssue(
                "duplicate-language-section",
                $"{relativePath}:{title}"));
            return null;
        }

        var section = new LanguageContent(title);
        document.ByName.Add(title, section);
        return section;
    }

    private static void AddMajorHeading(
        string relativePath,
        LanguageContent section,
        string? parityKey,
        string title,
        int lineNumber,
        List<ComplianceIssue> issues)
    {
        if (!section.VisibleHeadings.Add(title))
        {
            issues.Add(new ComplianceIssue(
                "duplicate-major-heading",
                FormatSubject(relativePath, section.Name, title, lineNumber)));
        }

        if (parityKey is null)
        {
            issues.Add(new ComplianceIssue(
                "unmarked-major-heading",
                FormatSubject(relativePath, section.Name, title, lineNumber)));
            return;
        }

        if (!section.ParityKeys.Add(parityKey))
        {
            issues.Add(new ComplianceIssue(
                "duplicate-heading-parity-key",
                FormatSubject(
                    relativePath,
                    section.Name,
                    parityKey,
                    lineNumber)));
        }

        section.Headings.Add(new MajorHeading(parityKey));
    }

    private static void AddReleaseInformation(
        string relativePath,
        LanguageContent? section,
        string value,
        int lineNumber,
        List<ComplianceIssue> issues)
    {
        if (section is null || !TryParseReleaseInformation(value, out var release))
        {
            issues.Add(new ComplianceIssue(
                "invalid-release-information",
                FormatSubject(relativePath, section?.Name, value, lineNumber)));
            return;
        }

        section.ReleaseInformation.Add(release);
    }

    private static void ValidateLanguageSection(
        string relativePath,
        string language,
        ParsedDocument document,
        List<ComplianceIssue> issues)
    {
        if (!document.ByName.TryGetValue(language, out var section))
        {
            issues.Add(new ComplianceIssue(
                "missing-language-section",
                $"{relativePath}:{language}"));
            return;
        }

        if (section.ReleaseInformation.Count == 0)
        {
            issues.Add(new ComplianceIssue(
                "missing-release-information",
                $"{relativePath}:{language}"));
        }
        else if (section.ReleaseInformation.Count > 1)
        {
            issues.Add(new ComplianceIssue(
                "duplicate-release-information",
                $"{relativePath}:{language}"));
        }
    }

    private static void CompareHeadings(
        string relativePath,
        LanguageContent japanese,
        LanguageContent english,
        List<ComplianceIssue> issues)
    {
        foreach (var key in japanese.ParityKeys.Except(english.ParityKeys))
        {
            issues.Add(new ComplianceIssue(
                "major-heading-parity-mismatch",
                $"{relativePath}:{key}:missing-in-English"));
        }

        foreach (var key in english.ParityKeys.Except(japanese.ParityKeys))
        {
            issues.Add(new ComplianceIssue(
                "major-heading-parity-mismatch",
                $"{relativePath}:{key}:missing-in-日本語"));
        }

        if (japanese.ParityKeys.SetEquals(english.ParityKeys) &&
            !japanese.Headings.Select(item => item.ParityKey).SequenceEqual(
                english.Headings.Select(item => item.ParityKey),
                StringComparer.Ordinal))
        {
            issues.Add(new ComplianceIssue(
                "major-heading-order-mismatch",
                relativePath));
        }
    }

    private static void CompareReleaseInformation(
        string relativePath,
        LanguageContent japanese,
        LanguageContent english,
        List<ComplianceIssue> issues)
    {
        if (japanese.ReleaseInformation.Count != 1 ||
            english.ReleaseInformation.Count != 1)
        {
            return;
        }

        if (!ReleaseInformationEquals(
                japanese.ReleaseInformation[0],
                english.ReleaseInformation[0]))
        {
            issues.Add(new ComplianceIssue(
                "release-information-parity-mismatch",
                relativePath));
        }
    }

    private static bool ReleaseInformationEquals(
        Dictionary<string, string> left,
        Dictionary<string, string> right) =>
        left.Count == right.Count && left.All(pair =>
            right.TryGetValue(pair.Key, out var value) &&
            string.Equals(pair.Value, value, StringComparison.Ordinal));

    private static bool TryParseReleaseInformation(
        string value,
        out Dictionary<string, string> release)
    {
        release = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in value.Split(';', StringSplitOptions.TrimEntries))
        {
            var separator = item.IndexOf('=');
            if (separator <= 0 || separator == item.Length - 1 ||
                item.IndexOf('=', separator + 1) >= 0)
            {
                return false;
            }

            var key = item[..separator].Trim();
            var itemValue = item[(separator + 1)..].Trim();
            if (!IsParityKey(key) || itemValue.Length == 0 ||
                !release.TryAdd(key, itemValue))
            {
                return false;
            }
        }

        return release.Count > 0;
    }

    private static void ReportOrphanMarker(
        string relativePath,
        LanguageContent? section,
        string? parityKey,
        int lineNumber,
        List<ComplianceIssue> issues)
    {
        if (parityKey is null)
        {
            return;
        }

        issues.Add(new ComplianceIssue(
            "orphan-heading-parity-marker",
            FormatSubject(relativePath, section?.Name, parityKey, lineNumber)));
    }

    private static bool TryParseMarker(
        string line,
        string markerName,
        out string value)
    {
        var prefix = $"<!-- {markerName}:";
        if (!line.StartsWith(prefix, StringComparison.Ordinal) ||
            !line.EndsWith("-->", StringComparison.Ordinal))
        {
            value = string.Empty;
            return false;
        }

        value = line[prefix.Length..^3].Trim();
        return true;
    }

    private static bool TryParseHeading(
        string line,
        out int level,
        out string title)
    {
        level = 0;
        title = string.Empty;
        var start = 0;
        while (start < line.Length && start < 3 && line[start] == ' ')
        {
            start++;
        }

        var index = start;
        while (index < line.Length && line[index] == '#')
        {
            index++;
        }

        level = index - start;
        if (level is < 1 or > 6 || index >= line.Length ||
            !char.IsWhiteSpace(line[index]))
        {
            level = 0;
            return false;
        }

        title = line[index..].Trim();
        var lastContent = title.Length - 1;
        while (lastContent >= 0 && title[lastContent] == '#')
        {
            lastContent--;
        }

        if (lastContent >= 0 && lastContent < title.Length - 1 &&
            char.IsWhiteSpace(title[lastContent]))
        {
            title = title[..(lastContent + 1)].TrimEnd();
        }

        return title.Length > 0;
    }

    private static bool IsParityKey(string value)
    {
        if (value.Length == 0 || value[0] == '-' || value[^1] == '-')
        {
            return false;
        }

        var previousWasHyphen = false;
        foreach (var character in value)
        {
            if (character == '-')
            {
                if (previousWasHyphen)
                {
                    return false;
                }

                previousWasHyphen = true;
                continue;
            }

            if (character is not (>= 'a' and <= 'z') and
                not (>= '0' and <= '9'))
            {
                return false;
            }

            previousWasHyphen = false;
        }

        return true;
    }

    private static string FormatSubject(
        string relativePath,
        string? language,
        string value,
        int lineNumber) =>
        $"{relativePath}:{language ?? "outside-language-section"}:{lineNumber}:{value}";

    private sealed class ParsedDocument
    {
        public Dictionary<string, LanguageContent> ByName { get; } =
            new(StringComparer.Ordinal);

        public string? FirstSectionName { get; set; }
    }

    private sealed class LanguageContent(string name)
    {
        public string Name { get; } = name;

        public List<MajorHeading> Headings { get; } = [];

        public HashSet<string> ParityKeys { get; } =
            new(StringComparer.Ordinal);

        public HashSet<string> VisibleHeadings { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public List<Dictionary<string, string>> ReleaseInformation { get; } = [];
    }

    private sealed record MajorHeading(string ParityKey);

    private sealed class CodeFence
    {
        private char marker;
        private int length;

        public bool TryConsume(string line)
        {
            var trimmed = line.TrimStart();
            if (length == 0)
            {
                if (!TryReadFence(
                        trimmed,
                        out var openingMarker,
                        out var openingLength))
                {
                    return false;
                }

                marker = openingMarker;
                length = openingLength;
                return true;
            }

            if (IsClosingFence(trimmed, marker, length))
            {
                marker = default;
                length = 0;
            }

            return true;
        }

        private static bool TryReadFence(
            string line,
            out char fenceMarker,
            out int fenceLength)
        {
            fenceMarker = default;
            fenceLength = 0;
            if (line.Length < 3 || line[0] is not ('`' or '~'))
            {
                return false;
            }

            fenceMarker = line[0];
            while (fenceLength < line.Length &&
                   line[fenceLength] == fenceMarker)
            {
                fenceLength++;
            }

            return fenceLength >= 3;
        }

        private static bool IsClosingFence(
            string line,
            char fenceMarker,
            int minimumLength)
        {
            var markerLength = 0;
            while (markerLength < line.Length &&
                   line[markerLength] == fenceMarker)
            {
                markerLength++;
            }

            return markerLength >= minimumLength &&
                   line[markerLength..].Trim().Length == 0;
        }
    }
}
