using Magic.IndexedDb;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<ProtectedFixture.App>("#app");
builder.Services.AddMagicBlazorDB(BlazorInteropMode.WASM, false,
    [typeof(ProtectedRow)], [typeof(ProtectedRepository)]);
builder.Services.AddMagicIndexedDbEncryption<FixtureKeys>(_ =>
    [new MagicEncryptionOptions("protected-wasm", "published-account", "fixture-key")
        { PhysicalDatabaseName = ProtectedRepository.PhysicalName }]);
await builder.Build().RunAsync();

public sealed class FixtureKeys : IMagicEncryptionKeyProvider
{
    public static int Requests { get; private set; }
    public ValueTask<MagicEncryptionKey?> GetKeyAsync(MagicEncryptionKeyRequest request, CancellationToken cancellationToken)
    {
        Requests++;
        return ValueTask.FromResult<MagicEncryptionKey?>(MagicEncryptionKey.FromBytes(Enumerable.Repeat((byte)55, 32).ToArray()));
    }
}
