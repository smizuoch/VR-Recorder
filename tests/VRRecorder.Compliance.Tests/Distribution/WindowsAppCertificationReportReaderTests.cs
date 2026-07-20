using System.Text;
using VRRecorder.Compliance.Distribution;

namespace VRRecorder.Compliance.Tests.Distribution;

public sealed class WindowsAppCertificationReportReaderTests
{
    [Fact]
    public void AcceptsOnlyAnOverallPassWithPassingNestedResults()
    {
        var report = WindowsAppCertificationReportReader.Read(Bytes(
            "<REPORT OVERALL_RESULT=\"PASS\">" +
            "<TEST NAME=\"manifest\" RESULT=\"PASS\" />" +
            "<TEST NAME=\"launch\" TEST_RESULT=\"SUCCESS\" />" +
            "</REPORT>"));

        Assert.True(report.IsPassed);
        Assert.Empty(report.NonPassingResults);
    }

    [Theory]
    [InlineData("FAIL")]
    [InlineData("NOT RUN")]
    [InlineData("NOT_APPLICABLE")]
    [InlineData("INAPPLICABLE")]
    [InlineData("SKIPPED")]
    public void NestedNonPassingOrUnexecutedResultRejectsOverallPass(
        string result)
    {
        var report = WindowsAppCertificationReportReader.Read(Bytes(
            $"<REPORT OVERALL_RESULT=\"PASS\">" +
            $"<TEST NAME=\"case\" RESULT=\"{result}\" />" +
            "</REPORT>"));

        Assert.False(report.IsPassed);
        Assert.NotEmpty(report.NonPassingResults);
    }

    [Theory]
    [InlineData("<REPORT />")]
    [InlineData("<report OVERALL_RESULT=\"PASS\" />")]
    [InlineData("<!DOCTYPE REPORT [<!ENTITY x 'PASS'>]>" +
                "<REPORT OVERALL_RESULT=\"&x;\" />")]
    public void InvalidOrUnsafeReportsAreRejected(string xml)
    {
        Assert.Throws<InvalidDataException>(() =>
            WindowsAppCertificationReportReader.Read(Bytes(xml)));
    }

    private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);
}
