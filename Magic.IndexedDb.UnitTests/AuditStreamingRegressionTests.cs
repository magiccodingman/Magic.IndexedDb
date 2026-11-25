using Magic.IndexedDb.Extensions;
using Magic.IndexedDb.Factories;
using Microsoft.JSInterop;

namespace Magic.IndexedDb.UnitTests;

[TestClass]
public sealed class AuditStreamingRegressionTests
{
    [TestMethod]
    public async Task StorageEstimatePropagatesCancellationToTheBrowser()
    {
        using var cancellation = new CancellationTokenSource();
        var browser = new DelayedInvocationBrowser();
        await using var factory = new MagicDbFactory(new ImportingRuntime(browser), 1024, false, [], []);
        var pending = factory.GetStorageEstimateAsync(cancellation.Token);
        try
        {
            await browser.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(1)));
        }
        finally
        {
            browser.Release.TrySetCanceled();
            try { await pending; }
            catch (OperationCanceledException) { }
        }
    }

    [TestMethod]
    public async Task CancellationStopsWaitingForNonStreamingBrowserWork()
    {
        using var cancellation = new CancellationTokenSource();
        var browser = new DelayedInvocationBrowser();
        var invocation = new MagicJsInvoke(browser, 1024);
        var pending = invocation.CallJsAsync("./fixture.js", "write", cancellation.Token);
        try
        {
            await browser.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(1)));
        }
        finally
        {
            browser.Release.TrySetCanceled();
            try { await pending; }
            catch (OperationCanceledException) { }
        }
    }

    [TestMethod]
    public async Task CancellationStopsAlreadyBufferedResults()
    {
        using var cancellation = new CancellationTokenSource();
        var invocation = new MagicJsInvoke(new BufferedBrowser(), 1024);
        await using var enumerator = invocation.CallYieldJsAsync<int>(
            "./fixture.js", "rows", cancellation.Token).GetAsyncEnumerator();

        Assert.IsTrue(await enumerator.MoveNextAsync());
        Assert.AreEqual(1, enumerator.Current);
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await enumerator.MoveNextAsync());
    }

    [TestMethod]
    public void LateCallbacksCannotRecreateRemovedStreams()
    {
        var id = Guid.NewGuid().ToString("N");
        MagicJsChunkProcessor.RegisterInstance(id);
        MagicJsChunkProcessor.RemoveInstance(id);
        try
        {
            // Browser work can finish after its .NET consumer is cancelled/disposed.
            MagicJsChunkProcessor.AddChunk(id, "late", 0, "sensitive payload", 0, 1);
            MagicJsChunkProcessor.AddChunk(id, "STREAM_COMPLETE", -1, "", 0, 1);
            Assert.IsNull(MagicJsChunkProcessor.GetCompletedItem(id));
        }
        finally
        {
            MagicJsChunkProcessor.RemoveInstance(id);
        }
    }

    [TestMethod]
    public void CompletionDoesNotHideAnIncompleteRecord()
    {
        var id = Guid.NewGuid().ToString("N");
        MagicJsChunkProcessor.RegisterInstance(id);
        try
        {
            MagicJsChunkProcessor.AddChunk(id, "row", 0, "first-", 0, 2);
            MagicJsChunkProcessor.AddChunk(id, "STREAM_COMPLETE", -1, "", 0, 1);
            Assert.IsNull(MagicJsChunkProcessor.GetCompletedItem(id),
                "An incomplete record must not turn into a successful partial stream.");
            MagicJsChunkProcessor.AddChunk(id, "row", 0, "second", 1, 2);
            Assert.AreEqual("first-second", MagicJsChunkProcessor.GetCompletedItem(id));
            Assert.AreEqual("STREAM_COMPLETE", MagicJsChunkProcessor.GetCompletedItem(id));
        }
        finally
        {
            MagicJsChunkProcessor.RemoveInstance(id);
        }
    }

    [TestMethod]
    public async Task ProducerFailureDuringDisposalStillRemovesTheStream()
    {
        var browser = new FailingCancellationBrowser();
        var invocation = new MagicJsInvoke(browser, 1024);
        var enumerator = invocation.CallYieldJsAsync<int>("./fixture.js", "rows", default).GetAsyncEnumerator();
        Assert.IsTrue(await enumerator.MoveNextAsync());

        await Assert.ThrowsAsync<JSException>(async () => await enumerator.DisposeAsync());
        MagicJsChunkProcessor.AddChunk(browser.InstanceId!, "late", 1, "2", 0, 1);
        Assert.IsNull(MagicJsChunkProcessor.GetCompletedItem(browser.InstanceId!));
    }

    [TestMethod]
    public async Task SynchronousInteropFailureStillRemovesTheStream()
    {
        var browser = new ThrowingBrowser();
        var invocation = new MagicJsInvoke(browser, 1024);
        await using var enumerator = invocation.CallYieldJsAsync<int>("./fixture.js", "rows", default).GetAsyncEnumerator();
        await Assert.ThrowsAsync<JSException>(async () => await enumerator.MoveNextAsync());
        MagicJsChunkProcessor.AddChunk(browser.InstanceId!, "late", 0, "1", 0, 1);
        Assert.IsNull(MagicJsChunkProcessor.GetCompletedItem(browser.InstanceId!));
    }

    [TestMethod]
    public void ConcurrentFragmentsAreReassembledExactlyOnce()
    {
        var id = Guid.NewGuid().ToString("N");
        MagicJsChunkProcessor.RegisterInstance(id);
        try
        {
            Parallel.For(0, 128, index =>
                MagicJsChunkProcessor.AddChunk(id, "record", 0, $"{index},", index, 128));
            Assert.AreEqual(string.Concat(Enumerable.Range(0, 128).Select(index => $"{index},")),
                MagicJsChunkProcessor.GetCompletedItem(id));
            Assert.IsNull(MagicJsChunkProcessor.GetCompletedItem(id));
        }
        finally
        {
            MagicJsChunkProcessor.RemoveInstance(id);
        }
    }

    private sealed class DelayedInvocationBrowser : IJSObjectReference
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, default, args);

        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier,
            CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier != "streamedJsHandler") return default!;
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return default!;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ImportingRuntime(IJSObjectReference module) : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromResult((TValue)(object)module);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier,
            CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }

    private sealed class ThrowingBrowser : IJSObjectReference
    {
        public string? InstanceId { get; private set; }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, default, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier,
            CancellationToken cancellationToken, object?[]? args)
        {
            InstanceId = (string)args![1]!;
            throw new JSException("Interop disconnected.");
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FailingCancellationBrowser : IJSObjectReference
    {
        public string? InstanceId { get; private set; }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, default, args);

        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier,
            CancellationToken cancellationToken, object?[]? args)
        {
            InstanceId = (string)args![1]!;
            var receiver = ((DotNetObjectReference<MagicJsInvoke>)args[2]!).Value;
            await receiver.ProcessJsChunk(InstanceId, "one", 0, "1", 0, 1);
            var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = cancellationToken.Register(() => finish.TrySetException(new JSException("Producer failed.")));
            await finish.Task;
            return default!;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class BufferedBrowser : IJSObjectReference
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, default, args);

        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier,
            CancellationToken cancellationToken, object?[]? args)
        {
            Assert.AreEqual("streamedJsHandler", identifier);
            var id = (string)args![1]!;
            var receiver = ((DotNetObjectReference<MagicJsInvoke>)args[2]!).Value;
            await receiver.ProcessJsChunk(id, "one", 0, "1", 0, 1);
            await receiver.ProcessJsChunk(id, "two", 1, "2", 0, 1);
            await receiver.ProcessJsChunk(id, "STREAM_COMPLETE", -1, "", 0, 1);
            return default!;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
