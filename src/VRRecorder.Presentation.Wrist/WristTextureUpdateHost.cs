namespace VRRecorder.Presentation.Wrist;

public interface IWristTexturePublisher
{
    void Publish(WristTextureFrame frame);

    void Show();
}

public sealed record WristTextureHostTickResult(
    bool Published,
    bool BecameVisible,
    WristTextureUpdateReason Reason);

public sealed class WristTextureUpdateHost
{
    private readonly object _gate = new();
    private readonly WristTextureRenderer _renderer;
    private readonly WristLayoutOptions _layoutOptions;
    private readonly IWristTexturePublisher _publisher;
    private WristTextureUpdateCursor? _cursor;
    private bool _visible;

    public WristTextureUpdateHost(
        WristTextureRenderer renderer,
        WristLayoutOptions layoutOptions,
        IWristTexturePublisher publisher)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(layoutOptions);
        ArgumentNullException.ThrowIfNull(publisher);
        _renderer = renderer;
        _layoutOptions = layoutOptions;
        _publisher = publisher;
    }

    public WristTextureHostTickResult Tick(
        WristUiSnapshot snapshot,
        TimeSpan now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate)
        {
            var decision = WristTextureUpdatePolicy.Evaluate(
                _cursor,
                snapshot,
                now);
            if (!decision.ShouldRender)
            {
                return new WristTextureHostTickResult(
                    Published: false,
                    BecameVisible: false,
                    decision.Reason);
            }

            var frame = _renderer.Render(snapshot, _layoutOptions);
            _publisher.Publish(frame);
            var becameVisible = false;
            if (!_visible)
            {
                _publisher.Show();
                becameVisible = true;
            }

            _cursor = decision.NextCursor;
            _visible = true;
            return new WristTextureHostTickResult(
                Published: true,
                becameVisible,
                decision.Reason);
        }
    }
}
