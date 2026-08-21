using TestBase.Helpers;
using TestBase.Models;

namespace Magic.IndexedDb.UnitTests;

[TestClass]
public sealed class TestValidatorTests
{
    [TestMethod]
    public void OrderedComparison_RejectsTheRightRowsInTheWrongOrder()
    {
        Person[] expected =
        [
            new() { _Id = 1, Name = "first" },
            new() { _Id = 2, Name = "second" }
        ];
        Person[] reversed = [expected[1], expected[0]];

        var ordered = TestValidator.ValidateLists(expected, reversed, ordered: true);
        var unordered = TestValidator.ValidateLists(expected, reversed);

        Assert.IsFalse(ordered.Success);
        StringAssert.Contains(ordered.Message, "Position 0");
        Assert.IsTrue(unordered.Success, unordered.Message);
    }

    [TestMethod]
    public void Comparison_ReportsPropertyDifferencesForMatchingKeys()
    {
        Person[] expected = [new() { _Id = 1, Name = "expected" }];
        Person[] actual = [new() { _Id = 1, Name = "actual" }];

        var result = TestValidator.ValidateLists(expected, actual);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message, nameof(Person.Name));
    }
}
