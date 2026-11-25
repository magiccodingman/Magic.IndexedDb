using System.Collections.Concurrent;

namespace Magic.IndexedDb.Extensions;

public static class MagicJsChunkProcessor
{
    private sealed class StreamState
    {
        public readonly Dictionary<string, Dictionary<int, string>> Messages = new();
        public readonly SortedDictionary<int, string> Completed = new();
        public bool CompletionReceived;
        public bool Removed;
    }

    private static readonly ConcurrentDictionary<string, StreamState> _instances = new();

    public static void RegisterInstance(string instanceId) =>
        _instances.TryAdd(instanceId, new StreamState());

    public static void AddChunk(string instanceId, string chunkInstanceId, int yieldOrderIndex,
        string chunk, int chunkIndex, int totalChunks)
    {
        // A late browser callback must not resurrect a disposed/cancelled stream.
        if (!_instances.TryGetValue(instanceId, out var state)) return;

        lock (state)
        {
            if (state.Removed) return;
            if (chunkInstanceId == "STREAM_COMPLETE")
            {
                state.CompletionReceived = true;
                return;
            }

            if (!state.Messages.TryGetValue(chunkInstanceId, out var chunks))
                state.Messages[chunkInstanceId] = chunks = new Dictionary<int, string>();
            chunks[chunkIndex] = chunk;

            if (chunks.Count == totalChunks)
            {
                state.Completed[yieldOrderIndex] = string.Join("", chunks.OrderBy(pair => pair.Key).Select(pair => pair.Value));
                state.Messages.Remove(chunkInstanceId);
            }
        }
    }

    public static string? GetCompletedItem(string instanceId)
    {
        if (!_instances.TryGetValue(instanceId, out var state)) return null;

        lock (state)
        {
            if (state.Removed) return null;
            if (state.Completed.Count > 0)
            {
                var first = state.Completed.First();
                state.Completed.Remove(first.Key);
                return first.Value;
            }

            // A completion marker cannot hide a partially transferred record.
            // The consumer detects an incomplete stream once the producer ends.
            if (state.CompletionReceived && state.Messages.Count == 0)
            {
                state.CompletionReceived = false;
                return "STREAM_COMPLETE";
            }
            return null;
        }
    }

    public static void RemoveInstance(string instanceId)
    {
        if (!_instances.TryRemove(instanceId, out var state)) return;
        lock (state)
        {
            state.Removed = true;
            state.Messages.Clear();
            state.Completed.Clear();
        }
    }
}
