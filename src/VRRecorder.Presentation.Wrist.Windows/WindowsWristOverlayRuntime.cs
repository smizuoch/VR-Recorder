using VRRecorder.Application.Ports;
using VRRecorder.Application.Presentation;
using VRRecorder.Application.Settings;
using VRRecorder.DesignSystem;

namespace VRRecorder.Presentation.Wrist.Windows;

public sealed class WindowsWristOverlayRuntime
{
    private readonly WristOverlayBackgroundHost _background;

    public WindowsWristOverlayRuntime(
        IRecorderStatusSource statuses,
        IUiCommandDispatcher commands,
        IWristOverlayAdjustmentCommands placementCommands,
        IWristTexturePublisher texturePublisher,
        IWristPointerEventSource pointerEvents,
        IUiLocalizer localizer,
        WristLayoutOptions layoutOptions,
        IMonotonicClock clock)
        : this(
            statuses,
            commands,
            placementCommands,
            texturePublisher,
            pointerEvents,
            localizer,
            layoutOptions,
            clock,
            NullWristTelemetrySource.Instance,
            OverlayPlacementMode.WristDock)
    {
    }

    public WindowsWristOverlayRuntime(
        IRecorderStatusSource statuses,
        IUiCommandDispatcher commands,
        IWristOverlayAdjustmentCommands placementCommands,
        IWristTexturePublisher texturePublisher,
        IWristPointerEventSource pointerEvents,
        IUiLocalizer localizer,
        WristLayoutOptions layoutOptions,
        IMonotonicClock clock,
        IWristTelemetrySource telemetry,
        OverlayPlacementMode initialPlacementMode)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(placementCommands);
        ArgumentNullException.ThrowIfNull(texturePublisher);
        ArgumentNullException.ThrowIfNull(pointerEvents);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(layoutOptions);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(telemetry);
        if (!Enum.IsDefined(initialPlacementMode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(initialPlacementMode));
        }

        var textures = new WristTextureUpdateHost(
            new WristTextureRenderer(
                new WindowsWristRasterAssetProvider(),
                WindowsWristTextureThemes.Default),
            layoutOptions,
            texturePublisher);
        var session = new WristUiSession(
            localizer,
            commands,
            placementCommands,
            telemetry,
            initialPlacementMode);
        var interaction = new WristOverlayInteractionHost(
            textures,
            layoutOptions,
            new WristInputAdapter(session),
            session,
            pointerEvents);
        _background = new WristOverlayBackgroundHost(
            statuses,
            session,
            interaction,
            clock);
    }

    public Task RunAsync(CancellationToken cancellationToken) =>
        _background.RunAsync(cancellationToken);
}
