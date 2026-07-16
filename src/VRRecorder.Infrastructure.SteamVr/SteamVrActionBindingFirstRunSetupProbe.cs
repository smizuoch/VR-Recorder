using VRRecorder.Application.Ports;
using VRRecorder.Application.Setup;
using VRRecorder.DesignSystem;

namespace VRRecorder.Infrastructure.SteamVr;

public sealed class SteamVrActionBindingFirstRunSetupProbe
    : IFirstRunSetupProbe
{
    private static readonly string[] RequiredActionPaths =
    [
        RecordingInputContract.SteamVrToggleActionPath,
        RecordingInputContract.SteamVrToggleMicrophoneActionPath,
        WristOverlayInputContract.SteamVrRecenterActionPath,
    ];
    private readonly Func<ISteamVrInputRuntime> _createRuntime;
    private readonly TimeSpan _timeout;

    public SteamVrActionBindingFirstRunSetupProbe(
        Func<ISteamVrInputRuntime> createRuntime)
        : this(createRuntime, TimeSpan.FromSeconds(2))
    {
    }

    public SteamVrActionBindingFirstRunSetupProbe(
        Func<ISteamVrInputRuntime> createRuntime,
        TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(createRuntime);
        if (timeout <= TimeSpan.Zero || timeout == Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        _createRuntime = createRuntime;
        _timeout = timeout;
    }

    public async Task<bool> VerifyAsync(
        FirstRunSetupStep setupStep,
        CancellationToken cancellationToken)
    {
        if (setupStep != FirstRunSetupStep.SteamVrActionBinding)
        {
            return false;
        }

        var runtime = _createRuntime() ?? throw new InvalidOperationException(
            "The SteamVR input runtime factory returned null.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(_timeout);
        try
        {
            foreach (var actionPath in RequiredActionPaths)
            {
                var state = await ReadFirstAsync(
                        runtime,
                        actionPath,
                        timeout.Token)
                    .ConfigureAwait(false);
                if (state is null || !state.IsActive)
                {
                    return false;
                }
            }

            return true;
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested &&
            timeout.IsCancellationRequested)
        {
            return false;
        }
    }

    private static async Task<SteamVrDigitalActionState?> ReadFirstAsync(
        ISteamVrInputRuntime runtime,
        string actionPath,
        CancellationToken cancellationToken)
    {
        await foreach (var state in runtime
                           .ObserveDigitalActionAsync(
                               actionPath,
                               cancellationToken)
                           .WithCancellation(cancellationToken)
                           .ConfigureAwait(false))
        {
            return state;
        }

        return null;
    }
}
