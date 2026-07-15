using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using VRRecorder.DesignSystem;
using VRRecorder.Infrastructure.SteamVr.Native;

namespace VRRecorder.Infrastructure.SteamVr;

public sealed class NativeSteamVrInputRuntime : ISteamVrInputRuntime
{
    private static readonly TimeSpan MinimumPollInterval = TimeSpan.FromTicks(
        (TimeSpan.TicksPerSecond + 89) / 90);
    private readonly string _libraryPath;
    private readonly string _manifestPath;
    private readonly TimeSpan _pollInterval;

    public NativeSteamVrInputRuntime(
        string libraryPath,
        string installRoot)
        : this(libraryPath, installRoot, MinimumPollInterval)
    {
    }

    public NativeSteamVrInputRuntime(
        string libraryPath,
        string installRoot,
        TimeSpan pollInterval)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryPath);
        if (!Path.IsPathFullyQualified(libraryPath))
        {
            throw new ArgumentException(
                "The native library path must be absolute.",
                nameof(libraryPath));
        }

        if (pollInterval < MinimumPollInterval ||
            pollInterval == Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pollInterval),
                pollInterval,
                "SteamVR input polling cannot exceed 90 Hz.");
        }

        _libraryPath = Path.GetFullPath(libraryPath);
        _manifestPath = OpenVrApplicationManifest.ResolveAndValidate(
            installRoot).ActionManifestPath;
        _pollInterval = pollInterval;
    }

    public async IAsyncEnumerable<SteamVrDigitalActionState>
        ObserveDigitalActionAsync(
            string actionPath,
            [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionPath);
        cancellationToken.ThrowIfCancellationRequested();
        using var library = new NativeSteamVrInputLibrary(_libraryPath);
        using var input = CreateInput(library, actionPath);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = new NativeSteamVrDigitalStateV1
            {
                StructSize = checked((uint)Marshal.SizeOf<
                    NativeSteamVrDigitalStateV1>()),
                AbiVersion = NativeSteamVrInputLibrary.SupportedAbiVersion,
            };
            var status = library.PollInput(
                input.DangerousGetHandle(),
                ref state);
            ThrowIfFailed(status, "poll");
            yield return new SteamVrDigitalActionState(
                state.IsActive != 0,
                state.State != 0,
                state.Changed != 0);
            await Task.Delay(_pollInterval, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private NativeSteamVrInputSafeHandle CreateInput(
        NativeSteamVrInputLibrary library,
        string actionPath)
    {
        var manifestPath = Marshal.StringToCoTaskMemUTF8(_manifestPath);
        var actionSetPath = Marshal.StringToCoTaskMemUTF8(
            RecordingInputContract.SteamVrActionSetPath);
        var digitalActionPath = Marshal.StringToCoTaskMemUTF8(actionPath);
        try
        {
            var config = new NativeSteamVrInputConfigV1
            {
                StructSize = checked((uint)Marshal.SizeOf<
                    NativeSteamVrInputConfigV1>()),
                AbiVersion = NativeSteamVrInputLibrary.SupportedAbiVersion,
                ActionManifestPathUtf8 = manifestPath,
                ActionSetPathUtf8 = actionSetPath,
                DigitalActionPathUtf8 = digitalActionPath,
            };
            var status = library.CreateInput(ref config, out var input);
            ThrowIfFailed(status, "create");
            return new NativeSteamVrInputSafeHandle(input, library);
        }
        finally
        {
            Marshal.FreeCoTaskMem(digitalActionPath);
            Marshal.FreeCoTaskMem(actionSetPath);
            Marshal.FreeCoTaskMem(manifestPath);
        }
    }

    private static void ThrowIfFailed(
        NativeSteamVrStatus status,
        string operation)
    {
        if (status == NativeSteamVrStatus.Ok)
        {
            return;
        }

        if (status == NativeSteamVrStatus.BackendUnavailable)
        {
            throw new SteamVrUnavailableException((int)status, operation);
        }

        throw new SteamVrInputException((int)status, operation);
    }
}
