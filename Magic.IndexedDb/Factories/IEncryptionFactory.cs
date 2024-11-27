using System.Collections.Generic;
using System.Threading.Tasks;

namespace Magic.IndexedDb
{
    public interface IEncryptionFactory
    {
        Task<string> EncryptAsync(
            string data, string key, 
            CancellationToken cancellationToken = default);

        Task<string> DecryptAsync(
            string data, string key, 
            CancellationToken cancellationToken = default);
    }
}