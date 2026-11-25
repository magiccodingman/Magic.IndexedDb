using System.Text.Json;
using Magic.IndexedDb.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace Magic.IndexedDb.UnitTests;

[TestClass]
public sealed class NumericConfigurationTests
{
    [TestMethod]
    public void SeparateRegistrationCallsAccumulateWithoutSilentlyDroppingAPolicy()
    {
        var services = new ServiceCollection();
        services.AddMagicIndexedDbNumericStorage(new MagicNumericStorageOptions("first"));
        services.AddMagicIndexedDbNumericStorage(new MagicNumericStorageOptions("second"));
        Assert.Throws<ArgumentException>(() => services.AddMagicIndexedDbNumericStorage(new MagicNumericStorageOptions("first")));
        using var provider = services.BuildServiceProvider();
        var configuration = provider.GetRequiredService<NumericStorageConfiguration>();
        Assert.AreEqual(MagicNumericStorageMode.Lossless, configuration.Mode("first"));
        Assert.AreEqual(MagicNumericStorageMode.Lossless, configuration.Mode("second"));
    }

    [TestMethod]
    public void UnconfiguredDatabasesRemainLegacyAndConfiguredNamesAreValidated()
    {
        var configuration = new NumericStorageConfiguration([new("exact"), new("old", MagicNumericStorageMode.Legacy)]);
        Assert.AreEqual(MagicNumericStorageMode.Lossless, configuration.Mode("exact"));
        Assert.AreEqual(MagicNumericStorageMode.Legacy, configuration.Mode("old"));
        Assert.AreEqual(MagicNumericStorageMode.Legacy, configuration.Mode("unconfigured"));
        Assert.Throws<ArgumentException>(() => new NumericStorageConfiguration([new("same"), new("same")]));
        Assert.Throws<ArgumentException>(() => new NumericStorageConfiguration([new("__magic_numeric_v1__:reserved")]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NumericStorageConfiguration([new("exact", (MagicNumericStorageMode)99)]));
    }

    [TestMethod]
    public void PlainLosslessPayloadNamesItsSourceAndPersistentVersion()
    {
        var schema = SchemaHelper.GetStoreSchema(typeof(NumericSerializationTests.AutoRecord));
        var payload = NumericStorageConfiguration.CreateDatabase("exact", [schema]);
        StringAssert.StartsWith(payload.GetProperty("name").GetString()!, "__magic_numeric_v1__:");
        Assert.AreEqual(NumericStorageConfiguration.PhysicalName("exact"), payload.GetProperty("name").GetString());
        Assert.AreNotEqual(NumericStorageConfiguration.PhysicalName("other"), payload.GetProperty("name").GetString());
        Assert.AreEqual("exact", payload.GetProperty("numericLegacyName").GetString());
        Assert.AreEqual("lossless", payload.GetProperty("numericStorage").GetProperty("mode").GetString());
        Assert.AreEqual(1, payload.GetProperty("numericStorage").GetProperty("version").GetInt32());
    }

    [TestMethod]
    public void EncryptionKeepsLegacyIdentityAndSeparatesLosslessAccounts()
    {
        var legacy = new EncryptionConfiguration(new("records", "account", "key"));
        var legacyPayload = legacy.CreateDatabase([]);
        Assert.IsFalse(legacyPayload.TryGetProperty("numericStorage", out _));
        Assert.IsFalse(legacyPayload.TryGetProperty("numericLegacyName", out _));
        Assert.AreSame(legacy, legacy.WithNumericStorage(MagicNumericStorageMode.Legacy));

        var exact = legacy.WithNumericStorage(MagicNumericStorageMode.Lossless);
        var same = new EncryptionConfiguration(new("records", "account", "key"), MagicNumericStorageMode.Lossless);
        var other = new EncryptionConfiguration(new("records", "other", "key"), MagicNumericStorageMode.Lossless);
        Assert.AreNotEqual(legacy.PhysicalName, exact.PhysicalName);
        Assert.AreEqual(exact.PhysicalName, same.PhysicalName);
        Assert.AreNotEqual(exact.PhysicalName, other.PhysicalName);
        var exactPayload = exact.CreateDatabase([]);
        Assert.AreEqual(legacy.PhysicalName, exactPayload.GetProperty("numericLegacyName").GetString());
        Assert.AreEqual(1, exactPayload.GetProperty("numericStorage").GetProperty("version").GetInt32());
        Assert.AreEqual(legacy.Request, exact.Request, "The application key provider still receives the logical account/database identity.");
    }
}
