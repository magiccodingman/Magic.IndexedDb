using Magic.IndexedDb;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;

namespace E2eTest;

[TestClass]
public class Test1 : TestBase
{
    [TestMethod]
    public async Task Test()
    {
        var g = await Page.GotoAsync("/");
    }
    [TestMethod]
    public async Task Test2()
    {
        var g = await Page.GotoAsync("/");
    }
    [TestMethod]
    public async Task Test3()
    {
        var g = await Page.GotoAsync("/");
    }
}
