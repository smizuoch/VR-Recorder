using VRRecorder.Compliance.Repository;

namespace VRRecorder.Compliance.Tests.Repository;

public sealed class ReadmeBilingualParityValidatorTests
{
    private const string MinimalValidReadme = """
        # Product

        ## 日本語
        <!-- readme-release: version=1 -->

        ## English
        <!-- readme-release: version=1 -->
        """;

    [Fact]
    public void StructurallyPairedReadmeIsAcceptedWithoutComparingLocalizedBodyText()
    {
        const string markdown = """
            # Product

            ## 日本語

            <!-- readme-parity: overview -->
            ### 概要

            日本語の説明と `Some.Api.Name`。

            <!-- readme-parity: status -->
            ### 状態

            <!-- readme-release: design-version=0.3; readiness=implementation; distributable=false -->

            ## English

            <!-- readme-parity: overview -->
            ### Overview

            Deliberately different localized prose and MIT license wording.

            <!-- readme-parity: status -->
            ### Status

            <!-- readme-release: distributable=false; readiness=implementation; design-version=0.3 -->
            """;

        var issues = ReadmeBilingualParityValidator.ValidateDocument(
            "README.md",
            markdown);

        Assert.Empty(issues);
    }

    [Theory]
    [InlineData("日本語", "missing-language-section")]
    [InlineData("English", "missing-language-section")]
    public void MissingLanguageSectionFailsClosed(
        string retainedLanguage,
        string expectedCode)
    {
        var markdown = $$"""
            # Product

            ## {{retainedLanguage}}

            <!-- readme-release: version=1; readiness=implementation -->
            """;

        var issues = ReadmeBilingualParityValidator.ValidateDocument(
            "README.md",
            markdown);

        Assert.Contains(issues, issue => issue.Code == expectedCode);
    }

    [Fact]
    public void DuplicateLanguageSectionFailsClosed()
    {
        const string markdown = """
            # Product

            ## 日本語
            <!-- readme-release: version=1 -->

            ## English
            <!-- readme-release: version=1 -->

            ## English
            <!-- readme-release: version=1 -->
            """;

        var issues = ReadmeBilingualParityValidator.ValidateDocument(
            "README.md",
            markdown);

        Assert.Contains(issues, issue =>
            issue.Code == "duplicate-language-section");
    }

    [Fact]
    public void AdditionalUnlocalizedSectionFailsClosed()
    {
        const string markdown = """
            # Product

            ## 日本語
            <!-- readme-release: version=1 -->

            ## English
            <!-- readme-release: version=1 -->

            ## Release Notes
            """;

        var issues = ReadmeBilingualParityValidator.ValidateDocument(
            "README.md",
            markdown);

        Assert.Contains(issues, issue =>
            issue.Code == "unexpected-readme-section");
    }

    [Fact]
    public void AddedOrMissingMajorHeadingFailsClosed()
    {
        const string markdown = """
            # Product

            ## 日本語
            <!-- readme-parity: overview -->
            ### 概要
            <!-- readme-parity: status -->
            ### 状態
            <!-- readme-release: version=1 -->

            ## English
            <!-- readme-parity: overview -->
            ### Overview
            <!-- readme-release: version=1 -->
            """;

        var issues = ReadmeBilingualParityValidator.ValidateDocument(
            "README.md",
            markdown);

        Assert.Contains(issues, issue =>
            issue.Code == "major-heading-parity-mismatch" &&
            issue.Subject.Contains("status", StringComparison.Ordinal));
    }

    [Fact]
    public void ReorderedMajorHeadingFailsClosed()
    {
        const string markdown = """
            # Product

            ## 日本語
            <!-- readme-parity: overview -->
            ### 概要
            <!-- readme-parity: status -->
            ### 状態
            <!-- readme-release: version=1 -->

            ## English
            <!-- readme-parity: status -->
            ### Status
            <!-- readme-release: version=1 -->
            <!-- readme-parity: overview -->
            ### Overview
            """;

        var issues = ReadmeBilingualParityValidator.ValidateDocument(
            "README.md",
            markdown);

        Assert.Contains(issues, issue =>
            issue.Code == "major-heading-order-mismatch");
    }

    [Fact]
    public void UnmarkedOrDuplicateMajorHeadingFailsClosed()
    {
        const string markdown = """
            # Product

            ## 日本語
            ### 概要
            <!-- readme-parity: status -->
            ### 状態
            <!-- readme-parity: duplicate-status -->
            ### 状態
            <!-- readme-release: version=1 -->

            ## English
            <!-- readme-parity: status -->
            ### Status
            <!-- readme-parity: duplicate-status -->
            ### Other status
            <!-- readme-release: version=1 -->
            """;

        var issues = ReadmeBilingualParityValidator.ValidateDocument(
            "README.md",
            markdown);

        Assert.Contains(issues, issue =>
            issue.Code == "unmarked-major-heading");
        Assert.Contains(issues, issue =>
            issue.Code == "duplicate-major-heading");
    }

    [Fact]
    public void DuplicateParityKeyFailsClosedEvenWhenVisibleHeadingsDiffer()
    {
        const string markdown = """
            # Product

            ## 日本語
            <!-- readme-parity: overview -->
            ### 概要
            <!-- readme-parity: overview -->
            ### 詳細
            <!-- readme-release: version=1 -->

            ## English
            <!-- readme-parity: overview -->
            ### Overview
            <!-- readme-parity: details -->
            ### Details
            <!-- readme-release: version=1 -->
            """;

        var issues = ReadmeBilingualParityValidator.ValidateDocument(
            "README.md",
            markdown);

        Assert.Contains(issues, issue =>
            issue.Code == "duplicate-heading-parity-key");
    }

    [Fact]
    public void ReleaseInformationMustExistOncePerLanguageAndMatchByKey()
    {
        const string markdown = """
            # Product

            ## 日本語
            <!-- readme-release: design-version=0.3; readiness=implementation -->

            ## English
            <!-- readme-release: design-version=0.4; readiness=implementation -->
            """;

        var issues = ReadmeBilingualParityValidator.ValidateDocument(
            "README.md",
            markdown);

        Assert.Contains(issues, issue =>
            issue.Code == "release-information-parity-mismatch");
    }

    [Fact]
    public void MarkdownInsideFencedCodeDoesNotCreateSectionsOrMarkers()
    {
        const string markdown = """
            # Product

            ## 日本語
            <!-- readme-release: version=1 -->

            `<inline-code>` is content, not a fenced-code opener.

            ```markdown
            ## English
            <!-- readme-parity: fake -->
            ### Fake
            ```

            ## English
            <!-- readme-release: version=1 -->
            """;

        var issues = ReadmeBilingualParityValidator.ValidateDocument(
            "README.md",
            markdown);

        Assert.Empty(issues);
    }

    [Fact]
    public void EveryTopLevelTemplateReadmeIsDiscoveredFailClosed()
    {
        var root = Directory.CreateTempSubdirectory(
            "vr-recorder-readme-parity-");
        try
        {
            WriteReadme(root.FullName, "README.md", MinimalValidReadme);
            WriteReadme(root.FullName, "ui-template/README.md", MinimalValidReadme);
            WriteReadme(root.FullName, "legal-template/README.md", MinimalValidReadme);
            WriteReadme(
                root.FullName,
                "future-template/README.md",
                """
                # Future template

                ## 日本語
                <!-- readme-release: version=1 -->

                ## English
                <!-- readme-release: version=2 -->
                """);

            var issues = ReadmeBilingualParityValidator.VerifyRequiredReadmes(
                root.FullName);

            Assert.Contains(issues, issue =>
                issue.Code == "release-information-parity-mismatch" &&
                issue.Subject == "future-template/README.md");
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static void WriteReadme(
        string root,
        string relativePath,
        string markdown)
    {
        var path = Path.Combine(
            root,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, markdown);
    }
}
