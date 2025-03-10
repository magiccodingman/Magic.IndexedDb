using Magic.IndexedDb.Helpers;
using Magic.IndexedDb.Interfaces;
using Magic.IndexedDb.Models;
using Microsoft.JSInterop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb.Extensions
{
    internal class MagicJsInvoke
    {
        private readonly IJSObjectReference _jsModule;

        public MagicJsInvoke(IJSObjectReference jsModule)
        {
            _jsModule = jsModule;
        }

        internal async Task<T?> MagicStreamJsAsync<T>(string functionName, CancellationToken token, params ITypedArgument[] args)
        {
            return await TrueMagicStreamJsAsync<T>(functionName, token, false, args);
        }
        private async Task<T?> TrueMagicStreamJsAsync<T>(string functionName, CancellationToken token, bool isVoid, params ITypedArgument[] args)
        {
            var settings = new MagicJsonSerializationSettings() { UseCamelCase = true };

            var package = new MagicJsPackage
            {
                MethodName = functionName,
                Parameters = MagicSerializationHelper.SerializeObjectsToString(args, settings),
                IsVoid = isVoid
            };

#if DEBUG
            package.IsDebug = true;
#endif

            using var stream = new MemoryStream();
            await using (var writer = new StreamWriter(stream, leaveOpen: true))
            {
                await MagicSerializationHelper.SerializeObjectToStreamAsync(writer, package, settings);
            }

            // ✅ Immediately release reference to `package`
            package = null;
            GC.Collect();
            GC.WaitForPendingFinalizers();

            stream.Position = 0;

            var streamRef = new DotNetStreamReference(stream);

            // Send to JS
            var responseStreamRef = await _jsModule.InvokeAsync<IJSStreamReference>("streamedJsHandler", token, streamRef);

            // 🚀 Convert the stream reference back to JSON in C#
            await using var responseStream = await responseStreamRef.OpenReadStreamAsync(long.MaxValue, token);
            using var reader = new StreamReader(responseStream);

            string jsonResponse = await reader.ReadToEndAsync();
            return MagicSerializationHelper.DeserializeObject<T>(jsonResponse, settings);
        }

        internal async Task MagicVoidStreamJsAsync(string functionName, CancellationToken token, params ITypedArgument[] args)
        {
            await TrueMagicStreamJsAsync<bool>(functionName, token, true, args);
        }
    }


}
