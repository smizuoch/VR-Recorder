using VRRecorder.Presentation.Wrist;

namespace VRRecorder.Presentation.Tests.Wrist;

public sealed class UiLocalizationTests
{
    [Fact]
    public void JapaneseAndEnglishResourceKeysAreInParity()
    {
        var english = EnglishUiLocalizer.Instance;
        var japanese = JapaneseUiLocalizer.Instance;

        Assert.Equal(
            english.ResourceKeys.Order(StringComparer.Ordinal),
            japanese.ResourceKeys.Order(StringComparer.Ordinal));
        Assert.All(english.ResourceKeys, key =>
        {
            Assert.False(string.IsNullOrWhiteSpace(english.Resolve(key).Value));
            Assert.False(string.IsNullOrWhiteSpace(japanese.Resolve(key).Value));
            Assert.NotEqual(key, english.Resolve(key).Value);
            Assert.NotEqual(key, japanese.Resolve(key).Value);
        });
    }
}
