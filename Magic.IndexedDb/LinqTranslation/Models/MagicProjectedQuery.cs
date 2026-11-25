using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Magic.IndexedDb;

/// <summary>A deferred Magic query that returns only the selected values across browser interop.</summary>
public sealed class MagicProjectedQuery<TResult>
{
    private readonly Func<CancellationToken, Task<JsonElement[]>> _load;
    private readonly Func<CancellationToken, IAsyncEnumerable<JsonElement>> _stream;
    private readonly Func<JsonElement, TResult> _materialize;

    internal MagicProjectedQuery(Func<CancellationToken, Task<JsonElement[]>> load,
        Func<CancellationToken, IAsyncEnumerable<JsonElement>> stream, Func<JsonElement, TResult> materialize)
    {
        _load = load;
        _stream = stream;
        _materialize = materialize;
    }

    public async Task<List<TResult>> ToListAsync(CancellationToken cancellationToken = default) =>
        (await _load(cancellationToken)).Select(_materialize).ToList();

    public async IAsyncEnumerable<TResult> AsAsyncEnumerable(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var row in _stream(cancellationToken))
            yield return _materialize(row);
    }
}
