using System.Linq.Expressions;

namespace Magic.IndexedDb;

/// <summary>
/// Direct ordering applied to a table without where statements.
/// </summary>
public interface IMagicQueryOrderableTable<T> : IMagicQueryOrderable<T>, IMagicExecute<T>
    where T : class
{
    Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate);
    Task<T?> LastOrDefaultAsync(Expression<Func<T, bool>> predicate);
}