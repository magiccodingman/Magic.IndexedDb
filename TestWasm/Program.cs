using Magic.IndexedDb;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using TestWasm;
using TestWasm.Models;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });


builder.Services.AddMagicBlazorDB(BlazorInteropMode.WASM, builder.HostEnvironment.IsDevelopment());
builder.Services.AddMagicIndexedDbEncryption<BenchmarkKeyProvider>(_ =>
[
    new(EncryptionBenchmarkRepository.Protected.DatabaseName, "benchmark-fixture", "fixture",
        new Dictionary<string, IReadOnlyCollection<string>>
        {
            [nameof(EncryptionBenchmarkRecord)] = [nameof(EncryptionBenchmarkRecord.Id), nameof(EncryptionBenchmarkRecord.Group)]
        })
]);
builder.Services.AddMudServices();
await builder.Build().RunAsync();
