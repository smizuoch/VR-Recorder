using System.Text;
using VRRecorder.Compliance.Distribution;

namespace VRRecorder.Compliance.Tests.Distribution;

public sealed class WindowsAppCertificationWaiverReaderTests
{
    [Fact]
    public void IndependentWaiverRequiresPassingPartnerCenterFlight()
    {
        var waiver = WindowsAppCertificationWaiverReader.Read(Bytes(
            requestedBy: "release-owner",
            approvedBy: "independent-reviewer",
            certificationStatus: "passed",
            validationStatus: "passed"));

        Assert.Equal("flight-42", waiver.FlightSubmissionId);
    }

    [Theory]
    [InlineData("same", "same", "passed", "passed")]
    [InlineData("owner", "reviewer", "failed", "passed")]
    [InlineData("owner", "reviewer", "passed", "failed")]
    public void SelfApprovalOrFailedFlightIsRejected(
        string requestedBy,
        string approvedBy,
        string certificationStatus,
        string validationStatus)
    {
        Assert.Throws<InvalidDataException>(() =>
            WindowsAppCertificationWaiverReader.Read(Bytes(
                requestedBy,
                approvedBy,
                certificationStatus,
                validationStatus)));
    }

    private static byte[] Bytes(
        string requestedBy,
        string approvedBy,
        string certificationStatus,
        string validationStatus) => Encoding.UTF8.GetBytes("""
            {
              "schemaVersion": 1,
              "evidenceKind": "wack-tool-unavailable-waiver-v1",
              "packageSha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
              "toolVersion": "10.0.26100.0",
              "reason": "The installed WACK does not support this package type.",
              "requestedBy": "{{REQUESTED}}",
              "approvedBy": "{{APPROVED}}",
              "flightSubmissionId": "flight-42",
              "flightCertificationStatus": "{{CERTIFICATION}}",
              "flightValidationStatus": "{{VALIDATION}}",
              "capturedAtUtc": "2026-07-20T00:00:00Z"
            }
            """
            .Replace("{{REQUESTED}}", requestedBy, StringComparison.Ordinal)
            .Replace("{{APPROVED}}", approvedBy, StringComparison.Ordinal)
            .Replace("{{CERTIFICATION}}", certificationStatus,
                StringComparison.Ordinal)
            .Replace("{{VALIDATION}}", validationStatus,
                StringComparison.Ordinal));
}
