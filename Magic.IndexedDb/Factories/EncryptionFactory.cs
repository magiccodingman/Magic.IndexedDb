using Magic.IndexedDb.Interfaces;
using Magic.IndexedDb.Models;
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
    [Obsolete("The old encryption system no longer works. It's being depreciated.")]
    public sealed class EncryptionFactory(IndexedDbManager indexDbManager) : IEncryptionFactory
    {
        [Obsolete("The old encryption system no longer works. It's being depreciated.")]
        public Task<string> EncryptAsync(
            string data, string key, 
            CancellationToken cancellationToken = default)
        {
            

            return indexDbManager.CallJsAsync<string>(Cache.MagicDbJsImportPath,
                "encryptString", cancellationToken,
                new ITypedArgument[] { new TypedArgument<string>(data), new TypedArgument<string>(key) }
                );
        }

        [Obsolete("The old encryption system no longer works. It's being depreciated.")]
        public Task<string> DecryptAsync(
            string encryptedData, string key, 
            CancellationToken cancellationToken = default)
        {
            return indexDbManager.CallJsAsync<string>(Cache.MagicDbJsImportPath,
                "decryptString", cancellationToken,
                new ITypedArgument[] { new TypedArgument<string>(encryptedData), new TypedArgument<string>(key) }
                );
        }
    }
}
