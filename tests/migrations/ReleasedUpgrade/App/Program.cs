using Magic.IndexedDb;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<ReleasedUpgrade.App.App>("#app");
#if MANAGED || CODECS
builder.Services.AddMagicBlazorDB(BlazorInteropMode.WASM, false,
    FixtureModel.Tables, [typeof(FixtureRepository)]);
#else
builder.Services.AddMagicBlazorDB(BlazorInteropMode.WASM, false);
#endif
#if CODECS
builder.Services.AddMagicIndexedDbNumericStorage(new MagicNumericStorageOptions(FixtureRepository.East.DatabaseName));
#endif
await builder.Build().RunAsync();
