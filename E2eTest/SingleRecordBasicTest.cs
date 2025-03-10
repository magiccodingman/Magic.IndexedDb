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

        // TODO:
        // Ignored should not exist here
        // the property of Nested should be "Value" rather than "value"
        Assert.That.AreJsonEqual("""
            [
                {
                    "id":12,
                    "Normal":"Norm",
                    "Renamed":"R",
                    "Index":"I",
                    "UniqueIndex":"633a97d2-0c92-4c68-883b-364f94ad6030",
                    "Enum":0,
                    "Nested":{"value":1234},
                    "LargeNumber":9007199254740991
                }
            ]
            """, records);
    }

    [TestMethod]
    public async Task DeleteTest()
    {
        var page = await this.NewPageAsync();
        await page.DeleteDatabaseAsync("SingleRecordBasic.Delete");

        var result = await this.RunTestPageMethodAsync(p => p.Delete);
        Assert.AreEqual("OK", result);

        var records = await page.EvaluateAsync<string>("getAll('SingleRecordBasic.Delete', 'Records')");
        Assert.That.AreJsonEqual("[]", records);
    }

    [TestMethod]
    public async Task UpdateTest()
    {
        var page = await this.NewPageAsync();
        await page.DeleteDatabaseAsync("SingleRecordBasic.Update");

        var result = await this.RunTestPageMethodAsync(p => p.Update);
        Assert.AreEqual("1", result);

        var records = await page.EvaluateAsync<string>("getAll('SingleRecordBasic.Update', 'Records')");

        // TODO:
        // Ignored should not exist here
        // the property of Nested should be "Value" rather than "value"
        Assert.That.AreJsonEqual("""
            [
                {
                    "id":12,
                    "Normal":"Updated",
                    "Renamed":"R",
                    "Index":"I",
                    "UniqueIndex":"633a97d2-0c92-4c68-883b-364f94ad6030",
                    "Enum":0,
                    "Nested":{"value":1234},
                    "LargeNumber":9007199254740991
                }
            ]
            """, records);
    }

    [TestMethod]
    public async Task GetByIdTest()
    {
        var page = await this.NewPageAsync();
        await page.DeleteDatabaseAsync("SingleRecordBasic.GetById");

        var result = await this.RunTestPageMethodAsync(p => p.GetById);
        Assert.AreEqual("Norm", result);
    }

    [TestMethod]
    public async Task GetAllTest()
    {
        var page = await this.NewPageAsync();
        await page.DeleteDatabaseAsync("SingleRecordBasic.GetAll");

        var result = await this.RunTestPageMethodAsync(p => p.GetAll);

        // Nested.Value should be 1234
        Assert.That.AreJsonEqual("""
            [{
                "Id":12,
                "Normal":"Norm",
                "ShouldBeRenamed":"R",
                "Ignored":false,
                "Index":"I",
                "UniqueIndex":"633a97d2-0c92-4c68-883b-364f94ad6030",
                "Enum":0,
                "Nested":{"Value":1234},
                "LargeNumber":9007199254740991
            }]
            """, result);
    }
}
