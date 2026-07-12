using System.Windows.Threading;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;

namespace VRRecorder.App;

internal sealed class WpfVideoSenderSelectionPrompt
    : IVideoSenderSelectionPrompt
{
    private readonly Dispatcher _dispatcher;

    public WpfVideoSenderSelectionPrompt()
        : this(System.Windows.Application.Current?.Dispatcher ??
               throw new InvalidOperationException(
                   "The WPF application dispatcher is unavailable."))
    {
    }

    internal WpfVideoSenderSelectionPrompt(Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _dispatcher = dispatcher;
    }

    public async Task<string?> SelectAsync(
        IReadOnlyList<StableVideoSignal> candidates,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var snapshot = candidates.ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        if (_dispatcher.CheckAccess())
        {
            return ShowDialog(snapshot, cancellationToken);
        }

        var operation = _dispatcher.InvokeAsync(
            () => ShowDialog(snapshot, cancellationToken),
            DispatcherPriority.Normal,
            cancellationToken);
        return await operation.Task.ConfigureAwait(false);
    }

    private static string? ShowDialog(
        IReadOnlyList<StableVideoSignal> candidates,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var window = new SpoutSenderSelectionWindow(candidates);
        if (System.Windows.Application.Current?.MainWindow is
            { IsVisible: true } owner)
        {
            window.Owner = owner;
        }

        using var registration = cancellationToken.Register(() =>
            _ = window.Dispatcher.InvokeAsync(() =>
            {
                if (window.IsVisible)
                {
                    window.Close();
                }
            }));
        cancellationToken.ThrowIfCancellationRequested();
        var accepted = window.ShowDialog() == true;
        cancellationToken.ThrowIfCancellationRequested();
        return accepted ? window.SelectedSenderId : null;
    }
}
