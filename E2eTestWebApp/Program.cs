using Magic.IndexedDb;
using Magic.IndexedDb.Extensions;

namespace E2eTestWebApp;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        _ = builder.Services.AddRazorComponents().AddInteractiveServerComponents();

        _ = builder.Services.AddBlazorDB((x) =>
        {
            x.Name = "OpenTest.RegisteredOpen1";
            x.Version = 2;
            x.StoreSchemas = [];
        });
        _ = builder.Services.AddSingleton(new DbStore()
        {
            Name = "OpenTest.RegisteredOpen2",
            Version = 3,
            StoreSchemas = []
        });


        var app = builder.Build();
        _ = app.UseExceptionHandler("/Error", createScopeForErrors: true);
        _ = app.UseAntiforgery();
        _ = app.MapStaticAssets();
        _ = app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
        app.Run();
    }
}
