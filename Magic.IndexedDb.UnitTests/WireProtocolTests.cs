using System.Text.Json;
using Magic.IndexedDb.Helpers;
using Magic.IndexedDb.Interfaces;
using Magic.IndexedDb.Models;

namespace Magic.IndexedDb.UnitTests;

[TestClass]
public sealed class WireProtocolTests
{
    [TestMethod]
    public async Task VersionTwoEnvelope_WritesArgumentsAsJsonValues()
    {
        var settings = new MagicJsonSerializationSettings { UseCamelCase = true };
        ITypedArgument[] arguments =
        [
            new TypedArgument<string>("slash\\ newline\n"),
            new TypedArgument<int>(0),
            new TypedArgument<bool>(false),
            new TypedArgument<string?>(null)
        ];
        var package = new MagicJsPackage
        {
            ModulePath = "./module.js",
            MethodName = "invoke",
            Parameters = MagicSerializationHelper.SerializeArguments(arguments, settings)
        };

        await using var stream = new MemoryStream();
        await MagicSerializationHelper.SerializeJsPackageToStreamAsync(stream, package, settings);
        stream.Position = 0;
        using var document = await JsonDocument.ParseAsync(stream);
        var root = document.RootElement;
        var parameters = root.GetProperty("parameters");

        Assert.AreEqual(2, root.GetProperty("protocolVersion").GetInt32());
        Assert.AreEqual("slash\\ newline\n", parameters[0].GetString());
        Assert.AreEqual(0, parameters[1].GetInt32());
        Assert.IsFalse(parameters[2].GetBoolean());
        Assert.AreEqual(JsonValueKind.Null, parameters[3].ValueKind);
    }
}
