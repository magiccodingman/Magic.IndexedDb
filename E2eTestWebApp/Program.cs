using Magic.IndexedDb.Extensions;

namespace E2eTestWebApp;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();
        builder.Services.AddBlazorDB((x) => { });
        var app = builder.Build();
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error", createScopeForErrors: true);
        }
        app.UseAntiforgery();
        app.MapStaticAssets();
        app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
        app.Run();
    }
}
