using Magic.IndexedDb.Migrations;
using Microsoft.JSInterop;

namespace Magic.IndexedDb.UnitTests;

[TestClass]
public sealed class MigrationDiagnosticsTests
{
    [TestMethod]
    public async Task CancelledRegistration_UnsubscribesTheCallerIdAndDisposesTheReference()
    {
        using var cancellation = new CancellationTokenSource();
        var browser = new ObserverBrowser { CancelAfterRegistration = cancellation };
        var removed = new List<string>();
        var calls = 0;
        await using var subscription = new MagicMigrationSubscription(browser, _ =>
        {
            calls++;
            return ValueTask.CompletedTask;
        }, removed.Add);

        var error = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            subscription.StartAsync(cancellation.Token));

        Assert.AreEqual(cancellation.Token, error.CancellationToken);
        Assert.AreEqual(subscription.Id, browser.RegisteredId);
        CollectionAssert.AreEqual(new[] { subscription.Id }, browser.StoppedIds.ToArray());
        CollectionAssert.AreEqual(new[] { subscription.Id }, removed.ToArray());
        Assert.IsFalse(browser.StopToken.CanBeCanceled,
            "Cleanup must reach the browser even when the registration's caller token is cancelled.");
        Assert.IsNotNull(browser.Reference);
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = browser.Reference.Value);
        await subscription.OnMigrationEvent(new() { Phase = "complete" });
        Assert.AreEqual(0, calls, "A cancelled registration must not retain an active observer.");
    }

    [TestMethod]
    public async Task ObserverFailures_DoNotEscapeOrPreventLaterNotifications()
    {
        var browser = new ObserverBrowser();
        var notifications = new List<string>();
        await using var subscription = new MagicMigrationSubscription(browser, notification =>
        {
            notifications.Add(notification.Phase);
            return notifications.Count switch
            {
                1 => throw new InvalidOperationException("Synchronous observer failure."),
                2 => FailAfterYield(),
                _ => ValueTask.CompletedTask
            };
        }, _ => { });
        await subscription.StartAsync(CancellationToken.None);
        var callback = browser.Reference!.Value;

        await callback.OnMigrationEvent(new() { Phase = "started" });
        await callback.OnMigrationEvent(new() { Phase = "progress" });
        await callback.OnMigrationEvent(new() { Phase = "complete" });

        CollectionAssert.AreEqual(new[] { "started", "progress", "complete" }, notifications.ToArray());
        Assert.AreEqual(0, browser.StoppedIds.Count,
            "An observer exception must not tear down observation or control the migration lifecycle.");
    }

    [TestMethod]
    public async Task Dispose_RemovesTheSubscriptionAndIgnoresCallbacksDuringAndAfterCleanup()
    {
        var browser = new ObserverBrowser();
        var active = new Dictionary<string, MagicMigrationSubscription>();
        var removals = 0;
        var calls = 0;
        await using var subscription = new MagicMigrationSubscription(browser, _ =>
        {
            calls++;
            return ValueTask.CompletedTask;
        }, id =>
        {
            Assert.IsTrue(active.Remove(id));
            removals++;
        });
        active.Add(subscription.Id, subscription);
        await subscription.StartAsync(CancellationToken.None);
        var callback = browser.Reference!.Value;
        await callback.OnMigrationEvent(new() { Phase = "started" });
        browser.DuringStop = () => callback.OnMigrationEvent(new() { Phase = "progress" });

        await subscription.DisposeAsync();
        await callback.OnMigrationEvent(new() { Phase = "complete" });
        await subscription.DisposeAsync();

        Assert.AreEqual(1, calls, "Callbacks queued during unsubscribe or after disposal must be ignored.");
        Assert.AreEqual(0, active.Count);
        Assert.AreEqual(1, removals, "Disposal must remove the subscription exactly once.");
        CollectionAssert.AreEqual(new[] { subscription.Id }, browser.StoppedIds.ToArray());
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = browser.Reference.Value);
    }

    private static async ValueTask FailAfterYield()
    {
        await Task.Yield();
        throw new InvalidOperationException("Asynchronous observer failure.");
    }

    private sealed class ObserverBrowser : IJSObjectReference
    {
        public CancellationTokenSource? CancelAfterRegistration { get; init; }
        public string? RegisteredId { get; private set; }
        public DotNetObjectReference<MagicMigrationSubscription>? Reference { get; private set; }
        public List<string> StoppedIds { get; } = [];
        public CancellationToken StopToken { get; private set; }
        public Func<Task>? DuringStop { get; set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            switch (identifier)
            {
                case "observeMigrations":
                    RegisteredId = (string)args![0]!;
                    Reference = (DotNetObjectReference<MagicMigrationSubscription>)args[1]!;
                    if (CancelAfterRegistration is not null)
                    {
                        // The browser accepted registration; the interop result is
                        // cancelled before the caller can observe its completion.
                        CancelAfterRegistration.Cancel();
                        throw new OperationCanceledException(cancellationToken);
                    }
                    return default!;
                case "stopObservingMigrations":
                    StoppedIds.Add((string)args![0]!);
                    StopToken = cancellationToken;
                    if (DuringStop is not null) await DuringStop();
                    return default!;
                default:
                    throw new InvalidOperationException($"Unexpected observer JS call '{identifier}'.");
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
