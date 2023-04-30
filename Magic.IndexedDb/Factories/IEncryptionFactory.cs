using System.Collections.Generic;
using System.Threading.Tasks;

namespace Magic.IndexedDb
{
    public interface IEncryptionFactory
    {
        Task<string> Encrypt(string data, string key);

        Task<string> Decrypt(string data, string key);
    }
}