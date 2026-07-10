using VRRecorder.Compliance.Staging;

namespace VRRecorder.Compliance.Tests.Staging;

public sealed class StagingInventoryValidatorTests
{
    [Fact]
    public void WindowsEquivalentPathsAreRejectedAsDuplicate()
    {
        StagedPayloadFile[] actualFiles =
        [
            new("native/A.dll", "aa", 1, StagedArtifactKind.NativeLibrary),
            new("native/a.DLL", "aa", 1, StagedArtifactKind.NativeLibrary),
        ];
        RegisteredStagedArtifact[] registrations =
        [
            new(
                "component-a",
                "native/A.dll",
                "aa",
                StagedArtifactKind.NativeLibrary),
            new(
                "component-b",
                "native/a.DLL",
                "aa",
                StagedArtifactKind.NativeLibrary),
        ];

        var issues = StagingInventoryValidator.Validate(
            actualFiles,
            registrations);

        var issue = Assert.Single(issues);
        Assert.Equal("duplicate-staging-path", issue.Code);
        Assert.Equal("native/A.dll", issue.Subject);
    }
}
