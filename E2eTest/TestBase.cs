using Microsoft.Playwright;
using System.Text.Json;
namespace E2eTest;


[TestClass]
#pragma warning disable MSTEST0016 // Test class should have test method
public abstract partial class TestBase : ContextTest
#pragma warning restore MSTEST0016 // Test class should have test method
{
    private readonly HttpClient client;

    protected async ValueTask<JsonElement> RunTestPageAsync(string page, string method)
    {
        var p = await this.Context.NewPageAsync();
        await p.GotoAsync(page);
        await p.GetByTestId("method").FillAsync(method);
        await p.GetByTestId("clear").ClickAsync();
        await p.GetByTestId("run").ClickAsync();
        await this.Expect(p.GetByTestId("output")).ToHaveTextAsync(AnyCharacter());
        var output = await p.GetByTestId("output").InputValueAsync();
        return JsonSerializer.Deserialize<JsonElement>(output);
    }

    protected TestBase()
    {
        this.client = Program.WebApplication.CreateClient();
    }

    public override BrowserNewContextOptions ContextOptions()
    {
        var options = base.ContextOptions();
        // don't use uppercase letters here
        options.BaseURL = "http://localhost/";
        return options;
    }

    [TestInitialize]
    public async Task Initialize()
    {
        await this.Context.RouteAsync($"{this.ContextOptions().BaseURL}**", async route =>
        {
            var request = route.Request;
            var content = request.PostDataBuffer is { } postDataBuffer
                ? new ByteArrayContent(postDataBuffer)
                : null;

            var requestMessage = new HttpRequestMessage(new(request.Method), request.Url)
            {
                Content = content,
            };

            foreach (var header in request.Headers)
            {
                _ = requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            var response = await this.client.SendAsync(requestMessage);
            var responseBody = await response.Content.ReadAsByteArrayAsync();
            var responseHeaders = response.Content.Headers.Select(h => KeyValuePair.Create(h.Key, string.Join(",", h.Value)));

            await route.FulfillAsync(new()
            {
                BodyBytes = responseBody,
                Headers = responseHeaders,
                Status = (int)response.StatusCode,
            });
        });
    }

    [TestCleanup]
    public void TestCleanup()
    {
        this.client.Dispose();
    }

    [GeneratedRegex(".+")]
    private static partial Regex AnyCharacter();
}
