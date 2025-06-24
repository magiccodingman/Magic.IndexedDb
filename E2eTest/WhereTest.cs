using E2eTest.Extensions;
using E2eTestWebApp.TestPages;

namespace E2eTest;

[TestClass]
public class WhereTest : TestBase<WhereTestPage>
{
    [TestMethod]
    public async Task Date_Equal(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere0);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Date_Not_Equal(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere1);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Date_Greater_Than(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere2);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Date_Greater_Than_Or_Equal(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere3);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Date_Less_Than(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere4);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Date_Less_Than_Or_Equal(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere5);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Date_Year_Equal(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere6);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Date_Year_Not_Equal(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere7);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Date_Year_Greater_Than(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere8);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Date_Year_Greater_Than_Or_Equal(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere9);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Date_Year_Less_Than(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere10);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Date_Year_Less_Than_Or_Equal(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere11);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Date_Month_Equal(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere12);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Date_Day_Equal(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere13);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Date_Day_Of_Year_Equal(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere14);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Date_Day_Of_Year_Not_Equal(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere15);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Date_Day_Of_Year_Greater_Than(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere16);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Date_Day_Of_Year_Greater_Than_Or_Equal(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere17);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Date_Day_Of_Year_Less_Than(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere18);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Date_Day_Of_Year_Less_Than_Or_Equal(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere19);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Date_Day_Of_Week_Equal(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere20);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Date_Day_Of_Week_Greater_Than(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere21);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Date_Day_Of_Week_Greater_Than_Or_Equal(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere22);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Date_Day_Of_Week_Less_Than(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere23);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Date_Day_Of_Week_Less_Than_Or_Equal(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere24);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task String_Length_Test(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere25);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Nullable_Date_Time_Test(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere26);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Not_Null_Test(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere27);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Is_Null_Test(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere28);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task One_Char_contains(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere29);
        Assert.AreEqual("OK", result);
        
        result = await this.RunTestPageMethodAsync(p => p.TestWhere30);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Ends_With_Test(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere31);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Ends_With_Negative_Test1(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere32);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Contains_Test(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere33);
        Assert.AreEqual("OK", result);
        
        result = await this.RunTestPageMethodAsync(p => p.TestWhere38);
        Assert.AreEqual("OK", result);
        
        result = await this.RunTestPageMethodAsync(p => p.TestWhere44);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Combined_Query_Test(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere34);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Get_All_Test(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere35);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Force_Cursor_Equal_Test(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere36);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Not_Equals_Test(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere37);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Not_Contains_Test(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere39);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Basic_Index_Equal_Test(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere40);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Basic_Cursor_Equal_Test(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere41);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Greater_Than_Age_Test(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere42);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Less_Than_or_Equal_Test(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere43);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task StartsWith_Test(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere45);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Multiple_Conditions_AND(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere46);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Multiple_Index_Conditions_OR(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere47);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Multiple_Cursor_Conditions_OR(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere48);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Nested_AND_OR(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere49);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Ordering_Test(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere50);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Order_Descending_Test(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere51);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Skip_Test1(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere52);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Take_Test(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere53);
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
    public async Task Take_Index_Test(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere56);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task TakeLast_Index_Test(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere57);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Cursor_First_Or_Default_Test(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere58);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Cursor_First_Or_Default_Where_Test(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere59);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Cursor_Last_Or_Default_Test(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere60);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Cursor_Last_Or_Default_Where_Test(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere61);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Index_First_Or_Default_Test(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere62);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Index_First_Or_Default_Where_Test(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere63);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Index_Last_Or_Default_Test(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere64);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Index_Last_Or_Default_Where_Test(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere65);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task TakeLast_Cursor_Test(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere66);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Chained_Where_Conditions(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere67);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Chained_Where_with_AND_OR(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere68);
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
    public async Task Nested_OR_within_AND(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere75);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Nested_AND_within_OR(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere76);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task No_Matching_Records(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere77);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Null_DateOfBirth_Handling(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere78);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Null_Field_with_OR(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere79);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Skip_and_Take_Across_Boundaries(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere80);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Large_Take_Last_Test(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere81);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Return_ToListAsync(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere82);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Universal_Truth_Where_Condition(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere83);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Universal_False_Where_Condition(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere84);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Cursor_Test_Equals(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere85);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task NOT_EQUAL(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere86);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task NOT_Contains(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere87);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task NOT_Greater_Than(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere88);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Deeply_Nested_OR_within_AND(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere89);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task Query_on_Not_mapped_Property(){
        var result = await this.RunTestPageMethodAsync(p => p.TestWhere90);
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
