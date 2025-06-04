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
        await page.DeleteDatabaseAsync("Where.Where1");
        var result = await this.RunTestPageMethodAsync(p => p.Where1);
        Assert.That.AreJsonEqual("[1,2]", result);
    }
}
