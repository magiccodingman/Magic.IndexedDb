using Magic.IndexedDb;
using Microsoft.AspNetCore.Components;
using TestBase.Models;
using TestBase.Data;

namespace E2eTestWebApp.TestPages;

[Route("/WhereTest")]
public class WhereTestPage(IMagicIndexedDb magic) : TestPageBase
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

    public async Task<string> TestWhere0()
    {
        var result = RunTest("Date Equal",
            await (await SetupData()).Where(x => x.DateOfBirth.Value.Date == new DateTime(2020, 2, 10)).ToListAsync(),
            PersonData.persons.Where(x => x.DateOfBirth.HasValue && x.DateOfBirth.Value.Date == new DateTime(2020, 2, 10)));
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere1() {
        var result = RunTest("Date Not Equal",
            await (await SetupData()).Where(x => x.DateOfBirth.Value.Date != new DateTime(2020, 2, 10)).ToListAsync(),
            PersonData.persons.Where(x => !x.DateOfBirth.HasValue || x.DateOfBirth.Value.Date != new DateTime(2020, 2, 10)));
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere2() {
        var result = RunTest("Date Greater Than",
            await (await SetupData()).Where(x => x.DateOfBirth.Value.Date > new DateTime(2020, 2, 9)).ToListAsync(),
            PersonData.persons.Where(x => x.DateOfBirth.HasValue && x.DateOfBirth.Value.Date > new DateTime(2020, 2, 9)));
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere3() {
        var result = RunTest("Date Greater Than Or Equal",
            await (await SetupData()).Where(x => x.DateOfBirth.Value.Date >= new DateTime(2020, 2, 10)).ToListAsync(),
            PersonData.persons.Where(x => x.DateOfBirth.HasValue && x.DateOfBirth.Value.Date >= new DateTime(2020, 2, 10)));
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere4() {
        var result = RunTest("Date Less Than",
            await (await SetupData()).Where(x => x.DateOfBirth.Value.Date < new DateTime(2020, 2, 11)).ToListAsync(),
            PersonData.persons.Where(x => x.DateOfBirth.HasValue && x.DateOfBirth.Value.Date < new DateTime(2020, 2, 11)));
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere5() {
        var result = RunTest("Date Less Than Or Equal",
            await (await SetupData()).Where(x => x.DateOfBirth.Value.Date <= new DateTime(2020, 2, 10)).ToListAsync(),
            PersonData.persons.Where(x => x.DateOfBirth.HasValue && x.DateOfBirth.Value.Date <= new DateTime(2020, 2, 10)));
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere6() {
        var result = RunTest("Date Year Equal",
            await (await SetupData()).Where(x => x.DateOfBirth.Value.Year == 2020).ToListAsync(),
            PersonData.persons.Where(x => x.DateOfBirth.HasValue && x.DateOfBirth.Value.Year == 2020));
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere7() {
        var result = RunTest("Date Year Not Equal",
            await (await SetupData()).Where(x => x.DateOfBirth.Value.Year != 2021).ToListAsync(),
            PersonData.persons.Where(x => x.DateOfBirth.HasValue && x.DateOfBirth.Value.Year != 2021));
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere8() {
        var result = RunTest("Date Year Greater Than",
            await (await SetupData()).Where(x => x.DateOfBirth.Value.Year > 2019).ToListAsync(),
            PersonData.persons.Where(x => x.DateOfBirth.HasValue && x.DateOfBirth.Value.Year > 2019));
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere9() {
        var result = RunTest("Date Year Greater Than Or Equal",
            await (await SetupData()).Where(x => x.DateOfBirth.Value.Year >= 2020).ToListAsync(),
            PersonData.persons.Where(x => x.DateOfBirth.HasValue && x.DateOfBirth.Value.Year >= 2020));
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere10() {
        var result = RunTest("Date Year Less Than",
            await (await SetupData()).Where(x => x.DateOfBirth.Value.Year < 2021).ToListAsync(),
            PersonData.persons.Where(x => x.DateOfBirth.HasValue && x.DateOfBirth.Value.Year < 2021));
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere11() {
        var result = RunTest("Date Year Less Than Or Equal",
            await (await SetupData()).Where(x => x.DateOfBirth.Value.Year <= 2020).ToListAsync(),
            PersonData.persons.Where(x => x.DateOfBirth.HasValue && x.DateOfBirth.Value.Year <= 2020));
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere12() {
        var result = RunTest("Date Month Equal",
            await (await SetupData()).Where(x => x.DateOfBirth.Value.Month == 2).ToListAsync(),
            PersonData.persons.Where(x => x.DateOfBirth.HasValue && x.DateOfBirth.Value.Month == 2));
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere13() {
        var result = RunTest("Date Day Equal",
            await (await SetupData()).Where(x => x.DateOfBirth.Value.Day == 10).ToListAsync(),
            PersonData.persons.Where(x => x.DateOfBirth.HasValue && x.DateOfBirth.Value.Day == 10));
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere14() {
        var result = RunTest("Date Day Of Year Equal",
            await (await SetupData()).Where(x => x.DateOfBirth.Value.DayOfYear == 41).ToListAsync(),
            PersonData.persons.Where(x => x.DateOfBirth.HasValue && x.DateOfBirth.Value.DayOfYear == 41));
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere15() {
        var result = RunTest("Date Day Of Year Not Equal",
            await (await SetupData()).Where(x => x.DateOfBirth.Value.DayOfYear != 41).ToListAsync(),
            PersonData.persons.Where(x => x.DateOfBirth.HasValue && x.DateOfBirth.Value.DayOfYear != 41));
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere16() {
        var result = RunTest("Date Day Of Year Greater Than",
            await (await SetupData()).Where(x => x.DateOfBirth.Value.DayOfYear > 40).ToListAsync(),
            PersonData.persons.Where(x => x.DateOfBirth.HasValue && x.DateOfBirth.Value.DayOfYear > 40));
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere17() {
        var result = RunTest("Date Day Of Year Greater Than Or Equal",
            await (await SetupData()).Where(x => x.DateOfBirth.Value.DayOfYear >= 41).ToListAsync(),
            PersonData.persons.Where(x => x.DateOfBirth.HasValue && x.DateOfBirth.Value.DayOfYear >= 41));
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere18() {
        var result = RunTest("Date Day Of Year Less Than",
            await (await SetupData()).Where(x => x.DateOfBirth.Value.DayOfYear < 42).ToListAsync(),
            PersonData.persons.Where(x => x.DateOfBirth.HasValue && x.DateOfBirth.Value.DayOfYear < 42));
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere19() {
        var result = RunTest("Date Day Of Year Less Than Or Equal",
            await (await SetupData()).Where(x => x.DateOfBirth.Value.DayOfYear <= 41).ToListAsync(),
            PersonData.persons.Where(x => x.DateOfBirth.HasValue && x.DateOfBirth.Value.DayOfYear <= 41));
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere20() {
        var result = RunTest("Date Day Of Week Equal",
            await (await SetupData()).Where(x => x.DateOfBirth.Value.DayOfWeek == DayOfWeek.Monday).ToListAsync(),
            PersonData.persons.Where(x => x.DateOfBirth.HasValue && x.DateOfBirth.Value.DayOfWeek == DayOfWeek.Monday));
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere21() {
        var result = RunTest("Date Day Of Week Greater Than",
            await (await SetupData()).Where(x => x.DateOfBirth.Value.DayOfWeek > DayOfWeek.Sunday).ToListAsync(),
            PersonData.persons.Where(x => x.DateOfBirth.HasValue && x.DateOfBirth.Value.DayOfWeek > DayOfWeek.Sunday));
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere22() {
        var result = RunTest("Date Day Of Week Greater Than Or Equal",
            await (await SetupData()).Where(x => x.DateOfBirth.Value.DayOfWeek >= DayOfWeek.Sunday).ToListAsync(),
            PersonData.persons.Where(x => x.DateOfBirth.HasValue && x.DateOfBirth.Value.DayOfWeek >= DayOfWeek.Sunday));
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere23() {
        var result = RunTest("Date Day Of Week Less Than",
            await (await SetupData()).Where(x => x.DateOfBirth.Value.DayOfWeek < DayOfWeek.Tuesday).ToListAsync(),
            PersonData.persons.Where(x => x.DateOfBirth.HasValue && x.DateOfBirth.Value.DayOfWeek < DayOfWeek.Tuesday));
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere24() {
        var result = RunTest("Date Day Of Week Less Than Or Equal",
            await (await SetupData()).Where(x => x.DateOfBirth.Value.DayOfWeek <= DayOfWeek.Monday).ToListAsync(),
            PersonData.persons.Where(x => x.DateOfBirth.HasValue && x.DateOfBirth.Value.DayOfWeek <= DayOfWeek.Monday));
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere25() {
        var result = RunTest("String Length Test", await (await SetupData()).Where(x => x.Name.Length > 4).ToListAsync(),
            PersonData.persons.Where(x => x.Name.Length > 4));
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere26() {
        var result = RunTest("Nullable Date Time Test", await (await SetupData()).Where(x => x.DateOfBirth == new DateTime(2020, 2, 10)).ToListAsync(),
        PersonData.persons.Where(x => x.Name == "Zane"));
    return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere27() {
        var result = RunTest("Not Null Test", await (await SetupData()).Where(x => x.DateOfBirth != null).ToListAsync(),
        PersonData.persons.Where(x => x.DateOfBirth != null));
    return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere28() {
        var result = RunTest("Is Null Test", await (await SetupData()).Where(x => x.DateOfBirth == null).ToListAsync(),
        PersonData.persons.Where(x => x.DateOfBirth == null));
    return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere29() {
        var result = RunTest("One Char contains", await (await SetupData()).Where(x => x.TestInt >= 1 && x.TestInt < 10).ToListAsync(),
            PersonData.persons.Where(x => x.TestInt >= 1 && x.TestInt < 10));
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere30() {
        var result = RunTest("One Char contains", await (await SetupData()).Where(x => x.Name.Contains("J")).ToListAsync(),
        PersonData.persons.Where(x => x.Name.Contains("J")));
    return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere31() {
        var result = RunTest("Ends With Test", await (await SetupData()).Where(x => x.Name.EndsWith("ack")).ToListAsync(),
        PersonData.persons.Where(x => x.Name.EndsWith("ack")));
    return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere32() {
        var result = RunTest("Ends With Negative Test", await (await SetupData()).Where(x => !x.Name.EndsWith("ack")).ToListAsync(),
        PersonData.persons.Where(x => !x.Name.EndsWith("ack")));
    return result.Success ? "OK" : result.Message;

        int[] myArray = { 38, 39 };
    }

    public async Task<string> TestWhere33() {
        int[] myArray = { 38, 39 };
        var result = RunTest("Contains Test", await (await SetupData()).Where(x => myArray.Contains(x._Age)).ToListAsync(),
        PersonData.persons.Where(x => myArray.Contains(x._Age)));
    return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere34() {
        var result = RunTest("Combined Query Test", await (await SetupData()).Where(x => x.Name == "Zack" && x.TestIntStable2 == 10).ToListAsync(),
        PersonData.persons.Where(x => x.Name == "Zack" && x.TestIntStable2 == 10));
    return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere35() {
        var result = RunTest("Get All Test", await (await SetupData()).ToListAsync(),
        PersonData.persons);
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere36() {
        var result = RunTest("Force Cursor Equal Test", await (await SetupData()).Cursor(x => x.Name == "Zack").ToListAsync(),
        PersonData.persons.Where(x => x.Name == "Zack"));
    return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere37() {
        var result = RunTest("Not Equals Test", await (await SetupData()).Where(x => x.Name != "Zack").ToListAsync(),
        PersonData.persons.Where(x => x.Name != "Zack"));
    return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere38() {
        var result = RunTest("Contains Test", await (await SetupData()).Where(x => x.Name.Contains("zac", StringComparison.OrdinalIgnoreCase)).ToListAsync(),
        PersonData.persons.Where(x => x.Name.Contains("zac", StringComparison.OrdinalIgnoreCase)));
    return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere39() {
        var result = RunTest("Not Contains Test", await (await SetupData()).Where(x => !x.Name.Contains("zac", StringComparison.OrdinalIgnoreCase)).ToListAsync(),
        PersonData.persons.Where(x => !x.Name.Contains("zac", StringComparison.OrdinalIgnoreCase)));
    return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere40() {
        var result = RunTest("Basic Index Equal Test", await (await SetupData()).Where(x => x.Name == "Zack").ToListAsync(),
        PersonData.persons.Where(x => x.Name == "Zack"));
    return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere41() {
        var result = RunTest("Basic Cursor Equal Test", await (await SetupData()).Cursor(x => x._Age == 35).ToListAsync(),
        PersonData.persons.Where(x => x._Age == 35));
    return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere42() {
        var result = RunTest("Greater Than Age Test", await (await SetupData()).Where(x => x._Age > 40).ToListAsync(),
            PersonData.persons.Where(x => x._Age > 40));
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere43() {
        var result = RunTest("Less Than or Equal Test", await (await SetupData()).Where(x => x._Age <= 35).ToListAsync(),
            PersonData.persons.Where(x => x._Age <= 35));
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere44() {
        var result = RunTest("Contains Test", await (await SetupData()).Where(x => x.Name.Contains("bo", StringComparison.OrdinalIgnoreCase)).ToListAsync(),
            PersonData.persons.Where(x => x.Name.Contains("bo", StringComparison.OrdinalIgnoreCase)));
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere45() {
        var result = RunTest("StartsWith Test", await (await SetupData()).Where(x => x.Name.StartsWith("C", StringComparison.OrdinalIgnoreCase)).ToListAsync(),
            PersonData.persons.Where(x => x.Name.StartsWith("C", StringComparison.OrdinalIgnoreCase)));
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere46() {
        var result = RunTest("Multiple Conditions AND", await (await SetupData()).Where(x => x._Age > 30 && x.TestInt == 9).ToListAsync(),
            PersonData.persons.Where(x => x._Age > 30 && x.TestInt == 9));
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere47() {
        var result = RunTest("Multiple Index Conditions OR", await (await SetupData()).Where(x => x.Name == "Isla" || x.Name == "Zack").ToListAsync(),
            PersonData.persons.Where(x => x.Name == "Zack" || x.Name == "Isla"));
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere48() {
        var result = RunTest("Multiple Cursor Conditions OR", await (await SetupData()).Cursor(x => x._Age > 50 || x.TestInt == 3).ToListAsync(),
            PersonData.persons.Where(x => x._Age > 50 || x.TestInt == 3));
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere49() {
        var result = RunTest("Nested AND/OR", await (await SetupData()).Where(x => (x._Age > 35 && x.TestInt == 9) || x.Name.Contains("bo", StringComparison.OrdinalIgnoreCase)).ToListAsync(),
            PersonData.persons.Where(x => (x._Age > 35 && x.TestInt == 9) || x.Name.Contains("bo", StringComparison.OrdinalIgnoreCase)));
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere50() {
        var result = RunTest("Ordering Test", await (await SetupData()).OrderBy(x => x._Age).ToListAsync(),
            PersonData.persons.OrderBy(x => x._Age).ThenBy(x => x._Id));
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere51() {
        var result = RunTest("Order Descending Test", await (await SetupData()).OrderByDescending(x => x._Age).ToListAsync(),
            PersonData.persons.OrderByDescending(x => x._Age).ThenByDescending(x => x._Id));
        return result.Success ? "OK" : result.Message;

        var asdfsdfdsfsdf = await (await SetupData()).OrderBy(x => x._Age).Skip(3).ToListAsync();
    }

    public async Task<string> TestWhere52() {
        var result = RunTest("Skip Test", await (await SetupData()).OrderBy(x => x._Age).Skip(3).ToListAsync(),
            PersonData.persons.OrderBy(x => x._Age).ThenBy(x => x._Id).Skip(3));
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere53() {
        var result = RunTest("Take Test", await (await SetupData()).OrderBy(x => x._Age).Take(2).ToListAsync(),
            PersonData.persons.OrderBy(x => x._Age).ThenBy(x => x._Id).Take(2));
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

    public async Task<string> TestWhere56() {
        var result = RunTest("Take Index Test", await (await SetupData()).OrderBy(x => x.Name).Take(2).ToListAsync(),

                /*
                * Take last is special operation that changes order,
                * but this altered version replicates the LINQ to SQL desired result
                */
                PersonData.persons.OrderBy(x => x.Name).ThenBy(x => x._Id).Take(2));
            return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere57() {
        var result = RunTest("TakeLast Index Test", await (await SetupData()).OrderBy(x => x.Name).TakeLast(2).ToListAsync(),

                /*
                * Take last is special operation that changes order,
                * but this altered version replicates the LINQ to SQL desired result
                */
                PersonData.persons.OrderBy(x => x.Name).ThenBy(x => x._Id).TakeLast(2));
            return result.Success ? "OK" : result.Message;

        var totalPersons = await (await SetupData()).CountAsync();
        var test1 = await (await SetupData()).FirstOrDefaultAsync();
        var test2 = await (await SetupData()).FirstOrDefaultAsync(x => x.Name == "Victor");

        var asdffffff = await (await SetupData()).FirstOrDefaultAsync(x => x.Name == "asdfn3nxknnd");
    }

    public async Task<string> TestWhere58() {
        var result = RunTest("Cursor First Or Default Test", new List<Person>() { await (await SetupData()).OrderBy(x => x._Age).FirstOrDefaultAsync() },
        new List<Person>() { PersonData.persons.OrderBy(x => x._Age).ThenBy(x => x._Id).FirstOrDefault() });
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere59() {
        var result = RunTest("Cursor First Or Default Where Test", new List<Person>() { await (await SetupData()).OrderBy(x => x._Age).FirstOrDefaultAsync(x => x.Name == "Victor") },
        new List<Person>() { PersonData.persons.OrderBy(x => x._Age).ThenBy(x => x._Id).FirstOrDefault(x => x.Name == "Victor") });
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere60() {
        var result = RunTest("Cursor Last Or Default Test", new List<Person>() { await (await SetupData()).OrderBy(x => x._Age).LastOrDefaultAsync() },
        new List<Person>() { PersonData.persons.OrderBy(x => x._Age).ThenBy(x => x._Id).LastOrDefault() });
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere61() {
        var result = RunTest("Cursor Last Or Default Where Test", new List<Person>() { await (await SetupData()).Cursor(x => x.Name == "Victor").OrderBy(x => x._Age).LastOrDefaultAsync() },
                new List<Person>() { PersonData.persons.Where(x => x.Name == "Victor").OrderBy(x => x._Age).ThenBy(x => x._Id).LastOrDefault() });
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere62() {
        var result = RunTest("Index First Or Default Test", new List<Person>() { await (await SetupData()).OrderBy(x => x._Id).FirstOrDefaultAsync() },
    new List<Person>() { PersonData.persons.OrderBy(x => x._Id).ThenBy(x => x._Id).FirstOrDefault() });
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere63() {
        var result = RunTest("Index First Or Default Where Test", new List<Person>() { await (await SetupData()).OrderBy(x => x._Id).FirstOrDefaultAsync(x => x.Name == "Victor") },
                new List<Person>() { PersonData.persons.OrderBy(x => x._Id).ThenBy(x => x._Id).FirstOrDefault(x => x.Name == "Victor") });
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere64() {
        var result = RunTest("Index Last Or Default Test", new List<Person>() { await (await SetupData()).OrderBy(x => x._Id).LastOrDefaultAsync() },
    new List<Person>() { PersonData.persons.OrderBy(x => x._Id).ThenBy(x => x._Id).LastOrDefault() });
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere65() {
        var result = RunTest("Index Last Or Default Where Test", new List<Person>() { await (await SetupData()).Cursor(x => x.Name == "Victor").OrderBy(x => x._Id).LastOrDefaultAsync() },
                new List<Person>() { PersonData.persons.Where(x => x.Name == "Victor").OrderBy(x => x._Id).LastOrDefault() });
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere66() {
        var result = RunTest("TakeLast Cursor Test", await (await SetupData()).OrderBy(x => x._Age).TakeLast(2).ToListAsync(),
                PersonData.persons.OrderBy(x => x._Age).ThenBy(x => x._Id).TakeLast(2));
            return result.Success ? "OK" : result.Message;

    }
    // 🛠️ Chaining Multiple Where Statements
    public async Task<string> TestWhere67() {
        var result = RunTest("Chained Where Conditions",
            await (await SetupData()).Where(x => x._Age > 30)
                                .Where(x => x.TestInt == 9)
                            .ToListAsync(),
        PersonData.persons.Where(x => x._Age > 30).Where(x => x.TestInt == 9));
    return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere68() {
        var result = RunTest("Chained Where with AND/OR",
            await (await SetupData()).Where(x => x._Age > 30)
                                .Where(x => x.TestInt == 9 || x.Name.StartsWith("C", StringComparison.OrdinalIgnoreCase))
                            .ToListAsync(),
        PersonData.persons.Where(x => x._Age > 30).Where(x => x.TestInt == 9 || x.Name.StartsWith("C", StringComparison.OrdinalIgnoreCase)));
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
    // 🧩 Complex Nested Conditions
    public async Task<string> TestWhere75() {
        var result = RunTest("Nested OR within AND",
            await (await SetupData()).Where(// EXAMPLE of a deeply nested expression:
            p =>
        (
            // Group 1: TestInt matches
            (p.TestInt == 9 || p.TestInt == 3 || p.TestInt == 7)

            &&

            // Group 2: Specific names or age range
            (
                (p.Name == "Luna" || p.Name == "Jerry" || p.Name == "Jamie")
                || (p._Age >= 35 && p._Age <= 40)
                || (p.Name == "Zane" && p._Age > 45)
            )

            &&

            // Group 3: Age-based logic only
            (p._Age < 30 || p._Age > 50 || p._Age == 35)
        )
    ).ToListAsync(),
        PersonData.persons.Where(// EXAMPLE of a deeply nested expression:
        p =>
        (
            // Group 1: TestInt matches
            (p.TestInt == 9 || p.TestInt == 3 || p.TestInt == 7)

            &&

            // Group 2: Specific names or age range
            (
                (p.Name == "Luna" || p.Name == "Jerry" || p.Name == "Jamie")
                || (p._Age >= 35 && p._Age <= 40)
                || (p.Name == "Zane" && p._Age > 45)
            )

            &&

            // Group 3: Age-based logic only
            (p._Age < 30 || p._Age > 50 || p._Age == 35)
        )).ToList());
    return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere76() {
        var result = RunTest("Nested AND within OR",
            await (await SetupData()).Where(x => (x._Age > 40 && x.TestInt == 9) || x.Name.Contains("bo", StringComparison.OrdinalIgnoreCase))
                                .ToListAsync(),
        PersonData.persons.Where(x => (x._Age > 40 && x.TestInt == 9) || x.Name.Contains("bo", StringComparison.OrdinalIgnoreCase)));
    return result.Success ? "OK" : result.Message;

    }
    // 🚀 Edge Cases
    public async Task<string> TestWhere77() {
        var result = RunTest("No Matching Records",
            await (await SetupData()).Where(x => x.Name == "NonExistentPerson").ToListAsync(),
            PersonData.persons.Where(x => x.Name == "NonExistentPerson"));
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere78() {
        var result = RunTest("Null DateOfBirth Handling",
            await (await SetupData()).Where(x => x.DateOfBirth == null).ToListAsync(),
            PersonData.persons.Where(x => x.DateOfBirth == null));
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere79() {
        var result = RunTest("Null Field with OR",
            await (await SetupData()).Where(x => x.DateOfBirth == null || x._Age > 50).ToListAsync(),
            PersonData.persons.Where(x => x.DateOfBirth == null || x._Age > 50));
        return result.Success ? "OK" : result.Message;

    }
    // 📊 Pagination Stress Test
    public async Task<string> TestWhere80() {
        var result = RunTest("Skip and Take Across Boundaries",
            await (await SetupData()).OrderBy(x => x._Age)
                                .Take(5)
                            .Skip(3)
                            .ToListAsync(),
        PersonData.persons.OrderBy(x => x._Age).ThenBy(x => x._Id).Skip(3).Take(5));
    return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere81() {
        var result = RunTest("Large Take Last Test",
            await (await SetupData()).OrderByDescending(x => x._Age)
                                .TakeLast(5)
                            .ToListAsync(),
        PersonData.persons.OrderByDescending(x => x._Age).ThenByDescending(x => x._Id).TakeLast(5));
    return result.Success ? "OK" : result.Message;

    }
    // Return ToListAsync
    public async Task<string> TestWhere82() {
        var result = RunTest("Return ToListAsync",
            await (await SetupData()).ToListAsync(),
            PersonData.persons.ToList());
        return result.Success ? "OK" : result.Message;

    }
    // ✅ Universal Truth Test (Always Returns Everything)
    public async Task<string> TestWhere83() {
        var result = RunTest("Universal Truth Where Condition",
            await (await SetupData()).Where(x => true).ToListAsync(),
            PersonData.persons.Where(x => true));
        return result.Success ? "OK" : result.Message;

    }
    
    // ❌ Universal False Test (Always Returns Nothing)
    public async Task<string> TestWhere84()
    {
        var result = RunTest("Universal False Where Condition",
            await (await SetupData()).Where(x => false).ToListAsync(),
            PersonData.persons.Where(x => false));
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere85() {
        var result = RunTest("Cursor Test Equals",
            await (await SetupData()).Cursor(x => x.Name == "Zack").ToListAsync(),
            PersonData.persons.Where(x => x.Name == "Zack"));
        return result.Success ? "OK" : result.Message;
    }
    
    // 🤯 Negation Tests
    public async Task<string> TestWhere86()
    {
        var result = RunTest("Negation Test (NOT EQUAL)",
            await (await SetupData()).Where(x => x.Name != "Zack").ToListAsync(),
            PersonData.persons.Where(x => x.Name != "Zack"));
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere87() {
        var result = RunTest("Negation Test (NOT Contains)",
            await (await SetupData()).Where(x => !x.Name.Contains("bo", StringComparison.OrdinalIgnoreCase)).ToListAsync(),
            PersonData.persons.Where(x => !x.Name.Contains("bo", StringComparison.OrdinalIgnoreCase)));
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere88() {
        var result = RunTest("Negation Test (NOT Greater Than)",
            await (await SetupData()).Where(x => !(x._Age > 40)).ToListAsync(),
            PersonData.persons.Where(x => !(x._Age > 40)));
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> TestWhere89() {
        var result = RunTest("Deeply Nested OR within AND",
            await (await SetupData()).Where(x => (x._Age < 50 || x.Name.StartsWith("Z")) && (x.TestInt == 9 && x.DateOfBirth != null))
                                .ToListAsync(),
        PersonData.persons.Where(x => (x._Age < 50 || x.Name.StartsWith("Z")) && (x.TestInt == 9 && x.DateOfBirth != null)));
    return result.Success ? "OK" : result.Message;
    }
    
    public async Task<string> TestWhere90() {
        var result = RunTest("Query on Not mapped Property",
            await (await SetupData()).Where(x => x.DoNotMapTest.Contains("diary")).ToListAsync(),
            []);
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