using VRRecorder.Application.Audio;

namespace VRRecorder.Application.Tests.Audio;

public sealed class AudioEndpointOptionTests
{
    private const int MaximumTextLength = 4096;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RejectsWhitespaceInEachTextFieldIndependently(bool invalidId)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new AudioEndpointOption(
                invalidId ? "   " : "endpoint-id",
                invalidId ? "Endpoint name" : "\t"));

        Assert.Equal(invalidId ? "id" : "displayName", exception.ParamName);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RejectsOneControlCharacterInEachTextFieldIndependently(
        bool invalidId)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new AudioEndpointOption(
                invalidId ? "endpoint\nid" : "endpoint-id",
                invalidId ? "Endpoint name" : "Endpoint\nname"));

        Assert.Equal(invalidId ? "id" : "displayName", exception.ParamName);
    }

    [Fact]
    public void AcceptsBothTextFieldsAtTheMaximumLength()
    {
        var id = new string('i', MaximumTextLength);
        var displayName = new string('n', MaximumTextLength);

        var option = new AudioEndpointOption(id, displayName);

        Assert.Same(id, option.Id);
        Assert.Same(displayName, option.DisplayName);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RejectsEachTextFieldAboveTheMaximumLength(bool invalidId)
    {
        var tooLong = new string('x', MaximumTextLength + 1);

        var exception = Assert.Throws<ArgumentException>(() =>
            new AudioEndpointOption(
                invalidId ? tooLong : "endpoint-id",
                invalidId ? "Endpoint name" : tooLong));

        Assert.Equal(invalidId ? "id" : "displayName", exception.ParamName);
    }
}
