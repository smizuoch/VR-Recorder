using System.Runtime.InteropServices;
using VRRecorder.Application.Haptics;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Settings;
using VRRecorder.DesignSystem;
using VRRecorder.Infrastructure.SteamVr.Native;

namespace VRRecorder.Infrastructure.SteamVr;

public sealed class NativeSteamVrHapticOutput
    : IWristHapticOutput, IDisposable
{
    private const string LeftInputSourcePath = "/user/hand/left";
    private const string RightInputSourcePath = "/user/hand/right";
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly NativeSteamVrLibrary _library;
    private readonly NativeSteamVrHapticSafeHandle _haptic;
    private bool _disposed;

    public NativeSteamVrHapticOutput(
        string libraryPath,
        string installRoot,
        VrHand hand)
    {
        var inputSourcePath = hand switch
        {
            VrHand.Left => LeftInputSourcePath,
            VrHand.Right => RightInputSourcePath,
            _ => throw new ArgumentOutOfRangeException(
                nameof(hand),
                hand,
                "The SteamVR haptic hand is not supported."),
        };
        var manifest = OpenVrApplicationManifest.ResolveAndValidate(
            installRoot);
        _library = new NativeSteamVrLibrary(libraryPath);
        try
        {
            _haptic = CreateHaptic(
                _library,
                manifest.ActionManifestPath,
                inputSourcePath);
        }
        catch
        {
            _library.Dispose();
            throw;
        }
    }

    public async Task PlayAsync(
        WristHapticPattern pattern,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        await _operationGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            for (var index = 0; index < pattern.PulseCount; ++index)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var pulse = new NativeSteamVrHapticPulseV1
                {
                    StructSize = checked((uint)Marshal.SizeOf<
                        NativeSteamVrHapticPulseV1>()),
                    AbiVersion = NativeSteamVrLibrary.SupportedAbiVersion,
                    DurationSeconds = checked((float)pattern.Duration.TotalSeconds),
                    FrequencyHertz = pattern.FrequencyHertz,
                    Amplitude = pattern.Amplitude,
                };
                var status = _library.TriggerHaptic(
                    _haptic.DangerousGetHandle(),
                    ref pulse);
                ThrowIfFailed(status, "trigger");
                if (index + 1 < pattern.PulseCount)
                {
                    await Task
                        .Delay(pattern.Duration, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public void Dispose()
    {
        _operationGate.Wait();
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _haptic.Dispose();
            _library.Dispose();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private static NativeSteamVrHapticSafeHandle CreateHaptic(
        NativeSteamVrLibrary library,
        string manifestPath,
        string inputSourcePath)
    {
        var nativeManifestPath = Marshal.StringToCoTaskMemUTF8(manifestPath);
        var nativeActionPath = Marshal.StringToCoTaskMemUTF8(
            WristOverlayInputContract.SteamVrHapticActionPath);
        var nativeInputSourcePath = Marshal.StringToCoTaskMemUTF8(
            inputSourcePath);
        try
        {
            var config = new NativeSteamVrHapticConfigV1
            {
                StructSize = checked((uint)Marshal.SizeOf<
                    NativeSteamVrHapticConfigV1>()),
                AbiVersion = NativeSteamVrLibrary.SupportedAbiVersion,
                ActionManifestPathUtf8 = nativeManifestPath,
                HapticActionPathUtf8 = nativeActionPath,
                InputSourcePathUtf8 = nativeInputSourcePath,
            };
            var status = library.CreateHaptic(ref config, out var haptic);
            ThrowIfFailed(status, "create");
            return new NativeSteamVrHapticSafeHandle(haptic, library);
        }
        finally
        {
            Marshal.FreeCoTaskMem(nativeInputSourcePath);
            Marshal.FreeCoTaskMem(nativeActionPath);
            Marshal.FreeCoTaskMem(nativeManifestPath);
        }
    }

    private static void ThrowIfFailed(
        NativeSteamVrStatus status,
        string operation)
    {
        if (status != NativeSteamVrStatus.Ok)
        {
            throw new SteamVrHapticException((int)status, operation);
        }
    }
}
