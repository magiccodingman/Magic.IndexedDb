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


// The sample and the shared test library both define a persisted Person table.
builder.Services.AddMagicBlazorDB(BlazorInteropMode.WASM, builder.HostEnvironment.IsDevelopment(),
    [typeof(Person), typeof(EncryptionBenchmarkRecord), typeof(TestBase.Models.Car),
        typeof(TestBase.Models.CompositeRecord), typeof(TestBase.Models.ContractRecord)],
    [typeof(TestWasm.Repository.IndexDbContext), typeof(EncryptionBenchmarkRepository)]);
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
