using System.Text.Json.Serialization;
using Magic.IndexedDb.Helpers;
using Magic.IndexedDb.Models;
using Magic.IndexedDb.SchemaAnnotations;

namespace Magic.IndexedDb.UnitTests;

[TestClass]
public sealed class ConstructorContractTests
{
    [TestMethod]
    public void MagicConstructor_IsCaseInsensitive_AndPopulatesRemainingProperties()
    {
        const string json = """{"ID":42,"name":"Ada","description":"hybrid"}""";

        var result = MagicSerializationHelper.DeserializeObject<HybridModel>(json);

        Assert.IsNotNull(result);
        Assert.AreEqual(42, result.Id);
        Assert.AreEqual("Ada", result.Name);
        Assert.AreEqual("hybrid", result.Description);
        Assert.AreEqual("magic", result.ConstructorUsed);
    }

    [TestMethod]
    public void MagicConstructor_HonorsOptionalParameterDefaults()
    {
        var result = MagicSerializationHelper.DeserializeObject<HybridModel>("""{"id":42}""");

        Assert.IsNotNull(result);
        Assert.AreEqual(42, result.Id);
        Assert.AreEqual("default", result.Name);
    }

    [TestMethod]
    public void JsonConstructor_RemainsSupported()
    {
        const string json = """{"id":7,"name":"Grace"}""";

        var result = MagicSerializationHelper.DeserializeObject<JsonModel>(json);

        Assert.IsNotNull(result);
        Assert.AreEqual(7, result.Id);
        Assert.AreEqual("Grace", result.Name);
        Assert.AreEqual("json", result.ConstructorUsed);
    }

    [TestMethod]
    public void NestedParameterizedType_DoesNotRequireAParameterlessConstructor()
    {
        const string json = """{"value":{"code":"nested"}}""";

        var result = MagicSerializationHelper.DeserializeObject<OuterModel>(json);

        Assert.IsNotNull(result);
        Assert.IsNotNull(result.Value);
        Assert.AreEqual("nested", result.Value.Code);
    }

    [TestMethod]
    public void MultipleMagicConstructors_FailWithAnActionableError()
    {
        var exception = Assert.ThrowsExactly<MagicConstructorException>(() =>
            MagicSerializationHelper.DeserializeObject<AmbiguousModel>("""{"id":1}"""));

        StringAssert.Contains(exception.Message, "multiple constructors");
        StringAssert.Contains(exception.Message, nameof(MagicConstructorAttribute));
    }

    private sealed class HybridModel
    {
        public int Id { get; }
        public string Name { get; }
        public string? Description { get; set; }
        public string ConstructorUsed { get; }

        public HybridModel()
        {
            Name = "parameterless";
            ConstructorUsed = "parameterless";
        }

        [MagicConstructor]
        public HybridModel(int id, string name = "default")
        {
            Id = id;
            Name = name;
            ConstructorUsed = "magic";
        }
    }

    private sealed class JsonModel
    {
        public int Id { get; }
        public string Name { get; }
        public string ConstructorUsed { get; }

        public JsonModel()
        {
            Name = "parameterless";
            ConstructorUsed = "parameterless";
        }

        [JsonConstructor]
        public JsonModel(int id, string name)
        {
            Id = id;
            Name = name;
            ConstructorUsed = "json";
        }
    }

    private sealed class OuterModel
    {
        public ParameterizedValue? Value { get; set; }
    }

    private sealed class ParameterizedValue(string code)
    {
        public string Code { get; } = code;
    }

    private sealed class AmbiguousModel
    {
        public int Id { get; }

        [MagicConstructor]
        public AmbiguousModel(int id) => Id = id;

        [MagicConstructor]
        public AmbiguousModel(long id) => Id = checked((int)id);
    }
}
