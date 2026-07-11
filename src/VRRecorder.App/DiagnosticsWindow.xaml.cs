using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using VRRecorder.Application.Desktop;
using WpfSaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace VRRecorder.App;

public partial class DiagnosticsWindow : Window, IDisposable
{
    private readonly DesktopDiagnosticsController _controller;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _exporting;
    private int _disposeStarted;

    public DiagnosticsWindow()
        : this(App.DiagnosticsController)
    {
    }

    internal DiagnosticsWindow(DesktopDiagnosticsController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        _controller = controller;
        InitializeComponent();
    }

    private async void OnExport(object sender, RoutedEventArgs e)
    {
        if (_exporting)
        {
            return;
        }

        var dialog = new WpfSaveFileDialog
        {
            AddExtension = true,
            CheckPathExists = true,
            DefaultExt = ".zip",
            FileName = Resource("Diagnostics_Export_DefaultFileName"),
            Filter = Resource("Diagnostics_Export_Filter"),
            OverwritePrompt = true,
            Title = Resource("Diagnostics_Export_DialogTitle"),
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _exporting = true;
        ExportDiagnosticsButton.IsEnabled = false;
        ApplyResourceStatus("Diagnostics_Exporting");
        try
        {
            await _controller.ExportAsync(
                dialog.FileName,
                _lifetime.Token);
            var bundlePath = _controller.State.LastExport?.BundlePath ??
                             throw new InvalidOperationException(
                                 "A completed diagnostic export has no bundle path.");
            ApplyTextStatus(string.Format(
                CultureInfo.CurrentCulture,
                Resource("Diagnostics_Export_Success_Format"),
                bundlePath));
        }
        catch (OperationCanceledException) when (
            _lifetime.IsCancellationRequested)
        {
            // Closing this window cancels its active export.
        }
        catch (Exception)
        {
            ApplyResourceStatus("Diagnostics_Export_Failure");
        }
        finally
        {
            _exporting = false;
            if (!_lifetime.IsCancellationRequested)
            {
                ExportDiagnosticsButton.IsEnabled = true;
            }
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnClosed(object? sender, EventArgs e) => Dispose();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            return;
        }

        _lifetime.Cancel();
        _lifetime.Dispose();
        GC.SuppressFinalize(this);
    }

    private void ApplyResourceStatus(string resourceKey)
    {
        DiagnosticsStatusText.SetResourceReference(
            TextBlock.TextProperty,
            resourceKey);
        DiagnosticsStatusText.SetResourceReference(
            AutomationProperties.NameProperty,
            resourceKey);
        DiagnosticsStatusText.Visibility = Visibility.Visible;
    }

    private void ApplyTextStatus(string text)
    {
        DiagnosticsStatusText.Text = text;
        AutomationProperties.SetName(DiagnosticsStatusText, text);
        DiagnosticsStatusText.Visibility = Visibility.Visible;
    }

    private string Resource(string key) =>
        FindResource(key) as string ?? throw new InvalidOperationException(
            $"The diagnostics resource {key} is missing.");
}
