using Magic.IndexedDb;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<RecoveryFixture.App>("#app");
builder.Services.AddMagicBlazorDB(BlazorInteropMode.WASM, false,
    [typeof(RecoveryRow)], [typeof(RecoveryRepository)]);
await builder.Build().RunAsync();
