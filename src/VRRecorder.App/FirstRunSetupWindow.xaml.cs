using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using VRRecorder.Application.Setup;

namespace VRRecorder.App;

public partial class FirstRunSetupWindow : Window
{
    private readonly FirstRunSetupUiController _controller;
    private bool _loaded;

    public FirstRunSetupWindow()
        : this(App.FirstRunSetupUi)
    {
    }

    internal FirstRunSetupWindow(FirstRunSetupUiController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        _controller = controller;
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        var view = await _controller.LoadAsync(CancellationToken.None);
        if (!view.RequiresSetup)
        {
            DialogResult = true;
            return;
        }

        SetupTitleText.SetResourceReference(
            TextBlock.TextProperty,
            view.TitleResourceKey);
        SetupTitleText.SetResourceReference(
            AutomationProperties.NameProperty,
            view.TitleResourceKey);
        SetupBodyText.SetResourceReference(
            TextBlock.TextProperty,
            view.BodyResourceKey);
        SetupBodyText.SetResourceReference(
            AutomationProperties.NameProperty,
            view.BodyResourceKey);
        SetupProgressBar.Value = view.ProgressPercent;
        SetupProgressText.Text = string.Format(
            CultureInfo.CurrentCulture,
            Resource("Setup_Progress_Format"),
            view.StepNumber,
            view.TotalSteps);
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private string Resource(string key) =>
        FindResource(key) as string ?? throw new InvalidOperationException(
            $"The first-run setup resource {key} is missing.");
}
