using Magic.IndexedDb;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using System.Net.Http;
namespace E2eTest;


[TestClass]
#pragma warning disable MSTEST0016 // Test class should have test method
public abstract class TestBase : PageTest
#pragma warning restore MSTEST0016 // Test class should have test method
{
    private readonly HttpClient client;

    protected TestBase()
    {
        this.client = Program.WebApplication.CreateClient();
    }

    public override BrowserNewContextOptions ContextOptions()
    {
        var options = base.ContextOptions();
        options.BaseURL = "http://E2eTestWebApp/";
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
                requestMessage.Headers.Add(header.Key, header.Value);
            }

            var response = await client.SendAsync(requestMessage);
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
}
