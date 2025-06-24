using Magic.IndexedDb;
using Magic.IndexedDb.Helpers;
using Microsoft.AspNetCore.Components;
using System.Text.Json;
using TestBase.Data;
using TestBase.Models;

namespace E2eTestWebApp.TestPages;

[Route("/CursorTest")]
public class CursorTestPage(IMagicIndexedDb magic) : TestPageBase
{
    public async Task<IMagicQuery<Person>> SetupData()
    {
        var db = await magic.Query<Person>();
        await db.AddRangeAsync(PersonData.persons);
        return db;
    }

    public async Task<string> GetDebugString()
    {
        var db = await SetupData();
        var items = await db.ToListAsync();
        var ret = string.Empty;
        foreach (var item in items)
        {
            ret += System.Text.Json.JsonSerializer.Serialize(item) + "\n";
        }
        return ret;
    }

    public async Task<string> TestWhere36() {
        var result = RunTest("Force Cursor Equal Test", await (await SetupData()).Cursor(x => x.Name == "Zack").ToListAsync(),
        PersonData.persons.Where(x => x.Name == "Zack"));
    return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere41() {
        var result = RunTest("Basic Cursor Equal Test", await (await SetupData()).Cursor(x => x._Age == 35).ToListAsync(),
        PersonData.persons.Where(x => x._Age == 35));
    return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere48() {
        var result = RunTest("Multiple Cursor Conditions OR", await (await SetupData()).Cursor(x => x._Age > 50 || x.TestInt == 3).ToListAsync(),
            PersonData.persons.Where(x => x._Age > 50 || x.TestInt == 3));
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere54() {
        var result = RunTest("Take & With Index Test", await (await SetupData()).Cursor(x => x.Name.StartsWith("J"))
        .OrderBy(x => x.Name).Take(2).ToListAsync(),
                    PersonData.persons.Where(x => x.Name.StartsWith("J")).OrderBy(x => x.Name).ThenBy(x => x._Id).Take(2));
                return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere55() {
        var result = RunTest("TakeLast & With Index Test", await (await SetupData()).Cursor(x => x.Name.StartsWith("J"))
        .OrderBy(x => x.Name).TakeLast(2).ToListAsync(),
                    PersonData.persons.Where(x => x.Name.StartsWith("J")).OrderBy(x => x.Name).ThenBy(x => x._Id).TakeLast(2));
                return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere61() {
        var result = RunTest("Cursor Last Or Default Where Test", new List<Person>() { await (await SetupData()).Cursor(x => x.Name == "Victor").OrderBy(x => x._Age).LastOrDefaultAsync() },
                new List<Person>() { PersonData.persons.Where(x => x.Name == "Victor").OrderBy(x => x._Age).ThenBy(x => x._Id).LastOrDefault() });
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere65() {
        var result = RunTest("Index Last Or Default Where Test", new List<Person>() { await (await SetupData()).Cursor(x => x.Name == "Victor").OrderBy(x => x._Id).LastOrDefaultAsync() },
                new List<Person>() { PersonData.persons.Where(x => x.Name == "Victor").OrderBy(x => x._Id).LastOrDefault() });
        return result.Success ? "OK" : result.Message;
    }

    // 🔄 Mixing Where, OrderBy, Skip, and Take
    public async Task<string> TestWhere69() {
        var result = RunTest("Cursor Where + OrderBy + Skip + Take",
            await (await SetupData()).Cursor(x => x._Age > 30)
                                .OrderBy(x => x._Age)
                            .Take(3)
                            .Skip(2)
                            .ToListAsync(),
        PersonData.persons.Where(x => x._Age > 30).OrderBy(x => x._Age).ThenBy(x => x._Id).Skip(2).Take(3));
    return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere70() {
        var result = RunTest("Cursor Where + OrderBy + Skip + Take + first or default",
    new List<Person>() { await (await SetupData()).Cursor(x => x._Age > 30)
                                .OrderBy(x => x._Age)
                            .Take(3)
                            .Skip(2)
                            .FirstOrDefaultAsync() },
    new List<Person>() { PersonData.persons.Where(x => x._Age > 30).OrderBy(x => x._Age).ThenBy(x => x._Id).Skip(2).Take(3).FirstOrDefault() });
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere71() {
        var result = RunTest("Cursor Where + OrderBy + Skip + Take + last or default",
    new List<Person>() { await (await SetupData()).Cursor(x => x._Age > 30)
                                .OrderBy(x => x._Age)
                            .Take(3)
                            .Skip(2)
                            .LastOrDefaultAsync() },
    new List<Person>() { PersonData.persons.Where(x => x._Age > 30).OrderBy(x => x._Age).ThenBy(x => x._Id).Skip(2).Take(3).LastOrDefault() });
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere72() {
        var result = RunTest("Index Where + OrderBy + Skip + Take",
        await (await SetupData()).Cursor(x => x.Name.StartsWith("J"))
                            .OrderBy(x => x._Id)
                        .Take(3)
                        .Skip(2)
                        .ToListAsync(),
    PersonData.persons.Where(x => x.Name.StartsWith("J")).OrderBy(x => x._Id).ThenBy(x => x._Id).Skip(2).Take(3));
return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere73() {
        var result = RunTest("Index Where + OrderBy Desc + Skip + Take",
        await (await SetupData()).Cursor(x => x.Name.StartsWith("J"))
                            .OrderByDescending(x => x._Id)
                        .Take(3)
                        .Skip(2)
                        .ToListAsync(),
    PersonData.persons.Where(x => x.Name.StartsWith("J")).OrderByDescending(x => x._Id).ThenByDescending(x => x._Id).Skip(2).Take(3));
return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere74() {
        var result = RunTest("Where + OrderByDescending + TakeLast",
            await (await SetupData()).Cursor(x => x._Age < 60)
                                .OrderByDescending(x => x._Age)
                            .TakeLast(2)
                            .ToListAsync(),
        PersonData.persons.Where(x => x._Age < 60).OrderByDescending(x => x._Age).ThenByDescending(x => x._Id).TakeLast(2));
    return result.Success ? "OK" : result.Message;

        //await Task.Delay(10000);
    }

    public async Task<string> TestWhere85() {
        var result = RunTest("Cursor Test Equals",
            await (await SetupData()).Cursor(x => x.Name == "Zack").ToListAsync(),
            PersonData.persons.Where(x => x.Name == "Zack"));
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere91() {
        var result = RunTest("Where + OrderBy + Skip + TakeLast",
            await (await SetupData()).Cursor(x => x.TestInt > 2)
                                .OrderBy(x => x._Id)
                            .TakeLast(2)
                            .ToListAsync(),
        PersonData.persons.Where(x => x.TestInt > 2).OrderBy(x => x._Id).ThenBy(x => x._Id).TakeLast(2));
    return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere92() {
        var result = RunTest("Paginated Index Query with AND Condition",
        await (await SetupData()).Cursor(x => x.TestInt > 2 && x.TestInt == 9)
                            .OrderBy(x => x._Id)
                        .Take(3)
                        .Skip(1)
                        .ToListAsync(),
    PersonData.persons.Where(x => x.TestInt > 2 && x.TestInt == 9).OrderBy(x => x._Id).ThenBy(x => x._Id).Skip(1).Take(3));
return result.Success ? "OK" : result.Message;


    }
    // 🔄 Complex Pagination with Condition
    public async Task<string> TestWhere93() {
        var result = RunTest("Paginated Cursor Query with AND Condition",
            await (await SetupData()).Cursor(x => x._Age > 30 && x.TestInt == 9)
                                .OrderBy(x => x._Age)
                            .Take(3)
                            .Skip(1)
                            .ToListAsync(),
        PersonData.persons.Where(x => x._Age > 30 && x.TestInt == 9).OrderBy(x => x._Age).ThenBy(x => x._Id).Skip(1).Take(3));
    return result.Success ? "OK" : result.Message;
    }
}