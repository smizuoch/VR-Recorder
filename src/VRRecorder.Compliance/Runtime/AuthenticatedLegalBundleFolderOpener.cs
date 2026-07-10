using VRRecorder.Application.Compliance;
using VRRecorder.Application.Ports;

namespace VRRecorder.Compliance.Runtime;

public sealed class AuthenticatedLegalBundleFolderOpener
    : ILegalBundleFolderOpener
{
    private readonly string _installDirectory;
    private readonly string _bundleDirectory;
    private readonly AuthenticatedLegalBundleVerifier _verifier;
    private readonly ILegalFolderShell _shell;

    public AuthenticatedLegalBundleFolderOpener(
        string installDirectory,
        string bundleDirectory,
        AuthenticatedLegalBundleVerifier verifier,
        ILegalFolderShell shell)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleDirectory);
        ArgumentNullException.ThrowIfNull(verifier);
        ArgumentNullException.ThrowIfNull(shell);
        _installDirectory = Normalize(installDirectory);
        _bundleDirectory = Normalize(bundleDirectory);
        _verifier = verifier;
        _shell = shell;
    }

    public async Task<LegalFolderOpenResult> OpenAsync(
        string expectedBundleId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedBundleId);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsSameOrDescendant(_installDirectory, _bundleDirectory))
        {
            return Reject(
                "legal-folder-outside-install-bundle",
                _bundleDirectory);
        }

        if (!Directory.Exists(_bundleDirectory))
        {
            return Reject("legal-folder-missing", _bundleDirectory);
        }

        if (ContainsReparsePointBetweenInstallAndBundle())
        {
            return Reject("legal-bundle-reparse-point", _bundleDirectory);
        }

        LegalBundleVerification verification;
        try
        {
            verification = await _verifier
                .VerifyAsync(_bundleDirectory, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return Reject("legal-folder-unreadable", _bundleDirectory);
        }

        if (verification is LegalBundleVerification.Rejected rejected)
        {
            return new LegalFolderOpenResult.Rejected(rejected.Issues
                .Select(issue => new LegalCatalogIssue(
                    issue.Code,
                    issue.Subject))
                .ToArray());
        }

        var identity = ((LegalBundleVerification.Verified)verification).Identity;
        if (!string.Equals(
                identity.BundleId,
                expectedBundleId,
                StringComparison.Ordinal))
        {
            return Reject("legal-folder-bundle-identity-mismatch", _bundleDirectory);
        }

        if (ContainsReparsePointBetweenInstallAndBundle())
        {
            return Reject("legal-bundle-reparse-point", _bundleDirectory);
        }

        try
        {
            await _shell
                .OpenFolderAsync(_bundleDirectory, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                System.ComponentModel.Win32Exception or
                InvalidOperationException)
        {
            return Reject("legal-folder-open-failed", _bundleDirectory);
        }

        return new LegalFolderOpenResult.Opened(_bundleDirectory);
    }

    private bool ContainsReparsePointBetweenInstallAndBundle()
    {
        var current = new DirectoryInfo(_bundleDirectory);
        while (true)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }

            if (PathsEqual(current.FullName, _installDirectory))
            {
                return false;
            }

            current = current.Parent ??
                      throw new InvalidDataException(
                          "The legal bundle escaped its install directory.");
        }
    }

    private static bool IsSameOrDescendant(string parent, string candidate)
    {
        if (PathsEqual(parent, candidate))
        {
            return true;
        }

        var prefix = Path.EndsInDirectorySeparator(parent)
            ? parent
            : parent + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, PathComparison);
    }

    private static bool PathsEqual(string first, string second) =>
        string.Equals(
            Normalize(first),
            Normalize(second),
            PathComparison);

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static LegalFolderOpenResult.Rejected Reject(
        string code,
        string subject) =>
        new([new LegalCatalogIssue(code, subject)]);
}
