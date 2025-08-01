﻿using E2eTest.Extensions;
using E2eTestWebApp.TestPages;

namespace E2eTest;

[TestClass]
public class SingleRecordBasicTest : TestBase<SingleRecordBasicTestPage>
{
    [TestMethod]
    public async Task AddTest()
    {
        var result = await this.RunTestPageMethodAsync(p => p.Add);
        Assert.AreEqual(result, "OK");
    }

    [TestMethod]
    public async Task DeleteTest()
    {
        var result = await this.RunTestPageMethodAsync(p => p.Delete);
        Assert.AreEqual(result, "OK");
    }

    [TestMethod]
    public async Task UpdateTest()
    {
        var result = await this.RunTestPageMethodAsync(p => p.Update);
        Assert.AreEqual(result, "OK");
    }

    [TestMethod]
    public async Task GetAllTest()
    {
        var result = await this.RunTestPageMethodAsync(p => p.GetAll);
        Assert.AreEqual(result, "OK");
    }
}
