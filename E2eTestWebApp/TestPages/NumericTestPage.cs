using Magic.IndexedDb;
using Microsoft.AspNetCore.Components;
using TestBase.Numeric;

namespace E2eTestWebApp.TestPages;

[Route("/Numeric")]
public sealed class NumericTestPage(IMagicIndexedDb magic, IMagicIndexedDbEncryption encryption) : TestPageBase
{
    public Task<string> RoundTripAndStream() => NumericFixture.RoundTripAndStream(magic);
    public Task<string> IndexedPredicatesAndOrdering() => NumericFixture.IndexedPredicatesAndOrdering(magic);
    public Task<string> ExplicitKeysAndBulkCrud() => NumericFixture.ExplicitKeysAndBulkCrud(magic);
    public Task<string> CanonicalUniqueIndexes() => NumericFixture.CanonicalUniqueIndexes(magic);
    public Task<string> NativeAutoIncrementKeys() => NumericFixture.NativeAutoIncrementKeys(magic);
    public Task<string> ProtectedPublicAccess() => NumericFixture.ProtectedPublicAccess(magic, encryption);
}
