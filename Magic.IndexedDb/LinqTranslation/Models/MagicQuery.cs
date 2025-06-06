using Magic.IndexedDb.LinqTranslation.Extensions;
using Magic.IndexedDb.LinqTranslation.Models;
using Magic.IndexedDb.Models;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Magic.IndexedDb;

internal class MagicQuery<T> : IMagicQuery<T>, IMagicQueryStaging<T> where T : class
{
    /// <summary>
    /// table name
    /// </summary>
    public string SchemaName { get; }

    /// <summary>
    /// database name
    /// </summary>
    public string DatabaseName { get; }
    internal IndexedDbManager Manager { get; }
    internal List<StoredMagicQuery> StoredMagicQueries { get; set; } = new List<StoredMagicQuery>();
    internal bool ForceCursorMode { get; set; } = false;

    internal bool ResultsUnique { get; set; } = true;
    internal List<Expression<Func<T, bool>>> Predicates { get; } = new List<Expression<Func<T, bool>>>();

    internal MagicQuery(string databaseName, string schemaName, IndexedDbManager manager)
    {
        Manager = manager;
        SchemaName = schemaName;
        DatabaseName = databaseName;
    }

    public MagicQuery(MagicQuery<T> _MagicQuery)
    {
        SchemaName = _MagicQuery.SchemaName;  // Keep reference
        DatabaseName = _MagicQuery.DatabaseName;  // Keep reference
        Manager = _MagicQuery.Manager;        // Keep reference
        StoredMagicQueries = new List<StoredMagicQuery>(_MagicQuery.StoredMagicQueries); // Deep copy
        ResultsUnique = _MagicQuery.ResultsUnique;
        Predicates = new List<Expression<Func<T, bool>>>(_MagicQuery.Predicates); // Deep copy
    }

    public IMagicQueryStaging<T> Where(Expression<Func<T, bool>> predicate)
    {
        var _MagicQuery = new MagicQuery<T>(this);
        _MagicQuery.Predicates.Add(predicate);
        return _MagicQuery; // Enable method chaining
    }

    internal Expression<Func<T, bool>> GetFinalPredicate()
    {
        if (Predicates.Count == 0)
            return x => true; // Default to always-true if no predicates exist

        Expression<Func<T, bool>> finalPredicate = Predicates[0];

        for (int i = 1; i < Predicates.Count; i++)
        {
            finalPredicate = CombineExpressions(finalPredicate, Predicates[i]);
        }

        return finalPredicate;
    }

    private Expression<Func<T, bool>> CombineExpressions(Expression<Func<T, bool>> first, Expression<Func<T, bool>> second)
    {
        var parameter = Expression.Parameter(typeof(T), "x");

        var combinedBody = Expression.AndAlso(
            new PredicateVisitor<T>().Visit(first.Body),
            new PredicateVisitor<T>().Visit(second.Body)
        );

        return Expression.Lambda<Func<T, bool>>(combinedBody, parameter);
    }


    public IMagicQueryPaginationTake<T> Take(int amount)
        => new MagicQueryExtensions<T>(this).Take(amount);

    public IMagicQueryFinal<T> TakeLast(int amount)
        => new MagicQueryExtensions<T>(this).TakeLast(amount);

    public IMagicQueryFinal<T> Skip(int amount)
        => new MagicQueryExtensions<T>(this).Skip(amount);

    public IMagicQueryOrderableTable<T> OrderBy(Expression<Func<T, object>> predicate)
        => new MagicQueryExtensions<T>(this).OrderBy(predicate);

    public IMagicQueryOrderableTable<T> OrderByDescending(Expression<Func<T, object>> predicate)
        => new MagicQueryExtensions<T>(this).OrderByDescending(predicate);

    /*IMagicQueryOrderable<T> IMagicQueryStaging<T>.OrderBy(Expression<Func<T, object>> predicate)
        => OrderBy(predicate);

    IMagicQueryOrderable<T> IMagicQueryStaging<T>.OrderByDescending(Expression<Func<T, object>> predicate)
        => OrderByDescending(predicate);*/

    public IMagicCursor<T> Cursor(Expression<Func<T, bool>> predicate)
        => new MagicCursor<T>(this).Cursor(predicate);

    public async Task<T?> FirstOrDefaultAsync()
        => await new MagicQueryExtensions<T>(this).FirstOrDefaultAsync();

    public async Task<T?> LastOrDefaultAsync()
        => await new MagicQueryExtensions<T>(this).LastOrDefaultAsync();

    public async Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate)
        => await new MagicQueryExtensions<T>(this).FirstOrDefaultAsync(predicate);

    public async Task<T?> LastOrDefaultAsync(Expression<Func<T, bool>> predicate)
        => await new MagicQueryExtensions<T>(this).LastOrDefaultAsync(predicate);

    public async IAsyncEnumerable<T> AsAsyncEnumerable(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in new MagicQueryExtensions<T>(this).AsAsyncEnumerable(cancellationToken)
                           .WithCancellation(cancellationToken))
        {
            yield return item;
        }
    }

    public async Task<List<T>> ToListAsync()
        => await new MagicQueryExtensions<T>(this).ToListAsync();

    public async Task<int> CountAsync()
    {
        return await Manager.CountEntireTableAsync<int>(SchemaName, DatabaseName);
    }

    public async Task AddRangeAsync(
        IEnumerable<T> records, CancellationToken cancellationToken = default)
    {
        await Manager.BulkAddRecordAsync(SchemaName, DatabaseName, records, cancellationToken);
    }

    public async Task<int> UpdateAsync(T item, CancellationToken cancellationToken = default)
    {
        return await Manager.UpdateAsync(item, DatabaseName, cancellationToken);
    }

    public async Task<int> UpdateRangeAsync(
        IEnumerable<T> items,
        CancellationToken cancellationToken = default)
    {
        return await Manager.UpdateRangeAsync(items, DatabaseName, cancellationToken);
    }

    public async Task DeleteAsync(T item, CancellationToken cancellationToken = default)
    {
        await Manager.DeleteAsync(item, DatabaseName, cancellationToken);
    }

    public async Task<int> DeleteRangeAsync(
        IEnumerable<T> items,
        CancellationToken cancellationToken = default)
    {
        return await Manager.DeleteRangeAsync(items, DatabaseName, cancellationToken);
    }

    public async Task AddAsync(T record, CancellationToken cancellationToken = default)
    {
        _ = await Manager.AddAsync<T, JsonElement>(record, DatabaseName, cancellationToken);
    }

    public async Task ClearTable()
    {
        await Manager.ClearTableAsync(SchemaName, DatabaseName);
    }
}