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

    public MagicJsInvoke(IJSObjectReference jsModule, long jsMessageSizeBytes)
    {
        _jsModule = jsModule;
        _jsMessageSizeBytes = jsMessageSizeBytes;
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
        var response = await _jsModule.InvokeAsync<T>("JsHandler", isVoid, modulePath, functionName, args);
        return response;
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
        var settings = new MagicJsonSerializationSettings() { UseCamelCase = true };

        var package = new MagicJsPackage
        {
            YieldResults = false,
            ModulePath = modulePath,
            MethodName = functionName,
            Parameters = MagicSerializationHelper.SerializeObjectsToString(args, settings),
            IsVoid = isVoid
        };

        string instanceId = Guid.NewGuid().ToString();

#if DEBUG
        package.IsDebug = true;
#else
            package.IsDebug = false;
#endif

        using var stream = new MemoryStream();
        await using (var writer = new StreamWriter(stream, leaveOpen: true))
        {
            await MagicSerializationHelper.SerializeObjectToStreamAsync(writer, package, settings);
        }

        // ✅ Immediately release reference to `package`
        package = null;
        GC.Collect();
        GC.WaitForPendingFinalizers();

        stream.Position = 0;

        var streamRef = new DotNetStreamReference(stream);

        // Send to JS
        var responseStreamRef = await _jsModule.InvokeAsync<IJSStreamReference>("streamedJsHandler", 
            streamRef, instanceId, DotNetObjectReference.Create(this), _jsMessageSizeBytes);

        // 🚀 Convert the stream reference back to JSON in C#
        await using var responseStream = await responseStreamRef.OpenReadStreamAsync(long.MaxValue, token);
        using var reader = new StreamReader(responseStream);

        string jsonResponse = await reader.ReadToEndAsync();
        return MagicSerializationHelper.DeserializeObject<T>(jsonResponse, settings);
    }

    private async IAsyncEnumerable<T?> MagicYieldJsAsync<T>(
        string modulePath, string functionName, 
        [EnumeratorCancellation] CancellationToken token, 
        params ITypedArgument[] args)
    {
        var settings = new MagicJsonSerializationSettings() { UseCamelCase = true };

        var package = new MagicJsPackage
        {
            ModulePath = modulePath,
            MethodName = functionName,
            Parameters = MagicSerializationHelper.SerializeObjectsToString(args, settings),
            IsVoid = false,
            YieldResults = true
        };

        string instanceId = Guid.NewGuid().ToString();

        using var stream = new MemoryStream();
        await using (var writer = new StreamWriter(stream, leaveOpen: true))
        {
            await MagicSerializationHelper.SerializeObjectToStreamAsync(writer, package, settings);
        }

        stream.Position = 0;
        var streamRef = new DotNetStreamReference(stream);

        // Call JS with our instanceId
        await _jsModule.InvokeVoidAsync("streamedJsHandler", token, streamRef, instanceId, DotNetObjectReference.Create(this));

        MagicJsChunkProcessor.RegisterInstance(instanceId);

        bool isCompleted = false;

        try
        {
            while (!isCompleted)
            {
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
                        isCompleted = true;
                        break;
                    }

                    T? deserializedItem;
                    try
                    {
                        deserializedItem = MagicSerializationHelper.DeserializeObject<T>(completedItem, settings);
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
                    await Task.Delay(15, token);
                }
            }
        }
        finally
        {
            // Ensure cleanup happens even if an error occurs
            MagicJsChunkProcessor.RemoveInstance(instanceId);
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