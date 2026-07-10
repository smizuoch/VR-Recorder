using VRRecorder.Application.Settings;
using VRRecorder.Domain.Video;

namespace VRRecorder.Application.Recording;

public sealed class RecordingVideoLayoutSession
{
    private readonly object _gate = new();
    private readonly VideoGeometry _initialSource;
    private RecordingVideoLayout _currentLayout;

    private RecordingVideoLayoutSession(
        ResolutionChangePolicy policy,
        VideoGeometry initialSource,
        RecordingVideoLayout initialLayout)
    {
        Policy = policy;
        _initialSource = initialSource;
        _currentLayout = initialLayout;
    }

    public ResolutionChangePolicy Policy { get; }

    public VideoGeometry OutputCanvas => _currentLayout.OutputCanvas;

    public RecordingVideoLayout CurrentLayout
    {
        get
        {
            lock (_gate)
            {
                return _currentLayout;
            }
        }
    }

    public static RecordingVideoLayoutSession Start(
        StableVideoSignal initialSignal,
        ResolutionChangePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(initialSignal);
        if (policy != ResolutionChangePolicy.SingleFileFit)
        {
            throw new UnsupportedResolutionChangePolicyException(policy);
        }

        var source = SourceGeometry(initialSignal);
        var canvas = new VideoGeometry(
                source.Width,
                source.Height,
                VideoPixelFormat.Nv12)
            .PadForChroma420();
        var initialLayout = Layout(
            source,
            canvas,
            new VideoPlacement(0, 0, source.Width, source.Height));
        return new RecordingVideoLayoutSession(
            policy,
            source,
            initialLayout);
    }

    public RecordingVideoLayout ApplyStableSignal(StableVideoSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        if (Policy != ResolutionChangePolicy.SingleFileFit)
        {
            throw new InvalidOperationException(
                $"Video layout updates are not implemented for {Policy}.");
        }

        var source = SourceGeometry(signal);
        var placement = source.Width == _initialSource.Width &&
                        source.Height == _initialSource.Height
            ? new VideoPlacement(0, 0, source.Width, source.Height)
            : VideoContainCalculator.Calculate(source, OutputCanvas);
        var layout = Layout(source, OutputCanvas, placement);
        lock (_gate)
        {
            _currentLayout = layout;
            return _currentLayout;
        }
    }

    private static RecordingVideoLayout Layout(
        VideoGeometry source,
        VideoGeometry canvas,
        VideoPlacement placement) =>
        new(
            source,
            canvas,
            placement,
            VideoCanvasBackground.Black,
            VideoRotation.None);

    private static VideoGeometry SourceGeometry(StableVideoSignal signal) =>
        new(signal.Width, signal.Height, VideoPixelFormat.Bgra8);
}
