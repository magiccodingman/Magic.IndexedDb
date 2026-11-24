using Microsoft.JSInterop;

namespace Magic.IndexedDb;

internal sealed class EncryptionUnlock(EncryptionConfiguration configuration, IMagicEncryptionKeyProvider provider)
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _browser = new(1);
    private Task _lockOperation = Task.CompletedTask;
    private Attempt? _attempt;
    private bool _disposed;

    private sealed class Attempt
    {
        public CancellationTokenSource Cancellation { get; } = new();
        public TaskCompletionSource<bool> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public Task<bool> UnlockAsync(Func<Task<IJSObjectReference>> getModule, bool initialize, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Attempt attempt;
        Task previousLock;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_attempt is not null && !_attempt.Result.Task.IsCompleted)
                return _attempt.Result.Task.WaitAsync(cancellationToken);

            _attempt = attempt = new();
            previousLock = _lockOperation;
        }
        _ = RunUnlockAsync(getModule, initialize, attempt, previousLock);
        return attempt.Result.Task.WaitAsync(cancellationToken);
    }

    private async Task RunUnlockAsync(Func<Task<IJSObjectReference>> getModule, bool initialize, Attempt attempt, Task previousLock)
    {
        var token = attempt.Cancellation.Token;
        try
        {
            await previousLock;
            token.ThrowIfCancellationRequested();
            var module = await getModule().WaitAsync(token);
            if (await InvokeAsync<bool>(module, "isProtectedDatabaseUnlocked", false, configuration.Route))
            {
                token.ThrowIfCancellationRequested();
                attempt.Result.TrySetResult(true);
                return;
            }

            var key = await provider.GetKeyAsync(configuration.Request, token);
            await _browser.WaitAsync(token);
            try
            {
                token.ThrowIfCancellationRequested();
                if (key is not null)
                    await InvokeAsync<bool>(module, "unlockProtectedDatabase", true,
                        configuration.Route, configuration.Request.KeyId, key.Material, initialize);

                token.ThrowIfCancellationRequested();
                attempt.Result.TrySetResult(key is not null);
            }
            finally
            {
                _browser.Release();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            attempt.Result.TrySetCanceled(token);
        }
        catch (Exception error)
        {
            attempt.Result.TrySetException(error);
        }
        finally
        {
            lock (_sync)
            {
                if (_attempt == attempt)
                    _attempt = null;
            }
        }
    }

    public Task LockAsync(Func<Task<IJSObjectReference>> getModule, bool dispose = false)
    {
        lock (_sync)
        {
            if (_disposed)
                return _lockOperation;

            _disposed = dispose;
            var previous = _attempt;
            _attempt = null;
            previous?.Result.TrySetCanceled();
            _lockOperation = RunLockAsync(getModule, previous, _lockOperation, dispose);
            return _lockOperation;
        }
    }

    private async Task RunLockAsync(Func<Task<IJSObjectReference>> getModule, Attempt? previous, Task previousLock, bool dispose)
    {
        // CancelAsync marks the token immediately and runs provider callbacks asynchronously.
        var cancellation = previous?.Cancellation.CancelAsync();
        if (cancellation is not null)
            _ = ObserveCancellationAsync(cancellation);

        try
        {
            await previousLock;
        }
        catch (JSException)
        {
            // Retry if a previous browser invocation failed.
        }

        var module = await getModule();
        await _browser.WaitAsync();
        try
        {
            await InvokeAsync<bool>(module, dispose ? "releaseProtectedDatabase" : "lockProtectedDatabase", true,
                configuration.Route);
        }
        finally
        {
            _browser.Release();
        }
    }

    private static async Task ObserveCancellationAsync(Task cancellation)
    {
        try
        {
            await cancellation;
        }
        catch (AggregateException)
        {
            // Provider cancellation callbacks do not control the lock.
        }
    }

    internal static ValueTask<T> InvokeAsync<T>(IJSObjectReference module, string method, bool isVoid, params object?[] args) =>
        module.InvokeAsync<T>("JsHandler", isVoid, Cache.MagicDbJsImportPath, method, args);
}
