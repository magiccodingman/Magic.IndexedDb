// works with TestPageBase in E2eTestWebApp

using Microsoft.AspNetCore.Components;
using Microsoft.Playwright;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
namespace E2eTest;


[TestClass]
#pragma warning disable MSTEST0016 // Test class should have test method
public abstract partial class TestBase<TPage> : ContextTest
#pragma warning restore MSTEST0016 // Test class should have test method
{
    private static string ResolveMethod<TObject, TMethod>(Expression<Func<TObject, TMethod>> method)
    {
        var unaryExpression = (UnaryExpression)method.Body;
        var methodCallExpression = (MethodCallExpression)unaryExpression.Operand;
        var methodInfoExpression = (ConstantExpression)methodCallExpression.Object!;
        var methodInfo = (MemberInfo)methodInfoExpression.Value!;
        return methodInfo.Name;
    }

    protected sealed record DisposablePage(IPage Page) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await Page.CloseAsync();
    }

    protected async ValueTask<DisposablePage> NewPageAsync()
    {
        var page = await this.Context.NewPageAsync();
        await page.GotoAsync("/");
        return new DisposablePage(page);
    }

    protected async ValueTask<TResult?> RunTestPageMethodAsync<TResult>(
        Expression<Func<TPage, Func<Task<TResult>>>> method)
    {
        await using var pageDisposable = await this.NewPageAsync();
        var page = pageDisposable.Page;

        await page.GotoAsync(typeof(TPage).GetCustomAttribute<RouteAttribute>()?.Template ?? "");
        await this.Expect(page.GetByTestId("output")).ToHaveValueAsync(JsonSerializer.Serialize("Loaded."));

        await page.GetByTestId("method").FillAsync(ResolveMethod(method));
        await page.WaitForTimeoutAsync(500);

        await page.GetByTestId("clear").ClickAsync();
        await this.Expect(page.GetByTestId("output")).ToHaveValueAsync("");

        await page.GetByTestId("run").ClickAsync();
        await page.WaitForTimeoutAsync(500);

        await this.Expect(page.GetByTestId("output")).ToHaveValueAsync(AnyCharacter());
        var output = await page.GetByTestId("output").InputValueAsync();

        try
        {
            return JsonSerializer.Deserialize<TResult>(output);
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
        options.BaseURL = Program.BaseUrl;
        return options;
    }

    [GeneratedRegex(".+")]
    private static partial Regex AnyCharacter();
}
