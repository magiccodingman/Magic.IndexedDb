using E2eTest.Entities;
using E2eTest.Extensions;
using E2eTestWebApp.TestPages;

namespace E2eTest;

[TestClass]
public class SingleRecordBasicTest : TestBase<SingleRecordBasicTestPage>
{
    [TestMethod]
    public async Task AddTest()
    {
        var page = await this.NewPageAsync();
        await page.DeleteDatabaseAsync("SingleRecordBasic.Add");

        var result = await this.RunTestPageMethodAsync(p => p.Add);
        Assert.AreEqual("12", result);
    }
}
