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

        public EncryptionFactory(IJSRuntime jsRuntime)
        {
            _jsRuntime = jsRuntime;
        }

        public async Task<string> Encrypt(string data, string key)
        {
            string encryptedData = await _jsRuntime.InvokeAsync<string>("encryptString", new[] { data, key });
            return encryptedData;
        }

        public async Task<string> Decrypt(string encryptedData, string key)
        {
            string decryptedData = await _jsRuntime.InvokeAsync<string>("decryptString", new[] { encryptedData, key });
            return decryptedData;
        }
    }
}
