using E2eTest.Extensions;
using E2eTestWebApp.TestPages;

namespace E2eTest;

[TestClass]
public class SingleRecordBasicTest : TestBase<SingleRecordBasicTestPage>
{
    [TestMethod]
    public async Task AddTest()
    {
        var result = await this.RunTestPageMethodAsync(p => p.Add);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task DeleteTest()
    {
        var result = await this.RunTestPageMethodAsync(p => p.Delete);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task UpdateTest()
    {
        var result = await this.RunTestPageMethodAsync(p => p.Update);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task GetAllTest()
    {
        var result = await this.RunTestPageMethodAsync(p => p.GetAll);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task EmptyCountTest()
    {
        var result = await this.RunTestPageMethodAsync(p => p.EmptyCount);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task YieldAllTest()
    {
        var result = await this.RunTestPageMethodAsync(p => p.YieldAll);
        Assert.AreEqual("OK", result);
    }
}
