using E2eTest.Extensions;
using E2eTestWebApp.TestPages;

namespace E2eTest;

[TestClass]
public class WhereTest : TestBase<WhereTestPage>
{
    [TestMethod]
    public async Task Where1Test()
    {
        var page = await this.NewPageAsync();
        await page.DeleteDatabaseAsync("Person");
        var result = await this.RunTestPageMethodAsync(p => p.Where1);
        Assert.AreEqual(result, "OK");
    }
}
