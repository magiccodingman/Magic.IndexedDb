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
        var records = await page.EvaluateAsync<string>("getAll('SingleRecordBasic.Add', 'Records')");
        Assert.That.AreJsonEqual("""
            [
                {
                    "id":12,
                    "normal":"Norm",
                    "Renamed":"R",
                    "index":"I",
                    "uniqueIndex":"633a97d2-0c92-4c68-883b-364f94ad6030",
                    "enum":0,
                    "nested":{"value":1234},
                    "largeNumber":9007199254740991
                }
            ]
            """, records);
    }

    [TestMethod]
    public async Task DeleteTest()
    {
        var page = await this.NewPageAsync();
        _ = await page.EvaluateAsync("deleteTestNewDatabase()");

        var result = await this.RunTestPageMethodAsync(p => p.Delete);
        Assert.AreEqual("OK", result);

        var records = await page.EvaluateAsync<string>("getAll('SingleRecordBasic.Delete', 'Records')");
        Assert.That.AreJsonEqual("""
            [
                {
                    "id":1234,
                    "value":"hello"
                }
            ]
            """, records);
    }
}
