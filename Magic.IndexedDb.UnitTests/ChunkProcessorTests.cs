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
}
