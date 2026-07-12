using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;
using VRRecorder.Application.Video;
using VRRecorder.Domain.Encoding;
using VRRecorder.Domain.Video;

namespace VRRecorder.Application.Tests.Video;

public sealed class CachedPromptingVideoSenderSelectionTests
{
    [Fact]
    public async Task PreviousServiceSelectionWinsWithoutPromptOrRewrite()
    {
        var store = new StubSelectionStore("sender-b");
        var prompt = new CapturingSelectionPrompt("sender-a");
        var selection = new CachedPromptingVideoSenderSelection(store, prompt);

        var selected = await selection.SelectAsync(
            "vrchat-service-42",
            [Signal("sender-a"), Signal("sender-b")],
            CancellationToken.None);

        Assert.Equal("sender-b", selected);
        Assert.Equal(["vrchat-service-42"], store.LoadedServiceIds);
        Assert.Empty(store.SavedSelections);
        Assert.Equal(0, prompt.CallCount);
    }

    private static StableVideoSignal Signal(string senderId) => new(
        senderId,
        adapterLuid: 42,
        "NVIDIA test adapter",
        GpuVendor.Nvidia,
        1920,
        1080,
        VideoPixelFormat.Bgra8,
        estimatedSourceFramesPerSecond: 60);

    private sealed class StubSelectionStore(string? cachedSenderId)
        : IVideoSenderSelectionStore
    {
        public List<string> LoadedServiceIds { get; } = [];

        public List<(string ServiceId, string SenderId)> SavedSelections { get; } =
            [];

        public Task<string?> LoadAsync(
            string vrChatServiceId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LoadedServiceIds.Add(vrChatServiceId);
            return Task.FromResult(cachedSenderId);
        }

        public Task SaveAsync(
            string vrChatServiceId,
            string senderId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SavedSelections.Add((vrChatServiceId, senderId));
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingSelectionPrompt(string? selectedSenderId)
        : IVideoSenderSelectionPrompt
    {
        public int CallCount { get; private set; }

        public Task<string?> SelectAsync(
            IReadOnlyList<StableVideoSignal> candidates,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(selectedSenderId);
        }
    }
}
