namespace VRRecorder.Compliance.Runtime;

public interface IAuthenticatedLegalBundleAnchorSource
{
    ValueTask<AuthenticatedLegalBundleAnchor> GetAsync(
        CancellationToken cancellationToken);
}
