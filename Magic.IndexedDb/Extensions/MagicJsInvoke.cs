using Magic.IndexedDb.Helpers;
using Magic.IndexedDb.Interfaces;
using Magic.IndexedDb.Models;
using Microsoft.JSInterop;
using System.Runtime.CompilerServices;

namespace Magic.IndexedDb.Extensions;

internal class MagicJsInvoke
{
    private readonly IJSObjectReference _jsModule;
    private readonly long _jsMessageSizeBytes;
    private readonly MagicJsonSerializationSettings _settings;
    private readonly MagicJsonSerializationSettings _responseSettings;

    public MagicJsInvoke(IJSObjectReference jsModule, long jsMessageSizeBytes,
        MagicJsonSerializationSettings? settings = null, MagicJsonSerializationSettings? responseSettings = null)
    {
        _jsModule = jsModule;
        _jsMessageSizeBytes = jsMessageSizeBytes;
        _settings = settings ?? new MagicJsonSerializationSettings { UseCamelCase = true };
        _responseSettings = responseSettings ?? _settings;
    }

    internal async Task CallJsAsync(string modulePath, string functionName,
        CancellationToken token, params ITypedArgument[] args)
    {
        await MagicVoidStreamJsAsync(modulePath, functionName, token, args);
    }

    internal async Task<T?> CallJsAsync<T>(string modulePath, string functionName,
        CancellationToken token, params ITypedArgument[] args)
    {

        return await MagicStreamJsAsync<T>(modulePath, functionName, token, args) ?? default;
    }

    ///
    internal async Task CallInvokeDefaultJsAsync(string modulePath, string functionName,
        params object[] args)
    {

        var throwAway = await CallInvokeDefaultJsAsync<bool>(modulePath, functionName, args);

        return;
    }

    /// <summary>
    /// Utilizes InvokeAsync normally with no special serialization or streaming.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="modulePath"></param>
    /// <param name="functionName"></param>
    /// <param name="token"></param>
    /// <param name="args"></param>
    /// <returns></returns>
    internal async Task<T?> CallInvokeDefaultJsAsync<T>(string modulePath, string functionName,
        params object[] args)
    {
        return await TrueCallInvokeDefaultJsAsync<T>(modulePath, functionName, false, args);
    }

    private async Task<T?> TrueCallInvokeDefaultJsAsync<T>(string modulePath, string functionName,
        bool isVoid,
        params object[] args)
    {
        return await InvokeBrowserAsync(_jsModule.InvokeAsync<T>("JsHandler", isVoid, modulePath, functionName, args));
    }


    internal async Task CallInvokeVoidDefaultJsAsync(string modulePath, string functionName,
        params object[] args)
    {
        await TrueCallInvokeDefaultJsAsync<bool>(modulePath, functionName, true, args);
    }

    internal async IAsyncEnumerable<T?> CallYieldJsAsync<T>(
        string modulePath,
        string functionName,
        [EnumeratorCancellation] CancellationToken token,
        params ITypedArgument[] args)
    {
        await foreach (var item in MagicYieldJsAsync<T>(modulePath, functionName, token, args)
                           .WithCancellation(token)) // Ensure cancellation works in the async stream
        {
            yield return item; // Yield items as they arrive
        }
    }

    private async Task<T?> MagicStreamJsAsync<T>(string modulePath, string functionName, CancellationToken token, params ITypedArgument[] args)
    {
        return await TrueMagicStreamJsAsync<T>(modulePath, functionName, token, false, args);
    }
    private async Task<T?> TrueMagicStreamJsAsync<T>(string modulePath, string functionName,
        CancellationToken token, bool isVoid, params ITypedArgument[] args)
    {
        var settings = _settings;

        var package = new MagicJsPackage
        {
            YieldResults = false,
            ModulePath = modulePath,
            MethodName = functionName,
            Parameters = MagicSerializationHelper.SerializeArguments(args, settings),
            IsVoid = isVoid
        };

        string instanceId = Guid.NewGuid().ToString();

        using var stream = new MemoryStream();
        await MagicSerializationHelper.SerializeJsPackageToStreamAsync(stream, package, settings, token);

        stream.Position = 0;

        using var streamRef = new DotNetStreamReference(stream);
        using var dotNetHelper = DotNetObjectReference.Create(this);

        // Send to JS
        await using var responseStreamRef = await InvokeBrowserAsync(_jsModule.InvokeAsync<IJSStreamReference>("streamedJsHandler",
            token, streamRef, instanceId, dotNetHelper, _jsMessageSizeBytes));

        // 🚀 Convert the stream reference back to JSON in C#
        await using var responseStream = await responseStreamRef.OpenReadStreamAsync(long.MaxValue, token);
        using var reader = new StreamReader(responseStream);

        string jsonResponse = await reader.ReadToEndAsync(token);
        return MagicSerializationHelper.DeserializeObject<T>(jsonResponse, _responseSettings);
    }

    private async IAsyncEnumerable<T?> MagicYieldJsAsync<T>(
        string modulePath, string functionName, 
        [EnumeratorCancellation] CancellationToken token, 
        params ITypedArgument[] args)
    {
        var settings = _settings;

        var package = new MagicJsPackage
        {
            ModulePath = modulePath,
            MethodName = functionName,
            Parameters = MagicSerializationHelper.SerializeArguments(args, settings),
            IsVoid = false,
            YieldResults = true
        };

        string instanceId = Guid.NewGuid().ToString();

        using var stream = new MemoryStream();
        await MagicSerializationHelper.SerializeJsPackageToStreamAsync(stream, package, settings, token);

        stream.Position = 0;
        using var streamRef = new DotNetStreamReference(stream);
        using var dotNetHelper = DotNetObjectReference.Create(this);
        using var producerCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);

        // Register before starting JavaScript so an immediate first chunk cannot race registration.
        MagicJsChunkProcessor.RegisterInstance(instanceId);
        Task producerTask = Task.CompletedTask;

        bool isCompleted = false;

        try
        {
            producerTask = ObserveProducerAsync(_jsModule.InvokeVoidAsync(
                "streamedJsHandler", producerCancellation.Token,
                streamRef, instanceId, dotNetHelper, _jsMessageSizeBytes));
            while (!isCompleted)
            {
                token.ThrowIfCancellationRequested();
                string? completedItem;
                try
                {
                    completedItem = MagicJsChunkProcessor.GetCompletedItem(instanceId);
                }
                catch (Exception queueError)
                {
                    MagicJsChunkProcessor.RemoveInstance(instanceId);
                    throw new InvalidOperationException($"Failed to retrieve chunk for instance {instanceId}.", queueError);
                }

                if (completedItem != null)
                {
                    if (completedItem == "STREAM_COMPLETE")
                    {
                        await producerTask;
                        isCompleted = true;
                        break;
                    }

                    T? deserializedItem;
                    try
                    {
                        deserializedItem = MagicSerializationHelper.DeserializeObject<T>(completedItem, _responseSettings);
                    }
                    catch (Exception deserializationError)
                    {
                        MagicJsChunkProcessor.RemoveInstance(instanceId);
                        throw new InvalidOperationException($"Failed to deserialize chunk for instance {instanceId}.", deserializationError);
                    }

                    yield return deserializedItem;
                }
                else
                {
                    if (producerTask.IsCompleted)
                    {
                        await producerTask;
                        throw new InvalidOperationException(
                            $"JavaScript stream {instanceId} completed without a completion marker.");
                    }

                    await Task.Delay(15, token);
                }
            }
        }
        finally
        {
            try
            {
                producerCancellation.Cancel();
                if (!producerTask.IsCompleted)
                {
                    try
                    {
                        await producerTask;
                    }
                    catch (OperationCanceledException) when (producerCancellation.IsCancellationRequested)
                    {
                        // Expected when enumeration is canceled or disposed before completion.
                    }
                }
            }
            finally
            {
                MagicJsChunkProcessor.RemoveInstance(instanceId);
            }
        }
    }

    private static async ValueTask<T> InvokeBrowserAsync<T>(ValueTask<T> invocation)
    {
        try
        {
            return await invocation;
        }
        catch (JSException error) when (error.Message.Contains("MAGIC_ENCRYPTION_LOCKED:", StringComparison.Ordinal))
        {
            throw new MagicEncryptionLockedException(error);
        }
    }

    private static async Task ObserveProducerAsync(ValueTask invocation)
    {
        try
        {
            await invocation;
        }
        catch (JSException error) when (error.Message.Contains("MAGIC_ENCRYPTION_LOCKED:", StringComparison.Ordinal))
        {
            throw new MagicEncryptionLockedException(error);
        }
    }

    [JSInvokable("ProcessJsChunk")]
    public Task ProcessJsChunk(string instanceId, string chunkInstanceId, int yieldOrderIndex, string chunk, int chunkIndex, int totalChunks)
    {
        if (chunkInstanceId == "STREAM_COMPLETE")
        {
            MagicJsChunkProcessor.AddChunk(instanceId, "STREAM_COMPLETE", -1, "", 0, 1);
            return Task.CompletedTask;
        }

        MagicJsChunkProcessor.AddChunk(instanceId, chunkInstanceId, yieldOrderIndex, chunk, chunkIndex, totalChunks);
        return Task.CompletedTask;
    }

    private async Task MagicVoidStreamJsAsync(string modulePath, string functionName, CancellationToken token, params ITypedArgument[] args)
    {
        await TrueMagicStreamJsAsync<bool>(modulePath, functionName, token, true, args);
    }
}
