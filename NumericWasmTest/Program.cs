using Magic.IndexedDb;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using NumericWasmTest;
using TestBase.Numeric;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");

builder.Services.AddMagicBlazorDB(BlazorInteropMode.WASM, isDebug: true,
    [typeof(NumericValueRecord), typeof(NumericLongKeyRecord), typeof(NumericUnsignedKeyRecord),
        typeof(NumericDecimalKeyRecord), typeof(NumericAutoKeyRecord), typeof(NumericProtectedRecord)],
    [typeof(NumericRepository), typeof(NumericProtectedRepository)]);
builder.Services.AddMagicIndexedDbNumericStorage(
    new(NumericRepository.Lossless.DatabaseName), new(NumericProtectedRepository.Database.DatabaseName));
builder.Services.AddMagicIndexedDbEncryption<SyntheticKeyProvider>(_ =>
    [new(NumericProtectedRepository.Database.DatabaseName, "numeric-wasm-fixture", "synthetic-key")]);

await builder.Build().RunAsync();

// This host only stores generated fixture values. Its fixed key is never an application example.
public sealed class SyntheticKeyProvider : IMagicEncryptionKeyProvider
{
    public ValueTask<MagicEncryptionKey?> GetKeyAsync(MagicEncryptionKeyRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<MagicEncryptionKey?>(MagicEncryptionKey.FromBytes(new byte[32]));
    }
}
