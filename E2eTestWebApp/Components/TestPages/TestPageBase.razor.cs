using Magic.IndexedDb;
using Magic.IndexedDb.Helpers;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components;
using System.Diagnostics;
using System.Text.Json;

namespace E2eTestWebApp.Components.TestPages;

partial class TestPageBase
{
    private string method = "";
    private string output = "";
    private async Task RunAsync()
    {
        try
        {
            dynamic? output = this.GetType()
                .GetMethods()
                .Single(x => x.Name == method)
                .Invoke(this, []);
            while (output is Task or ValueTask)
                output = await output;
            this.output = JsonSerializer.Serialize(output);
        }
        catch (Exception ex)
        {
            this.output = JsonSerializer.Serialize(new { Exception = ex.Message });
        }
    }

    private void Clear()
    {
        this.output = "";
    }
}
