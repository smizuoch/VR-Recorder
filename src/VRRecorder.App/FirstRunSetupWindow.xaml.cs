using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using VRRecorder.Application.Setup;

namespace VRRecorder.App;

public partial class FirstRunSetupWindow : Window
{
    private readonly FirstRunSetupUiController _controller;
    private readonly FirstRunSetupVerificationController _verification;
    private bool _loaded;

    public FirstRunSetupWindow()
        : this(App.FirstRunSetupUi, App.FirstRunSetupVerification)
    {
    }

    internal FirstRunSetupWindow(
        FirstRunSetupUiController controller,
        FirstRunSetupVerificationController verification)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(verification);
        _controller = controller;
        _verification = verification;
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
        Apply(view);
    }

    private void Apply(FirstRunSetupUiSnapshot view)
    {
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

    private async void OnVerify(object sender, RoutedEventArgs e)
    {
        VerifySetupButton.IsEnabled = false;
        ApplyStatus("Setup_Verifying");
        try
        {
            var result = await _verification.VerifyCurrentAsync(
                CancellationToken.None);
            ApplyStatus(result.Succeeded
                ? "Setup_Verification_Succeeded"
                : "Setup_Verification_Failed");
            if (result.Succeeded)
            {
                Apply(await _controller.LoadAsync(CancellationToken.None));
            }
        }
        finally
        {
            VerifySetupButton.IsEnabled = true;
        }
    }

    private void ApplyStatus(string resourceKey)
    {
        SetupVerificationStatusText.SetResourceReference(
            TextBlock.TextProperty,
            resourceKey);
        SetupVerificationStatusText.SetResourceReference(
            AutomationProperties.NameProperty,
            resourceKey);
        SetupVerificationStatusText.Visibility = Visibility.Visible;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private string Resource(string key) =>
        FindResource(key) as string ?? throw new InvalidOperationException(
            $"The first-run setup resource {key} is missing.");
}
