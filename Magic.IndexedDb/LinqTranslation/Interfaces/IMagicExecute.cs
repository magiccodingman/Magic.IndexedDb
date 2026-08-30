namespace Magic.IndexedDb;

public interface IMagicExecute<T> where T : class
{
    /// <summary>
    /// Executes the query as a progressive async stream. Streaming and fully materialized
    /// sequence semantics are separate contracts; callers that require the final ordered
    /// materialized sequence should use <see cref="ToListAsync"/>.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    IAsyncEnumerable<T> AsAsyncEnumerable(CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes and materializes the query. When an ordering operator is applied, the
    /// returned list preserves the resulting query order.
    /// </summary>
    /// <returns></returns>
    Task<List<T>> ToListAsync();
}
