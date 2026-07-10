namespace VRRecorder.Compliance.Runtime;

public sealed class AuthenticatedLegalBundleAnchorUnavailableException
    : Exception
{
    public AuthenticatedLegalBundleAnchorUnavailableException(string message)
        : base(message)
    {
    }

    public AuthenticatedLegalBundleAnchorUnavailableException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}
