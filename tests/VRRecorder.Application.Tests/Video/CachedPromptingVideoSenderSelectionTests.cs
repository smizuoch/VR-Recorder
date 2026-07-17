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

    [Fact]
    public async Task CancellationAfterCacheReadNeverSelectsAStaleResult()
    {
        using var cancellation = new CancellationTokenSource();
        var store = new CancelingSelectionStore(cancellation, "sender-b");
        var prompt = new CapturingSelectionPrompt("sender-a");
        var selection = new CachedPromptingVideoSenderSelection(store, prompt);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            selection.SelectAsync(
                "vrchat-service-42",
                [Signal("sender-a"), Signal("sender-b")],
                cancellation.Token));

        Assert.Equal(0, prompt.CallCount);
    }

    [Fact]
    public void ConstructorRejectsMissingSelectionDependencies()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new CachedPromptingVideoSenderSelection(
                null!,
                new CapturingSelectionPrompt(null)));
        Assert.Throws<ArgumentNullException>(() =>
            new CachedPromptingVideoSenderSelection(
                new StubSelectionStore(null),
                null!));
    }

    [Fact]
    public async Task InvalidCandidateSetsFailBeforeReadingCache()
    {
        var store = new StubSelectionStore(null);
        var selection = new CachedPromptingVideoSenderSelection(
            store,
            new CapturingSelectionPrompt(null));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            selection.SelectAsync(" ", [Signal("sender")], CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            selection.SelectAsync("service", null!, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            selection.SelectAsync("service", [], CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            selection.SelectAsync(
                "service",
                [Signal("sender"), null!],
                CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            selection.SelectAsync(
                "service",
                [Signal("sender"), Signal("sender")],
                CancellationToken.None));

        Assert.Empty(store.LoadedServiceIds);
    }

    [Fact]
    public async Task SingleCandidateIsCachedWithoutPrompting()
    {
        var store = new StubSelectionStore("stale-sender");
        var prompt = new CapturingSelectionPrompt(null);
        var selection = new CachedPromptingVideoSenderSelection(store, prompt);

        var selected = await selection.SelectAsync(
            "service",
            [Signal("only-sender")],
            CancellationToken.None);

        Assert.Equal("only-sender", selected);
        Assert.Equal([("service", "only-sender")], store.SavedSelections);
        Assert.Equal(0, prompt.CallCount);
    }

    [Fact]
    public async Task PromptSelectionMustBeOfferedAndIsPersistedInSortedOrder()
    {
        var canceledPrompt = new CapturingSelectionPrompt(null);
        var canceledStore = new StubSelectionStore(null);
        var canceled = new CachedPromptingVideoSenderSelection(
            canceledStore,
            canceledPrompt);
        Assert.Null(await canceled.SelectAsync(
            "service",
            [Signal("sender-b"), Signal("sender-a")],
            CancellationToken.None));
        Assert.Empty(canceledStore.SavedSelections);
        Assert.Equal(["sender-a", "sender-b"], canceledPrompt.LastSenderIds);

        var invalid = new CachedPromptingVideoSenderSelection(
            new StubSelectionStore(null),
            new CapturingSelectionPrompt("not-offered"));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            invalid.SelectAsync(
                "service",
                [Signal("sender-b"), Signal("sender-a")],
                CancellationToken.None));

        var selectedStore = new StubSelectionStore(null);
        var selected = new CachedPromptingVideoSenderSelection(
            selectedStore,
            new CapturingSelectionPrompt("sender-b"));
        Assert.Equal("sender-b", await selected.SelectAsync(
            "service",
            [Signal("sender-b"), Signal("sender-a")],
            CancellationToken.None));
        Assert.Equal([("service", "sender-b")], selectedStore.SavedSelections);
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

        public IReadOnlyList<string> LastSenderIds { get; private set; } = [];

        public Task<string?> SelectAsync(
            IReadOnlyList<StableVideoSignal> candidates,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            LastSenderIds = candidates.Select(candidate => candidate.SenderId)
                .ToArray();
            return Task.FromResult(selectedSenderId);
        }
    }

    private sealed class CancelingSelectionStore(
        CancellationTokenSource cancellation,
        string cachedSenderId) : IVideoSenderSelectionStore
    {
        public Task<string?> LoadAsync(
            string vrChatServiceId,
            CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            return Task.FromResult<string?>(cachedSenderId);
        }

        public Task SaveAsync(
            string vrChatServiceId,
            string senderId,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Save was not expected.");
    }
}
