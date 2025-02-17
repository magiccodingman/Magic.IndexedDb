using Microsoft.Playwright;
using System.Text.Json;
namespace E2eTest;


[TestClass]
#pragma warning disable MSTEST0016 // Test class should have test method
public abstract partial class TestBase : ContextTest
#pragma warning restore MSTEST0016 // Test class should have test method
{
    protected async ValueTask<T?> RunTestPageAsync<T>(string page, string method)
    {
        var p = await this.Context.NewPageAsync();

        await p.GotoAsync(page);
        await this.Expect(p.GetByTestId("output")).ToHaveValueAsync(JsonSerializer.Serialize("Loaded."));

        await p.GetByTestId("method").FillAsync(method);
        await p.WaitForTimeoutAsync(500);

        await p.GetByTestId("clear").ClickAsync();
        await this.Expect(p.GetByTestId("output")).ToHaveValueAsync("");

        await p.GetByTestId("run").ClickAsync();
        await p.WaitForTimeoutAsync(500);

        await this.Expect(p.GetByTestId("output")).ToHaveValueAsync(AnyCharacter());
        var output = await p.GetByTestId("output").InputValueAsync();
        try
        {
            return JsonSerializer.Deserialize<T>(output);
        }
        catch
        {
            Assert.Fail($"Unexpected output from test page {page}.{method}:{Environment.NewLine}" +
                $"{output}");
            throw new Exception($"Unexpected output from test page {page}.{method}:{Environment.NewLine}" +
                $"{output}");
        }
    }

    public override BrowserNewContextOptions ContextOptions()
    {
        var options = base.ContextOptions();
        // don't use uppercase letters here
        options.BaseURL = Program.BaseUrl;
        return options;
    }

    [GeneratedRegex(".+")]
    private static partial Regex AnyCharacter();
}
