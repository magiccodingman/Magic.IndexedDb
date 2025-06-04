using Magic.IndexedDb.Models;
using Microsoft.JSInterop;

namespace Magic.IndexedDb.Extensions;

internal class MagicUtilities : IMagicUtilities
{
    readonly IJSObjectReference _jsModule;
    private readonly long _jsMessageSizeBytes;

    /// <summary>
    /// Ctor
    /// </summary>
    /// <param name="dbStore"></param>
    /// <param name="jsRuntime"></param>
    public MagicUtilities(IJSObjectReference jsRuntime, long jsMessageSizeBytes)
    {
        this._jsModule = jsRuntime;
        _jsMessageSizeBytes = jsMessageSizeBytes;
    }

    /// <summary>
    /// Returns Mb
    /// </summary>
    /// <returns></returns>
    public Task<QuotaUsage> GetStorageEstimateAsync(CancellationToken cancellationToken = default)
    {
        return new MagicJsInvoke(_jsModule, _jsMessageSizeBytes).
            CallJsAsync<QuotaUsage>(Cache.MagicDbJsImportPath,
                IndexedDbFunctions.GET_STORAGE_ESTIMATE, cancellationToken, [])!;
    }
}