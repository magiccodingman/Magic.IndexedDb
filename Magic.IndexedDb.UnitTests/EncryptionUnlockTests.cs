using Microsoft.JSInterop;

namespace Magic.IndexedDb.UnitTests;

[TestClass]
public sealed class EncryptionUnlockTests
{
    [TestMethod]
    public async Task CancellingTheOnlyWaiterDoesNotCancelTheUnlockAttempt()
    {
        var config = new EncryptionConfiguration(new("records", "account", "test-key"));
        var provider = new DelayedProvider();
        var browser = new RecordingBrowser();
        var unlock = new EncryptionUnlock(config, provider);
        Task<IJSObjectReference> Module() => Task.FromResult<IJSObjectReference>(browser);
        using var wait = new CancellationTokenSource();

        var waiting = unlock.UnlockAsync(Module, initialize: false, wait.Token);
        await provider.Started.Task;
        wait.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => waiting);
        Assert.IsFalse(provider.AttemptToken.IsCancellationRequested);
        Assert.IsFalse(browser.Unlocked.Task.IsCompleted);

        provider.Key.SetResult(MagicEncryptionKey.FromBytes(new byte[32]));
        await browser.Unlocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(1, provider.Requests);
    }

    [TestMethod]
    public async Task LockInvalidatesSharedProviderEvenWhenItFinishesLate()
    {
        var config = new EncryptionConfiguration(new("records", "account", "test-key"));
        var provider = new DelayedProvider();
        var browser = new RecordingBrowser();
        var unlock = new EncryptionUnlock(config, provider);

        using var wait = new CancellationTokenSource();
        var first = unlock.UnlockAsync(() => Task.FromResult<IJSObjectReference>(browser), false, wait.Token);
        await provider.Started.Task;
        var second = unlock.UnlockAsync(() => Task.FromResult<IJSObjectReference>(browser), false, default);
        Assert.AreEqual(1, provider.Requests);

        wait.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => first);
        Assert.IsFalse(provider.AttemptToken.IsCancellationRequested);

        await unlock.LockAsync(() => Task.FromResult<IJSObjectReference>(browser));
        Assert.IsTrue(provider.AttemptToken.IsCancellationRequested);
        await Assert.ThrowsAsync<OperationCanceledException>(() => second);
        provider.Key.SetResult(MagicEncryptionKey.FromBytes(new byte[32]));
        Assert.IsTrue(await unlock.UnlockAsync(() => Task.FromResult<IJSObjectReference>(browser), false, default));
        Assert.AreEqual(2, provider.Requests, "Only the new attempt may unlock after the explicit lock.");
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

    [TestMethod]
    public void ExplicitPhysicalBindingPreservesTheAccountIdentityAndReservedSessionRoute()
    {
        var options = new MagicEncryptionOptions("logical-records", "account", "test-key")
            { PhysicalDatabaseName = "existing-browser-records" };
        var configuration = new EncryptionConfiguration(options);
        Assert.AreEqual("existing-browser-records", configuration.PhysicalName);
        Assert.IsTrue(configuration.Route.StartsWith("__magic_protected__:", StringComparison.Ordinal));
        Assert.AreEqual("logical-records", configuration.Request.DatabaseName);
        Assert.AreEqual("account", configuration.Request.AccountScope);
        Assert.IsTrue(configuration.HasExplicitPhysicalBinding);
        Assert.ThrowsExactly<ArgumentException>(() => new EncryptionConfiguration(options with { PhysicalDatabaseName = " " }));
    }

    [TestMethod]
    public async Task InitializationDoesNotShareOrdinaryUnlockResult()
    {
        var config = new EncryptionConfiguration(new("records", "account", "test-key"));
        var provider = new DelayedProvider();
        var browser = new RecordingBrowser { RequireInitialization = true };
        var unlock = new EncryptionUnlock(config, provider);
        var key = Enumerable.Repeat((byte)7, 32).ToArray();
        Task<IJSObjectReference> Module() => Task.FromResult<IJSObjectReference>(browser);

        var ordinary = unlock.UnlockAsync(Module, initialize: false, default);
        await provider.Started.Task;
        var initialize = unlock.UnlockAsync(Module, initialize: true, default);
        provider.Key.SetResult(MagicEncryptionKey.FromBytes(key));

        await Assert.ThrowsAsync<JSException>(() => ordinary);
        Assert.IsTrue(await initialize);
        Assert.AreEqual(2, provider.Requests);
        Assert.IsTrue(key.All(value => value == 7), "The provider retains ownership of its original key.");
        Assert.IsTrue(browser.LastInteropKey!.All(value => value == 0), "Magic wipes its completed interop copy.");
    }

    private sealed class DelayedProvider : IMagicEncryptionKeyProvider
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<MagicEncryptionKey?> Key { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Requests { get; private set; }
        public CancellationToken AttemptToken { get; private set; }

        public ValueTask<MagicEncryptionKey?> GetKeyAsync(MagicEncryptionKeyRequest request, CancellationToken cancellationToken)
        {
            Requests++;
            AttemptToken = cancellationToken;
            Started.TrySetResult();
            return new(Key.Task);
        }
    }

    private sealed class RecordingBrowser : IJSObjectReference
    {
        public List<string> Invocations { get; } = [];
        public TaskCompletionSource Unlocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool RequireInitialization { get; init; }
        public byte[]? LastInteropKey { get; private set; }
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => InvokeAsync<T>(identifier, default, args);
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            string method = (string)args![2]!;
            Invocations.Add(method);
            if (method == "unlockProtectedDatabase")
            {
                var parameters = (object?[])args[3]!;
                LastInteropKey = (byte[])parameters[2]!;
                if (RequireInitialization && parameters[3] is false)
                    return ValueTask.FromException<T>(new JSException("MAGIC_ENCRYPTION_UNINITIALIZED: Missing policy."));
                Unlocked.TrySetResult();
            }
            return ValueTask.FromResult((T)(object)!(Invocations[^1] == "isProtectedDatabaseUnlocked"));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
