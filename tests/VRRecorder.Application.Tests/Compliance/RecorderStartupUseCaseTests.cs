using VRRecorder.Application.Compliance;
using VRRecorder.Application.Ports;
using VRRecorder.Domain.Recording;

namespace VRRecorder.Application.Tests.Compliance;

public sealed class RecorderStartupUseCaseTests
{
    [Fact]
    public async Task VerifiedLegalBundleEnablesReadyState()
    {
        var useCase = new RecorderStartupUseCase(
            new StubLegalBundleVerifier(
                new LegalBundleVerification.Verified()));

        var result = await useCase.ExecuteAsync(CancellationToken.None);

        Assert.Equal(RecorderState.Ready, result.State);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public async Task RejectedLegalBundleLocksRecorderInComplianceFault()
    {
        LegalBundleIssue issue = new(
            "LEGAL_BUNDLE_HASH_MISMATCH",
            "THIRD-PARTY-COMPONENTS.json");
        var useCase = new RecorderStartupUseCase(
            new StubLegalBundleVerifier(
                new LegalBundleVerification.Rejected([issue])));

        var result = await useCase.ExecuteAsync(CancellationToken.None);

        Assert.Equal(RecorderState.ComplianceFault, result.State);
        Assert.Equal(issue, Assert.Single(result.Issues));
    }

    private sealed class StubLegalBundleVerifier(
        LegalBundleVerification result) : ILegalBundleVerifier
    {
        public Task<LegalBundleVerification> VerifyAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(result);
        }
    }
}
