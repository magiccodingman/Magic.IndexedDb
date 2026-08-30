using System.Linq.Expressions;
using Magic.IndexedDb;
using Microsoft.AspNetCore.Components;
using TestBase.Data;
using TestBase.Models;

namespace E2eTestWebApp.TestPages;

[Route("/QueryPlannerRegression")]
public class QueryPlannerRegressionPage(IMagicIndexedDb magic) : TestPageBase
{
    private async Task<IMagicQuery<Person>> SetupData(IEnumerable<Person>? records = null)
    {
        var db = await magic.Query<Person>();
        await db.AddRangeAsync(records ?? PersonData.persons);
        return db;
    }

    public async Task<string> IndependentIndexedAndPreservesIntersection()
    {
        var db = await SetupData();
        var actual = await db
            .Where(person => person.Name == "Zack" && person.TestInt == 3)
            .ToListAsync();
        var expected = PersonData.persons
            .Where(person => person.Name == "Zack" && person.TestInt == 3);

        var result = RunTest("Independent indexed AND preserves intersection", actual, expected);
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> CompoundIndexPreservesResidualPredicate()
    {
        var db = await SetupData();
        var actual = await db
            .Where(person => person.Name == "Zack"
                && person.TestIntStable2 == 10
                && person.TestInt == 3)
            .ToListAsync();
        var expected = PersonData.persons
            .Where(person => person.Name == "Zack"
                && person.TestIntStable2 == 10
                && person.TestInt == 3);

        var result = RunTest("Compound index preserves residual predicate", actual, expected);
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> MultipleIndexedOrWithTakePreservesSemantics()
    {
        var db = await SetupData();
        var actual = await db
            .Where(person => person.Name == "Zack" || person.TestInt == 3)
            .Take(100)
            .ToListAsync();
        var expected = PersonData.persons
            .Where(person => person.Name == "Zack" || person.TestInt == 3)
            .Take(100);

        var result = RunTest("Multiple indexed OR branches with Take preserve semantics", actual, expected);
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> MultipleStartsWithBranchesPreservePrefixSemantics()
    {
        var db = await SetupData();
        var actual = await db
            .Where(person => person.Name.StartsWith("Za", StringComparison.OrdinalIgnoreCase)
                || person.Name.StartsWith("Lu", StringComparison.OrdinalIgnoreCase))
            .ToListAsync();
        var expected = PersonData.persons
            .Where(person => person.Name.StartsWith("Za", StringComparison.OrdinalIgnoreCase)
                || person.Name.StartsWith("Lu", StringComparison.OrdinalIgnoreCase));

        var result = RunTest("Multiple StartsWith branches preserve prefix semantics", actual, expected);
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> OptimizerDispatchTraceProbe()
    {
        var db = await SetupData();
        var actual = await db
            .Where(person => person.Name == "Zack" || person.Name == "Luna")
            .ToListAsync();
        var expected = PersonData.persons
            .Where(person => person.Name == "Zack" || person.Name == "Luna");

        var result = RunTest("Optimizer dispatch trace probe", actual, expected);
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> CursorOrBranchCorrelationPreservesPairs()
    {
        Person[] records =
        [
            RegressionPerson(1001, "A1B2", age: 1, testInt: 2),
            RegressionPerson(1002, "A1B4", age: 1, testInt: 4),
            RegressionPerson(1003, "A3B2", age: 3, testInt: 2),
            RegressionPerson(1004, "A3B4", age: 3, testInt: 4)
        ];

        var db = await SetupData(records);
        var actual = await db.Cursor(person =>
                (person._Age == 1 && person.TestInt == 2)
                || (person._Age == 3 && person.TestInt == 4))
            .ToListAsync();
        var expected = records.Where(person =>
            (person._Age == 1 && person.TestInt == 2)
            || (person._Age == 3 && person.TestInt == 4));

        var result = RunTest("Cursor OR branch correlation preserves pairs", actual, expected);
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> IndexedDisjointRangeOrPreservesUnion()
    {
        var db = await SetupData();
        var actual = await db
            .Where(person => person.TestInt > 50 || person.TestInt < 0)
            .ToListAsync();
        var expected = PersonData.persons
            .Where(person => person.TestInt > 50 || person.TestInt < 0);

        var result = RunTest("Indexed disjoint range OR preserves union", actual, expected);
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> IndexedSameDirectionRangeOrIsCommutative()
    {
        var db = await SetupData();
        var forward = await db
            .Where(person => person.TestInt < 10 || person.TestInt < 20)
            .ToListAsync();
        var expected = PersonData.persons
            .Where(person => person.TestInt < 10 || person.TestInt < 20);

        var forwardResult = RunTest("Indexed same-direction range OR forward", forward, expected);
        if (!forwardResult.Success)
            return forwardResult.Message;

        var reverse = await db
            .Where(person => person.TestInt < 20 || person.TestInt < 10)
            .ToListAsync();
        var reverseResult = RunTest("Indexed same-direction range OR reverse", reverse, expected);
        return reverseResult.Success ? "OK" : reverseResult.Message;
    }

    public async Task<string> CaseInsensitiveIndexedStartsWithPreservesSemantics()
    {
        var db = await SetupData();
        var actual = await db
            .Where(person => person.Name.StartsWith("c", StringComparison.OrdinalIgnoreCase))
            .ToListAsync();
        var expected = PersonData.persons
            .Where(person => person.Name.StartsWith("c", StringComparison.OrdinalIgnoreCase));

        var result = RunTest("Case-insensitive indexed StartsWith preserves semantics", actual, expected);
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> CompatibilityPruningHonorsCaseInsensitiveStringSemantics()
    {
        var db = await SetupData();
        var actual = await db
            .Where(person => person.Name == "Cathy"
                && person.Name.StartsWith("c", StringComparison.OrdinalIgnoreCase))
            .ToListAsync();
        var expected = PersonData.persons
            .Where(person => person.Name == "Cathy"
                && person.Name.StartsWith("c", StringComparison.OrdinalIgnoreCase));

        var result = RunTest("Compatibility pruning honors case-insensitive string semantics", actual, expected);
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> CursorStringEqualityPreservesCSharpCaseSensitivity()
    {
        var db = await SetupData();
        var actual = await db
            .Cursor(person => person.Name == "zack")
            .ToListAsync();
        var expected = PersonData.persons
            .Where(person => person.Name == "zack");

        var result = RunTest("Cursor string equality preserves C# case sensitivity", actual, expected);
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> EmptyExternalContainsMatchesNothing()
    {
        int[] values = [];
        var db = await SetupData();
        var actual = await db
            .Where(person => values.Contains(person.TestInt))
            .ToListAsync();
        var expected = PersonData.persons
            .Where(person => values.Contains(person.TestInt));

        var result = RunTest("Empty external Contains matches nothing", actual, expected);
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> MixedConstantTrueAndPredicatePreservesIdentity()
    {
        var predicate = MixedConstantPredicate(constant: true, useOr: false);
        var db = await SetupData();
        var actual = await db.Where(predicate).ToListAsync();
        var expected = PersonData.persons.Where(predicate.Compile());

        var result = RunTest("true AND predicate preserves predicate identity", actual, expected);
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> MixedConstantTrueOrPredicatePreservesIdentity()
    {
        var predicate = MixedConstantPredicate(constant: true, useOr: true);
        var db = await SetupData();
        var actual = await db.Where(predicate).ToListAsync();
        var expected = PersonData.persons.Where(predicate.Compile());

        var result = RunTest("true OR predicate is universal true", actual, expected);
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> DateMemberGreaterThanPreservesWholeDaySemantics()
    {
        var target = new DateTime(2020, 2, 10);
        var records = DateRegressionRows(target);
        var db = await SetupData(records);
        var actual = await db
            .Cursor(person => person.DateOfBirth!.Value.Date > target)
            .ToListAsync();
        var expected = records
            .Where(person => person.DateOfBirth!.Value.Date > target);

        var result = RunTest("Date member greater-than preserves whole-day semantics", actual, expected);
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> DateMemberLessThanOrEqualPreservesWholeDaySemantics()
    {
        var target = new DateTime(2020, 2, 10);
        var records = DateRegressionRows(target);
        var db = await SetupData(records);
        var actual = await db
            .Cursor(person => person.DateOfBirth!.Value.Date <= target)
            .ToListAsync();
        var expected = records
            .Where(person => person.DateOfBirth!.Value.Date <= target);

        var result = RunTest("Date member less-than-or-equal preserves whole-day semantics", actual, expected);
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> NegatedEqualityPreservesSemantics()
    {
        var db = await SetupData();
        var actual = await db
            .Cursor(person => !(person.Name == "Zack"))
            .ToListAsync();
        var expected = PersonData.persons
            .Where(person => !(person.Name == "Zack"));

        var result = RunTest("Negated equality preserves semantics", actual, expected);
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> NegatedInequalityPreservesSemantics()
    {
        var db = await SetupData();
        var actual = await db
            .Cursor(person => !(person.Name != "Zack"))
            .ToListAsync();
        var expected = PersonData.persons
            .Where(person => !(person.Name != "Zack"));

        var result = RunTest("Negated inequality preserves semantics", actual, expected);
        return result.Success ? "OK" : result.Message;
    }

    public async Task<string> StringEqualsMethodPreservesSemantics()
    {
        var db = await SetupData();
        var actual = await db
            .Cursor(person => person.Name.Equals("Zack"))
            .ToListAsync();
        var expected = PersonData.persons
            .Where(person => person.Name.Equals("Zack"));

        var result = RunTest("String Equals method preserves semantics", actual, expected);
        return result.Success ? "OK" : result.Message;
    }

    private static Expression<Func<Person, bool>> MixedConstantPredicate(bool constant, bool useOr)
    {
        var person = Expression.Parameter(typeof(Person), "person");
        var testInt = Expression.Property(person, nameof(Person.TestInt));
        var equalsNine = Expression.Equal(testInt, Expression.Constant(9));
        var constantNode = Expression.Constant(constant);
        var body = useOr
            ? Expression.OrElse(constantNode, equalsNine)
            : Expression.AndAlso(constantNode, equalsNine);
        return Expression.Lambda<Func<Person, bool>>(body, person);
    }

    private static Person RegressionPerson(int id, string name, int age, int testInt, DateTime? dateOfBirth = null) =>
        new()
        {
            _Id = id,
            Name = name,
            _Age = age,
            TestInt = testInt,
            DateOfBirth = dateOfBirth,
            GUIY = Guid.NewGuid()
        };

    private static Person[] DateRegressionRows(DateTime target) =>
    [
        RegressionPerson(1101, "PreviousDay", 1, 1, target.AddDays(-1).AddHours(23)),
        RegressionPerson(1102, "SameDayAfternoon", 2, 2, target.AddHours(14).AddMinutes(30)),
        RegressionPerson(1103, "NextDay", 3, 3, target.AddDays(1))
    ];
}
