using Magic.IndexedDb;
using Magic.IndexedDb.Helpers;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components;
using System.Diagnostics;
using System.Text.Json;

namespace E2eTestWebApp.TestPages;

partial class TestPageBase
{
    private string method = "";
    private string output = "";

    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender)
        {
            output = "Loaded.";
            this.StateHasChanged();
        }
    }

    private async Task RunAsync()
    {
        try
        {
            var output = this.GetType()
                .GetMethods()
                .Single(x => x.Name == method)
                .Invoke(this, []);
            this.output = await (Task<string>)output!;
            if (string.IsNullOrEmpty(this.output))
                this.output = "Completed.";
        }
        catch (Exception ex)
        {
            this.output = ex.ToString();
        }
    }

    private void Clear()
    {
        this.output = "";
    }
}
