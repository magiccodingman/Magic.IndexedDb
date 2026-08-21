using Magic.IndexedDb.Interfaces;

namespace Magic.IndexedDb.UnitTests;

[TestClass]
public sealed class PublicApiContractTests
{
    [TestMethod]
    public void TypedArgumentSerializationMembers_RemainPublic()
    {
        var methods = typeof(ITypedArgument).GetMethods().Select(method => method.Name).ToHashSet();

        CollectionAssert.IsSubsetOf(
            new[] { "Serialize", "SerializeToJsonElement", "SerializeToJsonString" },
            methods.ToArray());
    }
}
