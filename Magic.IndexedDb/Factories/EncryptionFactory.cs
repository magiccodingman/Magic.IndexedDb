namespace Magic.IndexedDb.Factories
{
    public sealed class EncryptionFactory(IndexedDbManager indexDbManager) : IEncryptionFactory
    {
        public Task<string> EncryptAsync(
            string data, string? key,
            CancellationToken cancellationToken = default)
        {
            return indexDbManager.CallJsAsync<string>(
                "encryptString", cancellationToken,
                [data, key]);
        }

        public Task<string> DecryptAsync(
            string encryptedData, string? key,
            CancellationToken cancellationToken = default)
        {
            return indexDbManager.CallJsAsync<string>(
                "decryptString", cancellationToken,
                [encryptedData, key]);
        }
    }
}
