using E2eTestWebApp.TestPages;

namespace E2eTest;

[TestClass]
public sealed class QueryPlannerRegressionTestExpressions : TestBase<AuditExpressionPage>
{
    [TestMethod]
    public async Task CapturedOperands_MatchCompiledCSharpInIndexedAndCursorQueries() =>
        Assert.AreEqual("OK", await RunTestPageMethodAsync(page => page.CapturedOperands));

    [TestMethod]
    public async Task NullablePredicates_MatchCompiledCSharpInIndexedAndCursorQueries() =>
        Assert.AreEqual("OK", await RunTestPageMethodAsync(page => page.NullablePredicates));

    [TestMethod]
    public async Task DateCutoffs_MatchCompiledCSharpInIndexedAndCursorQueries() =>
        Assert.AreEqual("OK", await RunTestPageMethodAsync(page => page.DateCutoffs));

    [TestMethod]
    public async Task ConvertedPropertyOperands_MatchCompiledCSharpInIndexedAndCursorQueries() =>
        Assert.AreEqual("OK", await RunTestPageMethodAsync(page => page.ConvertedPropertyOperands));
}
