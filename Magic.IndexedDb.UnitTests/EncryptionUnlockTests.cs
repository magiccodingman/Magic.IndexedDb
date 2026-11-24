using Microsoft.JSInterop;

namespace Magic.IndexedDb.UnitTests;

[TestClass]
public sealed class EncryptionUnlockTests
{
    [TestMethod]
    public async Task LockInvalidatesSharedProviderEvenWhenItFinishesLate()
    {
        var config = new EncryptionConfiguration(new("records", "account", "test-key"));
        var provider = new DelayedProvider();
        var browser = new RecordingBrowser();
        var unlock = new EncryptionUnlock(config, provider);

        var first = unlock.UnlockAsync(() => Task.FromResult<IJSObjectReference>(browser), false, default);
        await provider.Started.Task;
        var second = unlock.UnlockAsync(() => Task.FromResult<IJSObjectReference>(browser), false, default);
        Assert.AreEqual(1, provider.Requests);

        await unlock.LockAsync(() => Task.FromResult<IJSObjectReference>(browser));
        await Assert.ThrowsAsync<OperationCanceledException>(() => first);
        await Assert.ThrowsAsync<OperationCanceledException>(() => second);
        provider.Key.SetResult(MagicEncryptionKey.FromBytes(new byte[32]));
        Assert.IsTrue(await unlock.UnlockAsync(() => Task.FromResult<IJSObjectReference>(browser), false, default));
        CollectionAssert.AreEqual(new[] {
            "isProtectedDatabaseUnlocked", "lockProtectedDatabase", "isProtectedDatabaseUnlocked", "unlockProtectedDatabase"
        }, browser.Invocations.ToArray());
    }

    [TestMethod]
    public void AccountAndDatabaseBindStorageWhileEachScopeGetsItsOwnRoute()
    {
        var first = new EncryptionConfiguration(new("records", "account", "key"));
        var second = new EncryptionConfiguration(new("records", "account", "key"));
        var other = new EncryptionConfiguration(new("records", "other account", "key"));

        Assert.AreEqual(first.PhysicalName, second.PhysicalName);
        Assert.AreNotEqual(first.Route, second.Route);
        Assert.AreNotEqual(first.PhysicalName, other.PhysicalName);
    }

    private sealed class DelayedProvider : IMagicEncryptionKeyProvider
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<MagicEncryptionKey?> Key { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Requests { get; private set; }

        public ValueTask<MagicEncryptionKey?> GetKeyAsync(MagicEncryptionKeyRequest request, CancellationToken cancellationToken)
        {
            Requests++;
            Started.TrySetResult();
            return new(Key.Task);
        }
    }

    private sealed class RecordingBrowser : IJSObjectReference
    {
        public List<string> Invocations { get; } = [];
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => InvokeAsync<T>(identifier, default, args);
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Invocations.Add((string)args![2]!);
            return ValueTask.FromResult((T)(object)!(Invocations[^1] == "isProtectedDatabaseUnlocked"));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
