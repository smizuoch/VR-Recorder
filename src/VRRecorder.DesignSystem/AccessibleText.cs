namespace VRRecorder.DesignSystem;

public sealed record AccessibleText
{
    private AccessibleText(string text)
    {
        VisibleText = text;
        AutomationName = text;
    }

    public string VisibleText { get; }

    public string AutomationName { get; }

    public static AccessibleText FromLocalizedFormat(
        IFormatProvider formatProvider,
        string localizedFormat,
        object? actualValue)
    {
        ArgumentNullException.ThrowIfNull(formatProvider);
        ArgumentNullException.ThrowIfNull(localizedFormat);
        return new AccessibleText(string.Format(
            formatProvider,
            localizedFormat,
            actualValue));
    }

    public static AccessibleText FromVisibleText(string visibleText)
    {
        ArgumentNullException.ThrowIfNull(visibleText);
        return new AccessibleText(visibleText);
    }
}
