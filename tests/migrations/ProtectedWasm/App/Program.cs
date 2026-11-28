using Microsoft.JSInterop;
using Magic.IndexedDb;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<ProtectedFixture.App>("#app");
builder.Services.AddMagicBlazorDB(BlazorInteropMode.WASM, false,
    [typeof(ProtectedRow), typeof(RotationRow)], [typeof(ProtectedRepository)]);
builder.Services.AddMagicIndexedDbEncryption<FixtureKeys>(_ =>
    [new MagicEncryptionOptions("protected-wasm", "published-account", "fixture-key")
        { PhysicalDatabaseName = ProtectedRepository.PhysicalName }]);
await builder.Build().RunAsync();

public sealed class FixtureKeys(IJSRuntime runtime) : IMagicEncryptionRotationKeyProvider
{
    public static byte? KeyByte { get; set; } = 55;
    public static bool RotationSourceAvailable { get; set; } = true;
    public static List<string> RotationRequests { get; } = [];
    public static int Requests { get; private set; }
    public ValueTask<MagicEncryptionKey?> GetKeyAsync(MagicEncryptionKeyRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        Requests++;
        if (request.DatabaseName == ProtectedRepository.RotationRecords.DatabaseName)
        {
            RotationRequests.Add(request.KeyId);
            byte? key = request.KeyId switch
            {
                "rotation-source" when RotationSourceAvailable => 71,
                "rotation-target" => 89,
                _ => null
            };
            return ValueTask.FromResult(key is { } material
                ? MagicEncryptionKey.FromBytes(Enumerable.Repeat(material, 32).ToArray()) : null);
        }
        return ValueTask.FromResult(KeyByte is { } value
            ? MagicEncryptionKey.FromBytes(Enumerable.Repeat(value, 32).ToArray()) : null);
    }

    public ValueTask<bool> CanReacquireKeysAsync(MagicEncryptionKeyRequest source, MagicEncryptionKeyRequest target,
        CancellationToken cancellationToken) => ValueTask.FromResult(
            source.KeyId == "rotation-source" && target.KeyId == "rotation-target");
}
