using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Magic.IndexedDb
{
    public class EncryptionFactory: IEncryptionFactory
    {
        readonly IJSRuntime _jsRuntime;
        readonly IndexedDbManager _indexDbManager;

        public EncryptionFactory(IJSRuntime jsRuntime, IndexedDbManager indexDbManager)
        {
            _jsRuntime = jsRuntime;
            _indexDbManager = indexDbManager;
        }

        public async Task<string> Encrypt(string data, string key)
        {
            var mod = await _indexDbManager.GetModule(_jsRuntime);
            string encryptedData = await mod.InvokeAsync<string>("encryptString", new[] { data, key });
            return encryptedData;
        }

        public async Task<string> Decrypt(string encryptedData, string key)
        {
            var mod = await _indexDbManager.GetModule(_jsRuntime);
            string decryptedData = await mod.InvokeAsync<string>("decryptString", new[] { encryptedData, key });
            return decryptedData;
        }
    }
}
