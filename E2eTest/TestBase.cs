// works with TestPageBase in E2eTestWebApp

using Microsoft.AspNetCore.Components;
using Microsoft.Playwright;
using System.Linq.Expressions;
using System.Reflection;
using E2eTest.Extensions;

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

    private static string ResolveRoute()
        => typeof(TPage).GetCustomAttribute<RouteAttribute>()?.Template ?? "/";

    protected sealed record DisposablePage(IPage Page) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await Page.CloseAsync();
    }

    protected sealed record PlannerTraceRunResult(string Output, string? TraceJson);

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

        // Direct JavaScript regression probes still need a real rendered application
        // document so module imports resolve against the same host as the normal E2E
        // path. Navigating to bare `/` is unnecessary and Firefox can terminate that
        // root navigation without a response when no test page owns the route. Use the
        // page type this fixture is parameterized for, exactly like the method runner.
        await page.GotoAsync(ResolveRoute());
        return page;
    }

    protected async ValueTask<string> RunTestPageMethodAsync(
        Expression<Func<TPage, Func<Task<string>>>> method)
    {
        var result = await RunTestPageMethodCoreAsync(method, capturePlannerTrace: false);
        return result.Output;
    }

    protected async ValueTask<PlannerTraceRunResult> RunTestPageMethodWithPlannerTraceAsync(
        Expression<Func<TPage, Func<Task<string>>>> method)
    {
        return await RunTestPageMethodCoreAsync(method, capturePlannerTrace: true);
    }

    private async ValueTask<PlannerTraceRunResult> RunTestPageMethodCoreAsync(
        Expression<Func<TPage, Func<Task<string>>>> method,
        bool capturePlannerTrace)
    {
        var page = await this.Context.NewPageAsync();

        await page.GotoAsync(ResolveRoute());
        await this.Expect(page.GetByTestId("output")).ToHaveValueAsync("Loaded.");
        
        await page.DeleteDatabaseAsync("Animal");
        await page.DeleteDatabaseAsync("Client");
        await page.DeleteDatabaseAsync("Employee");

        if (capturePlannerTrace)
        {
            await page.EvaluateAsync(
                """
                async () => {
                    const module = await import('/_content/Magic.IndexedDb/magicDbMethods.js');
                    module.clearQueryPlannerTrace();
                }
                """);
        }

        await page.GetByTestId("method").FillAsync(ResolveMethod(method));
        await page.WaitForTimeoutAsync(500);

        await page.GetByTestId("clear").ClickAsync();
        await this.Expect(page.GetByTestId("output")).ToHaveValueAsync("");

        await page.GetByTestId("run").ClickAsync();
        await this.Expect(page.GetByTestId("output")).ToHaveValueAsync(AnyCharacter());

        var output = await page.GetByTestId("output").InputValueAsync();
        string? traceJson = null;

        if (capturePlannerTrace)
        {
            traceJson = await page.EvaluateAsync<string?>(
                """
                async () => {
                    const module = await import('/_content/Magic.IndexedDb/magicDbMethods.js');
                    const trace = module.getLastQueryPlannerTrace();
                    return trace == null ? null : JSON.stringify(trace);
                }
                """);
        }

        return new PlannerTraceRunResult(output, traceJson);
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
