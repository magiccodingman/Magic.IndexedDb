using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.JSInterop;

namespace Magic.IndexedDb;

internal sealed class EncryptionUnlock(EncryptionConfiguration configuration, IMagicEncryptionKeyProvider provider)
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _browser = new(1);
    private Task _lockOperation = Task.CompletedTask;
    private Attempt? _attempt;
    private bool _disposed;
    private long _epoch;

    private sealed class Attempt
    {
        public bool Initialize { get; init; }
        public CancellationTokenSource Cancellation { get; } = new();
        public TaskCompletionSource<MagicUnlockResult> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public async Task<bool> UnlockAsync(Func<Task<IJSObjectReference>> getModule, bool initialize, CancellationToken cancellationToken) =>
        await UnlockCoreAsync(getModule, initialize, cancellationToken) is MagicUnlockResult.Unlocked or MagicUnlockResult.AlreadyUnlocked;

    public Task<MagicUnlockResult> UnlockCoreAsync(Func<Task<IJSObjectReference>> getModule, bool initialize,
        CancellationToken cancellationToken, long? epoch = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Attempt attempt;
        Task previousLock;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (epoch.HasValue && epoch.Value != _epoch)
                return Task.FromCanceled<MagicUnlockResult>(new CancellationToken(canceled: true));
            if (_attempt is not null && !_attempt.Result.Task.IsCompleted)
                return _attempt.Initialize == initialize
                    ? _attempt.Result.Task.WaitAsync(cancellationToken)
                    : WaitForDifferentAttemptAsync(_attempt.Result.Task, getModule, initialize, cancellationToken, _epoch);

            _attempt = attempt = new() { Initialize = initialize };
            previousLock = _lockOperation;
        }
        _ = RunUnlockAsync(getModule, initialize, attempt, previousLock);
        return attempt.Result.Task.WaitAsync(cancellationToken);
    }

    private async Task<MagicUnlockResult> WaitForDifferentAttemptAsync(Task<MagicUnlockResult> previous,
        Func<Task<IJSObjectReference>> getModule, bool initialize, CancellationToken cancellationToken, long epoch)
    {
        try
        {
            await previous.WaitAsync(cancellationToken);
        }
        catch (JSException error) when (initialize && error.Message.Contains("MAGIC_ENCRYPTION_UNINITIALIZED:", StringComparison.Ordinal))
        {
            // A pending ordinary unlock cannot provision the vault on behalf of this explicit request.
        }
        return await UnlockCoreAsync(getModule, initialize, cancellationToken, epoch);
    }

    private async Task RunUnlockAsync(Func<Task<IJSObjectReference>> getModule, bool initialize, Attempt attempt, Task previousLock)
    {
        var token = attempt.Cancellation.Token;
        try
        {
            await previousLock;
            token.ThrowIfCancellationRequested();
            var module = await getModule().WaitAsync(token);
            if (await InvokeAsync<bool>(module, "isProtectedDatabaseUnlocked", false, configuration.Route,
                provider is IMagicEncryptionRotationKeyProvider))
            {
                token.ThrowIfCancellationRequested();
                attempt.Result.TrySetResult(MagicUnlockResult.AlreadyUnlocked);
                return;
            }

            var request = configuration.Request;
            string? targetId = null;
            if (!initialize && provider is IMagicEncryptionRotationKeyProvider)
            {
                var hints = await InvokeAsync<JsonElement>(module, "getManagedEncryptionKeyHints", false, configuration.Route);
                if (hints.ValueKind == JsonValueKind.Object)
                {
                    if (hints.TryGetProperty("keyId", out var keyId) && keyId.GetString() is { Length: > 0 } active)
                        request = request with { KeyId = active };
                    if (hints.TryGetProperty("targetKeyId", out var target) && target.GetString() is { Length: > 0 } id)
                        targetId = id;
                }
            }
            var key = await provider.GetKeyAsync(request, token);
            var targetKey = key is not null && targetId is not null
                ? await provider.GetKeyAsync(request with { KeyId = targetId }, token) : null;
            var available = key is not null && (targetId is null || targetKey is not null);
            if (available)
            {
                var temporary = (key!.Material as byte[])?.ToArray();
                var temporaryTarget = (targetKey?.Material as byte[])?.ToArray();
                try
                {
                    await _browser.WaitAsync(token);
                    ValueTask<JsonElement> rotation = default;
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        if (targetKey is null)
                            await InvokeAsync<bool>(module, "unlockProtectedDatabase", true,
                                configuration.Route, request.KeyId, temporary ?? key.Material, initialize);
                        else
                            rotation = InvokeAsync<JsonElement>(module, "authorizeManagedKeyRotation", false,
                                configuration.Route, request.KeyId, temporary ?? key.Material,
                                targetId, temporaryTarget ?? targetKey.Material);
                    }
                    finally
                    {
                        _browser.Release();
                    }

                    if (targetKey is not null)
                    {
                        // The invocation has started. Its browser authorization can now
                        // be revoked while deployment validation is still awaiting I/O.
                        await rotation;
                        if (configuration.ResumeKeyRotationOnUnlock)
                        {
                            await _browser.WaitAsync(token);
                            try
                            {
                                token.ThrowIfCancellationRequested();
                                await InvokeAsync<bool>(module, "scheduleManagedKeyRotationResume", true, configuration.Route);
                            }
                            finally
                            {
                                _browser.Release();
                            }
                        }
                    }
                }
                finally
                {
                    if (temporary is not null) CryptographicOperations.ZeroMemory(temporary);
                    if (temporaryTarget is not null) CryptographicOperations.ZeroMemory(temporaryTarget);
                }
            }
            token.ThrowIfCancellationRequested();
            attempt.Result.TrySetResult(available ? MagicUnlockResult.Unlocked : MagicUnlockResult.KeyUnavailable);
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
            if (_disposed && (!dispose || !_lockOperation.IsFaulted))
                return _lockOperation;

            _disposed = dispose;
            _epoch++;
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
        catch (Exception error) when (error is JSException or OperationCanceledException)
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
