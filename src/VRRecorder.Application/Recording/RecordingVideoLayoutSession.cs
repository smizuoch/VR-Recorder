using VRRecorder.Application.Settings;
using VRRecorder.Domain.Video;

namespace VRRecorder.Application.Recording;

public sealed class RecordingVideoLayoutSession
{
    private readonly object _gate = new();
    private RecordingVideoLayout _currentLayout;

    private RecordingVideoLayoutSession(
        ResolutionChangePolicy policy,
        RecordingVideoLayout initialLayout)
    {
        Policy = policy;
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
            VideoContainCalculator.Calculate(source, canvas));
        return new RecordingVideoLayoutSession(
            policy,
            initialLayout);
    }

    public static RecordingVideoLayoutSession StartExactSegment(
        StableVideoSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        if ((signal.Width & 1) != 0 || (signal.Height & 1) != 0)
        {
            throw new ArgumentException(
                "Exact-follow H.264 segments require even source dimensions.",
                nameof(signal));
        }

        var source = SourceGeometry(signal);
        var canvas = new VideoGeometry(
            source.Width,
            source.Height,
            VideoPixelFormat.Nv12);
        return new RecordingVideoLayoutSession(
            ResolutionChangePolicy.ExactFollowSegments,
            Layout(
                source,
                canvas,
                new VideoPlacement(0, 0, source.Width, source.Height)));
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
        var placement = VideoContainCalculator.Calculate(source, OutputCanvas);
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
        new(signal.Width, signal.Height, signal.PixelFormat);
}
