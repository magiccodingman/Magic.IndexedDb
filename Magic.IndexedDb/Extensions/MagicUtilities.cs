using Magic.IndexedDb.Models;
using Microsoft.JSInterop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb.Extensions
{
    internal class MagicUtilities : IMagicUtilities
    {
        readonly IJSObjectReference _jsModule;

        /// <summary>
        /// Ctor
        /// </summary>
        /// <param name="dbStore"></param>
        /// <param name="jsRuntime"></param>
        public MagicUtilities(IJSObjectReference jsRuntime)
        {
            this._jsModule = jsRuntime;
        }


        /// <summary>
        /// Returns Mb
        /// </summary>
        /// <returns></returns>
        public Task<QuotaUsage> GetStorageEstimateAsync(CancellationToken cancellationToken = default)
        {
            return new MagicJsInvoke(_jsModule).
                CallJsAsync<QuotaUsage>(Cache.MagicDbJsImportPath,
                IndexedDbFunctions.GET_STORAGE_ESTIMATE, cancellationToken, [])!;
        }
    }
}
