using System.Globalization;
using System.Reflection;
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
        var semantic = InvokeFactory(
            "FromLocalizedFormat",
            [
                CultureInfo.InvariantCulture,
                localizedFormat,
                actualValue,
            ],
            typeof(IFormatProvider),
            typeof(string),
            typeof(object));

        Assert.Equal(expected, ReadText(semantic, "VisibleText"));
        Assert.Equal(expected, ReadText(semantic, "AutomationName"));
    }

    [Fact]
    public void ComponentDetailPreservesLicenseCopyrightAndSourceForAutomation()
    {
        var detail = string.Join(
            Environment.NewLine,
            "License: MIT",
            "Copyright: Copyright (c) Example Authors",
            "Source: SOURCES/example-1.2.3.zip");

        var semantic = InvokeFactory(
            "FromVisibleText",
            [detail],
            typeof(string));

        Assert.Equal(detail, ReadText(semantic, "VisibleText"));
        Assert.Equal(detail, ReadText(semantic, "AutomationName"));
        Assert.Contains("MIT", ReadText(semantic, "AutomationName"));
        Assert.Contains("Copyright", ReadText(semantic, "AutomationName"));
        Assert.Contains("SOURCES/example-1.2.3.zip", ReadText(
            semantic,
            "AutomationName"));
    }

    private static object InvokeFactory(
        string methodName,
        object?[] arguments,
        params Type[] parameterTypes)
    {
        var semanticType = typeof(LocalizedText).Assembly.GetType(
            "VRRecorder.DesignSystem.AccessibleText");
        Assert.NotNull(semanticType);
        var factory = semanticType.GetMethod(
            methodName,
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            parameterTypes,
            modifiers: null);
        Assert.NotNull(factory);
        var result = factory.Invoke(null, arguments);
        Assert.NotNull(result);
        Assert.Equal(semanticType, result.GetType());
        return result;
    }

    private static string ReadText(object semantic, string propertyName)
    {
        var property = semantic.GetType().GetProperty(propertyName);
        Assert.NotNull(property);
        return Assert.IsType<string>(property.GetValue(semantic));
    }
}
