using Magic.IndexedDb.Extensions;

namespace Magic.IndexedDb.UnitTests;

[TestClass]
public sealed class ChunkProcessorTests
{
    [TestMethod]
    public void Chunks_AreReassembledAndDrainedBeforeCompletion()
    {
        var instanceId = Guid.NewGuid().ToString("N");
        MagicJsChunkProcessor.RegisterInstance(instanceId);

        try
        {
            MagicJsChunkProcessor.AddChunk(instanceId, "second", 1, "two", 1, 2);
            MagicJsChunkProcessor.AddChunk(instanceId, "second", 1, "part-", 0, 2);
            MagicJsChunkProcessor.AddChunk(instanceId, "first", 0, "first", 0, 1);
            MagicJsChunkProcessor.AddChunk(instanceId, "STREAM_COMPLETE", -1, "", 0, 1);

            Assert.AreEqual("first", MagicJsChunkProcessor.GetCompletedItem(instanceId));
            Assert.AreEqual("part-two", MagicJsChunkProcessor.GetCompletedItem(instanceId));
            Assert.AreEqual("STREAM_COMPLETE", MagicJsChunkProcessor.GetCompletedItem(instanceId));
            Assert.IsNull(MagicJsChunkProcessor.GetCompletedItem(instanceId));
        }
        finally
        {
            MagicJsChunkProcessor.RemoveInstance(instanceId);
        }
    }

    [TestMethod]
    public void CompletionMarker_WaitsForEveryCompletedItemToDrain()
    {
        var instanceId = Guid.NewGuid().ToString("N");
        MagicJsChunkProcessor.RegisterInstance(instanceId);

        try
        {
            MagicJsChunkProcessor.AddChunk(instanceId, "last", 2, "third", 0, 1);
            MagicJsChunkProcessor.AddChunk(instanceId, "STREAM_COMPLETE", -1, "", 0, 1);
            MagicJsChunkProcessor.AddChunk(instanceId, "first", 0, "first", 0, 1);
            MagicJsChunkProcessor.AddChunk(instanceId, "middle", 1, "second", 0, 1);

            CollectionAssert.AreEqual(
                new[] { "first", "second", "third", "STREAM_COMPLETE" },
                Enumerable.Range(0, 4)
                    .Select(_ => MagicJsChunkProcessor.GetCompletedItem(instanceId))
                    .ToArray());
        }
        finally
        {
            MagicJsChunkProcessor.RemoveInstance(instanceId);
        }
    }

    [TestMethod]
    public void ConcurrentStreamInstances_RemainIsolated()
    {
        var first = Guid.NewGuid().ToString("N");
        var second = Guid.NewGuid().ToString("N");
        MagicJsChunkProcessor.RegisterInstance(first);
        MagicJsChunkProcessor.RegisterInstance(second);

        try
        {
            MagicJsChunkProcessor.AddChunk(first, "item", 0, "one", 0, 1);
            MagicJsChunkProcessor.AddChunk(second, "item", 0, "two", 0, 1);

            Assert.AreEqual("one", MagicJsChunkProcessor.GetCompletedItem(first));
            Assert.AreEqual("two", MagicJsChunkProcessor.GetCompletedItem(second));
            Assert.IsNull(MagicJsChunkProcessor.GetCompletedItem(first));
            Assert.IsNull(MagicJsChunkProcessor.GetCompletedItem(second));
        }
        finally
        {
            MagicJsChunkProcessor.RemoveInstance(first);
            MagicJsChunkProcessor.RemoveInstance(second);
        }
    }

    [TestMethod]
    public void RemovedInstance_DoesNotLeakPreviouslyCompletedItems()
    {
        var instanceId = Guid.NewGuid().ToString("N");
        MagicJsChunkProcessor.RegisterInstance(instanceId);
        MagicJsChunkProcessor.AddChunk(instanceId, "item", 0, "secret", 0, 1);

        MagicJsChunkProcessor.RemoveInstance(instanceId);

        Assert.IsNull(MagicJsChunkProcessor.GetCompletedItem(instanceId));
        MagicJsChunkProcessor.RemoveInstance(instanceId);
    }
}
