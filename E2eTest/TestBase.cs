// works with TestPageBase in E2eTestWebApp

using Microsoft.AspNetCore.Components;
using Microsoft.Playwright;
using System.Linq.Expressions;
using System.Reflection;

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

    protected async ValueTask<IPage> NewPageAsync(string? initScript = null)
    {
        var page = await this.Context.NewPageAsync();
        if (initScript is not null)
            await page.AddInitScriptAsync(scriptPath: initScript);
        else
        {
            initScript = $"{this.GetType().Name}.cs.js";
            if (File.Exists(initScript))
                await page.AddInitScriptAsync(scriptPath: initScript);
        }
        await page.GotoAsync("/");
        return page;
    }

    protected async ValueTask<string> RunTestPageMethodAsync(
        Expression<Func<TPage, Func<Task<string>>>> method)
    {
        var page = await this.Context.NewPageAsync();

        await page.GotoAsync(typeof(TPage).GetCustomAttribute<RouteAttribute>()?.Template ?? "");
        await this.Expect(page.GetByTestId("output")).ToHaveValueAsync("Loaded.");

        await page.GetByTestId("method").FillAsync(ResolveMethod(method));
        await page.WaitForTimeoutAsync(500);

        await page.GetByTestId("clear").ClickAsync();
        await this.Expect(page.GetByTestId("output")).ToHaveValueAsync("");

        await page.GetByTestId("run").ClickAsync();
        await this.Expect(page.GetByTestId("output")).ToHaveValueAsync(AnyCharacter());

        return await page.GetByTestId("output").InputValueAsync();
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
