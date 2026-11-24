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

    [TestMethod]
    public async Task DictionaryPropertyRoundTripTest()
    {
        var result = await this.RunTestPageMethodAsync(p => p.DictionaryPropertyRoundTrip);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task NumericEnumWhereTest()
    {
        var result = await this.RunTestPageMethodAsync(p => p.NumericEnumWhere);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task NamedEnumWhereTest()
    {
        var result = await this.RunTestPageMethodAsync(p => p.NamedEnumWhere);
        Assert.AreEqual("OK", result);
    }

    [TestMethod]
    public async Task RangeCrudTest() =>
        Assert.AreEqual("OK", await this.RunTestPageMethodAsync(page => page.RangeCrud));

    [TestMethod]
    public async Task WriteContractSemanticsTest() =>
        Assert.AreEqual("OK", await this.RunTestPageMethodAsync(page => page.WriteContractSemantics));

    [TestMethod]
    public async Task ClearAndPopulatedCountTest() =>
        Assert.AreEqual("OK", await this.RunTestPageMethodAsync(page => page.ClearAndPopulatedCount));

    [TestMethod]
    public async Task UniqueConstraintFailureIsRecoverableTest() =>
        Assert.AreEqual("OK", await this.RunTestPageMethodAsync(page => page.UniqueConstraintFailureIsRecoverable));

    [TestMethod]
    public async Task DatabaseLifecycleTest() =>
        Assert.AreEqual("OK", await this.RunTestPageMethodAsync(page => page.DatabaseLifecycle));

    [TestMethod]
    public async Task MultipleDatabaseIsolationTest() =>
        Assert.AreEqual("OK", await this.RunTestPageMethodAsync(page => page.MultipleDatabaseIsolation));

    [TestMethod]
    public async Task ExactMaterializedOrderingTest() =>
        Assert.AreEqual("OK", await this.RunTestPageMethodAsync(page => page.ExactMaterializedOrdering));

    [TestMethod]
    public async Task InMemoryWhereAfterPaginationTest() =>
        Assert.AreEqual("OK", await this.RunTestPageMethodAsync(page => page.InMemoryWhereAfterPagination));

    [TestMethod]
    public async Task CompoundKeyCrudAndQueryTest() =>
        Assert.AreEqual("OK", await this.RunTestPageMethodAsync(page => page.CompoundKeyCrudAndQuery));

    [TestMethod]
    public async Task LargeUnicodeStreamTest() =>
        Assert.AreEqual("OK", await this.RunTestPageMethodAsync(page => page.LargeUnicodeStream));

    [TestMethod]
    public async Task StreamCancellationAndRecoveryTest() =>
        Assert.AreEqual("OK", await this.RunTestPageMethodAsync(page => page.StreamCancellationAndRecovery));

    [TestMethod]
    public async Task ConcurrentStreamsRemainIsolatedTest() =>
        Assert.AreEqual("OK", await this.RunTestPageMethodAsync(page => page.ConcurrentStreamsRemainIsolated));

    [TestMethod]
    public async Task StorageEstimateTest() =>
        Assert.AreEqual("OK", await this.RunTestPageMethodAsync(page => page.StorageEstimate));
}
