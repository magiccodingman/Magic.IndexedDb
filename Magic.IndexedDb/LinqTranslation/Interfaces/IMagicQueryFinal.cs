using System.Linq.Expressions;

namespace Magic.IndexedDb;

/// <summary>
/// You did it, you've found the end! There's no more IndexDB operations 
/// you can append. Anything else is memory based. So off, venture forth!
/// has the potential of utilizing indexes.
/// </summary>
/// <typeparam name="T"></typeparam>
public interface IMagicQueryFinal<T> : IMagicExecute<T> where T : class
{

    /// <summary>
    /// In memory processing from this point forward! IndexDB 
    /// does not support complex query WHERE statements after appended 
    /// queries like, 'Take', 'Skip', 'OrderBy', and others are utilized.
    /// </summary>
    /// <param name="predicate"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<IEnumerable<T>> WhereAsync(Expression<Func<T, bool>> predicate);
}