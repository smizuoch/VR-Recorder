using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using VRRecorder.Application.Compliance;
using VRRecorder.Application.Desktop;

namespace VRRecorder.App;

public partial class LegalWindow : Window
{
    private readonly DesktopLegalController _controller;
    private bool _applyingState;
    private bool _loaded;

    internal LegalWindow(DesktopLegalController controller)
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
        await _controller.OpenAsync(CancellationToken.None);
        ApplyState();
    }

    private async void OnComponentSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_applyingState ||
            LegalComponentList.SelectedItem is not LegalListItem selected)
        {
            return;
        }

        await _controller.ShowDetailAsync(
            selected.Component.Id,
            CancellationToken.None);
        ApplyState();
    }

    private async void OnShowLicenseClick(
        object sender,
        RoutedEventArgs e)
    {
        await _controller.ShowLicenseAsync(CancellationToken.None);
        ApplyState();
    }

    private async void OnOpenLicenseFolderClick(
        object sender,
        RoutedEventArgs e)
    {
        await _controller.OpenLicenseFolderAsync(CancellationToken.None);
        ApplyState();
    }

    private async void OnRefreshLegalClick(
        object sender,
        RoutedEventArgs e)
    {
        await _controller.RefreshAsync(CancellationToken.None);
        ApplyState();
    }

    private void OnCloseLegalClick(object sender, RoutedEventArgs e) => Close();

    private void ApplyState()
    {
        var state = _controller.State;
        _applyingState = true;
        try
        {
            ProductVersionText.Text = state.ProductVersion is null
                ? Format("Legal_ProductVersion_Format", string.Empty)
                : Format("Legal_ProductVersion_Format", state.ProductVersion);
            BundleIdentityText.Text = state.BundleId is null
                ? Format("Legal_BundleIdentity_Format", string.Empty)
                : Format("Legal_BundleIdentity_Format", state.BundleId);
            var items = state.Components
                .OrderBy(item => item.Id, StringComparer.Ordinal)
                .Select(item => new LegalListItem(
                    item,
                    $"{item.DisplayName} — {item.Version} — {item.LicenseExpression}"))
                .ToArray();
            LegalComponentList.ItemsSource = items;
            LegalComponentList.SelectedItem = state.SelectedComponent is null
                ? null
                : items.SingleOrDefault(item => string.Equals(
                    item.Component.Id,
                    state.SelectedComponent.Id,
                    StringComparison.Ordinal));
            ComponentDetailText.Text = state.SelectedComponent is null
                ? string.Empty
                : FormatDetail(state.SelectedComponent);
            FullLicenseText.Text = state.FullLicenseText ?? string.Empty;
            var available = state.View != DesktopLegalView.Unavailable;
            LegalComponentList.IsEnabled = available;
            ShowLicenseButton.IsEnabled =
                available && state.SelectedComponent is not null;
            OpenLicenseFolderButton.IsEnabled = available;
            LegalUnavailableText.Visibility = available
                ? Visibility.Collapsed
                : Visibility.Visible;
            LegalUnavailableText.SetResourceReference(
                TextBlock.TextProperty,
                "Legal_State_ComplianceFault");
            LegalUnavailableText.SetResourceReference(
                AutomationProperties.NameProperty,
                "Legal_State_ComplianceFault");
        }
        finally
        {
            _applyingState = false;
        }
    }

    private string FormatDetail(LegalCatalogComponent component) =>
        string.Join(
            Environment.NewLine,
            Format("Legal_Detail_Name_Format", component.DisplayName),
            Format("Legal_Detail_Version_Format", component.Version),
            Format("Legal_Detail_License_Format", component.LicenseExpression),
            Format("Legal_Detail_Usage_Format", component.Usage),
            Format("Legal_Detail_Linkage_Format", component.Linkage),
            Format(
                "Legal_Detail_Modified_Format",
                Resolve(component.Modified
                    ? "Legal_Modified_Yes"
                    : "Legal_Modified_No")),
            Format("Legal_Detail_Source_Format", component.SourceInformation));

    private string Format(string resourceKey, object value) =>
        string.Format(
            CultureInfo.CurrentCulture,
            Resolve(resourceKey),
            value);

    private string Resolve(string resourceKey) =>
        TryFindResource(resourceKey) as string ??
        throw new InvalidOperationException(
            $"The localized desktop resource is missing: {resourceKey}");

    private sealed record LegalListItem(
        LegalCatalogComponent Component,
        string Label);
}
