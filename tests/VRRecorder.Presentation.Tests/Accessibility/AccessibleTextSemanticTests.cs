using System.Globalization;
using VRRecorder.DesignSystem;

namespace VRRecorder.Presentation.Tests.Accessibility;

public sealed class AccessibleTextSemanticTests
{
    [Theory]
    [InlineData("Product version: {0}", "0.3.7", "Product version: 0.3.7")]
    [InlineData("Legal Bundle ID: {0}", "vr-recorder-0.3.7", "Legal Bundle ID: vr-recorder-0.3.7")]
    [InlineData("製品バージョン: {0}", "0.3.7", "製品バージョン: 0.3.7")]
    public void LocalizedValueIsIdenticalForSightedAndAutomationConsumers(
        string localizedFormat,
        string actualValue,
        string expected)
    {
        var semantic = AccessibleText.FromLocalizedFormat(
            CultureInfo.InvariantCulture,
            localizedFormat,
            actualValue);

        Assert.Equal(expected, semantic.VisibleText);
        Assert.Equal(expected, semantic.AutomationName);
    }

    [Fact]
    public void ComponentDetailPreservesLicenseCopyrightAndSourceForAutomation()
    {
        var detail = string.Join(
            Environment.NewLine,
            "License: MIT",
            "Copyright: Copyright (c) Example Authors",
            "Source: SOURCES/example-1.2.3.zip");

        var semantic = AccessibleText.FromVisibleText(detail);

        Assert.Equal(detail, semantic.VisibleText);
        Assert.Equal(detail, semantic.AutomationName);
        Assert.Contains("MIT", semantic.AutomationName);
        Assert.Contains("Copyright", semantic.AutomationName);
        Assert.Contains(
            "SOURCES/example-1.2.3.zip",
            semantic.AutomationName);
    }
}
