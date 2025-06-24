using E2eTest.Extensions;
using E2eTestWebApp.TestPages;

namespace E2eTest;

[TestClass]
public class CursorTest : TestBase<CursorTestPage>
{
[TestMethod]
    public async Task Force_Cursor_Equal_Test(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere36);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Basic_Cursor_Equal_Test(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere41);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Multiple_Cursor_Conditions_OR(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere48);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Take_And_With_Index_Test(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere54);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task TakeLast_And_With_Index_Test(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere55);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Cursor_Last_Or_Default_Where_Test(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere61);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Index_Last_Or_Default_Where_Test(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere65);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Cursor_Where_Plus_OrderBy_Plus_Skip_Plus_Take(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere69);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Cursor_Where_Plus_OrderBy_Plus_Skip_Plus_Take_Plus_first_or_default(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere70);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Cursor_Where_Plus_OrderBy_Plus_Skip_Plus_Take_Plus_last_or_default(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere71);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Index_Where_Plus_OrderBy_Plus_Skip_Plus_Take(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere72);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Index_Where_Plus_OrderBy_Desc_Plus_Skip_Plus_Take(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere73);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Where_Plus_OrderByDescending_Plus_TakeLast(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere74);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Cursor_Test_Equals(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere85);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Where_Plus_OrderBy_Plus_Skip_Plus_TakeLast(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere91);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Paginated_Index_Query_with_AND_Condition(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere92);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Paginated_Cursor_Query_with_AND_Condition(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere93);
        Assert.AreEqual("OK", result);
    }
}
