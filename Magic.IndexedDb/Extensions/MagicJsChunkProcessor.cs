using System.Collections.Concurrent;

namespace Magic.IndexedDb.Extensions;

public static class MagicJsChunkProcessor
{
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, Dictionary<int, string>>> _chunkedMessages = new();
    private static readonly ConcurrentDictionary<string, ConcurrentQueue<string>> _instanceCompleteItems = new();
    private static readonly ConcurrentDictionary<string, SortedDictionary<int, string>> _instanceOrderedItems = new();

    public static void RegisterInstance(string instanceId)
    {
        _chunkedMessages.TryAdd(instanceId, new ConcurrentDictionary<string, Dictionary<int, string>>());
        _instanceCompleteItems.TryAdd(instanceId, new ConcurrentQueue<string>());
        _instanceOrderedItems.TryAdd(instanceId, new SortedDictionary<int, string>());
    }

    public static void AddChunk(string instanceId, string chunkInstanceId, int yieldOrderIndex, string chunk, int chunkIndex, int totalChunks)
    {
        // ✅ Ensure the instance exists before proceeding
        var messageStore = _chunkedMessages.GetOrAdd(instanceId, _ => new ConcurrentDictionary<string, Dictionary<int, string>>());
        var orderedItems = _instanceOrderedItems.GetOrAdd(instanceId, _ => new SortedDictionary<int, string>());
        var completedQueue = _instanceCompleteItems.GetOrAdd(instanceId, _ => new ConcurrentQueue<string>());

        if (chunkInstanceId == "STREAM_COMPLETE")
        {
            completedQueue.Enqueue("STREAM_COMPLETE");
            return;
        }

        // ✅ Ensure chunkInstanceId exists
        var messageChunks = messageStore.GetOrAdd(chunkInstanceId, _ => new Dictionary<int, string>());
        messageChunks[chunkIndex] = chunk;

        // ✅ Check if all chunks are received
        if (messageChunks.Count == totalChunks)
        {
            var fullMessage = string.Join("", messageChunks.OrderBy(kvp => kvp.Key).Select(kvp => kvp.Value));

            lock (orderedItems)
            {
                orderedItems[yieldOrderIndex] = fullMessage;
            }

            messageStore.TryRemove(chunkInstanceId, out _); // ✅ Corrected removal
        }
    }

    public static string? GetCompletedItem(string instanceId)
    {
        var orderedItems = _instanceOrderedItems.GetOrAdd(instanceId, _ => new SortedDictionary<int, string>());
        var completedQueue = _instanceCompleteItems.GetOrAdd(instanceId, _ => new ConcurrentQueue<string>());

        lock (orderedItems)
        {
            // If we still have items to return, prioritize them
            if (orderedItems.Count > 0)
            {
                var firstKey = orderedItems.Keys.First();
                var message = orderedItems[firstKey];

                orderedItems.Remove(firstKey);
                return message;
            }
        }

        // Only return "STREAM_COMPLETE" if ALL items have been returned
        if (completedQueue.TryPeek(out var completedMarker) && completedMarker == "STREAM_COMPLETE")
        {
            completedQueue.TryDequeue(out _); // Remove completion marker now that we're truly done
            return "STREAM_COMPLETE";
        }

        return null;
    }



    public static void RemoveInstance(string instanceId)
    {
        _chunkedMessages.TryRemove(instanceId, out _);
        _instanceCompleteItems.TryRemove(instanceId, out _);
        _instanceOrderedItems.TryRemove(instanceId, out _);
    }
}