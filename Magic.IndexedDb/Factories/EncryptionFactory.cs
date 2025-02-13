using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Magic.IndexedDb.Factories
{
    public sealed class EncryptionFactory(IndexedDbManager indexDbManager) : IEncryptionFactory
    {
        public Task<string> EncryptAsync(
            string data, string key, 
            CancellationToken cancellationToken = default)
        {
            return indexDbManager.CallJsAsync<string>(
                "encryptString", cancellationToken,
                [data, key]);
        }

        public Task<string> DecryptAsync(
            string encryptedData, string key, 
            CancellationToken cancellationToken = default)
        {
            return indexDbManager.CallJsAsync<string>(
                "decryptString", cancellationToken,
                [encryptedData, key]);
        }
    }
}
