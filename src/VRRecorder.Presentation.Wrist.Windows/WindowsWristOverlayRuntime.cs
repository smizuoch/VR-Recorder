using VRRecorder.Application.Ports;
using VRRecorder.Application.Presentation;
using VRRecorder.DesignSystem;

namespace VRRecorder.Presentation.Wrist.Windows;

public sealed class WindowsWristOverlayRuntime
{
    private readonly WristOverlayBackgroundHost _background;

    public WindowsWristOverlayRuntime(
        IRecorderStatusSource statuses,
        IUiCommandDispatcher commands,
        IWristTexturePublisher texturePublisher,
        IWristPointerEventSource pointerEvents,
        IUiLocalizer localizer,
        WristLayoutOptions layoutOptions,
        IMonotonicClock clock)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(texturePublisher);
        ArgumentNullException.ThrowIfNull(pointerEvents);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(layoutOptions);
        ArgumentNullException.ThrowIfNull(clock);

        var textures = new WristTextureUpdateHost(
            new WristTextureRenderer(
                new WindowsWristRasterAssetProvider(),
                WindowsWristTextureThemes.Default),
            layoutOptions,
            texturePublisher);
        var interaction = new WristOverlayInteractionHost(
            textures,
            layoutOptions,
            new WristInputAdapter(commands),
            pointerEvents);
        _background = new WristOverlayBackgroundHost(
            statuses,
            new WristUiProjector(localizer),
            interaction,
            clock);
    }

    public Task RunAsync(CancellationToken cancellationToken) =>
        _background.RunAsync(cancellationToken);
}
