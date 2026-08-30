using Magic.IndexedDb.Models;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Magic.IndexedDb.LinqTranslation.Models;
using Magic.IndexedDb.Extensions;

namespace Magic.IndexedDb.LinqTranslation.Extensions;

internal class MagicQueryExtensions<T> :
    IMagicQueryPaginationTake<T>, IMagicQueryOrderable<T>,
    IMagicQueryOrderableTable<T>, IMagicQueryFinal<T>
    where T : class
{
    public MagicQuery<T> MagicQuery { get; set; }

    public MagicQueryExtensions(MagicQuery<T> _magicQuery)
    {
        MagicQuery = _magicQuery;

    }

    /// <summary>
    /// Progressively streams query results across the Blazor/JavaScript boundary.
    /// Progressive streaming is distinct from the final materialized ordering contract;
    /// callers that require the completed ordered sequence should use <see cref="ToListAsync"/>.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the streaming enumeration.</param>
    /// <returns>The progressive query stream.</returns>
    public async IAsyncEnumerable<T> AsAsyncEnumerable([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in MagicQuery.Manager.LinqToIndexedDbYield<T>(nestedOrFilter, MagicQuery, cancellationToken))
        {
            if (item is not null) // Ensure non-null items
            {
                yield return item;
            }
        }
    }

    /// <summary>
    /// Materializes the current query and then applies the supplied predicate in memory.
    /// LINQ filtering preserves the enumeration order of the materialized sequence.
    /// </summary>
    /// <param name="predicate">Predicate to apply after materialization.</param>
    /// <returns>The filtered materialized sequence.</returns>
    public async Task<IEnumerable<T>> WhereAsync(
        Expression<Func<T, bool>> predicate)
    {
        var items = await ToListAsync();
        return items.Where(predicate.Compile()); // Apply predicate after materialization
    }

    private FilterNode nestedOrFilter { get => GetCollectedBinaryJsonExpressions(); }

    /// <summary>
    /// Materializes the query. When ordering operators are present, the returned list
    /// preserves the resulting query order after indexed/cursor execution and refetch.
    /// </summary>
    /// <returns>The materialized query results.</returns>
    public async Task<List<T>> ToListAsync()
    {
        return (await MagicQuery.Manager.LinqToIndexedDb<T>(
            nestedOrFilter, MagicQuery, default))?.ToList() ?? new List<T>();
    }

    public IMagicQueryPaginationTake<T> Take(int amount)
    {
        return new MagicQueryExtensions<T>(SharedQueryExtensions.Take(this.MagicQuery, amount));
    }

    public async Task<T?> FirstOrDefaultAsync()
    {
        var _MagicQuery = new MagicQuery<T>(this.MagicQuery);
        StoredMagicQuery smq = new StoredMagicQuery();
        smq.additionFunction = MagicQueryFunctions.First;
        _MagicQuery.StoredMagicQueries.Add(smq);

        var items = await new MagicQueryExtensions<T>(_MagicQuery).ToListAsync();
        return items.FirstOrDefault();
    }

    public async Task<T?> LastOrDefaultAsync()
    {
        var _MagicQuery = new MagicQuery<T>(this.MagicQuery);
        StoredMagicQuery smq = new StoredMagicQuery();
        smq.additionFunction = MagicQueryFunctions.Last;
        _MagicQuery.StoredMagicQueries.Add(smq);
        var items = await new MagicQueryExtensions<T>(_MagicQuery).ToListAsync();
        return items.LastOrDefault();
    }

    public async Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate)
    {
        var _MagicQuery = new MagicQuery<T>(this.MagicQuery);
        _MagicQuery.Predicates.Add(predicate);
        return await new MagicQueryExtensions<T>(_MagicQuery).FirstOrDefaultAsync();
    }

    public async Task<T?> LastOrDefaultAsync(Expression<Func<T, bool>> predicate)
    {
        var _MagicQuery = new MagicQuery<T>(this.MagicQuery);
        _MagicQuery.Predicates.Add(predicate);
        return await new MagicQueryExtensions<T>(_MagicQuery).LastOrDefaultAsync();
    }

    public IMagicQueryFinal<T> TakeLast(int amount)
    {
        return new MagicQueryExtensions<T>(
            SharedQueryExtensions.TakeLast(this.MagicQuery, amount)
        );
    }

    public IMagicQueryFinal<T> Skip(int amount)
    {
        return new MagicQueryExtensions<T>(
            SharedQueryExtensions.Skip(this.MagicQuery, amount)
        );
    }

    public IMagicQueryOrderableTable<T> OrderBy(Expression<Func<T, object>> predicate)
    {
        return new MagicQueryExtensions<T>(
            SharedQueryExtensions.OrderBy(this.MagicQuery, predicate)
        );
    }

    public IMagicQueryOrderableTable<T> OrderByDescending(Expression<Func<T, object>> predicate)
    {
        return new MagicQueryExtensions<T>(
            SharedQueryExtensions.OrderByDescending(this.MagicQuery, predicate)
        );
    }

    private FilterNode GetCollectedBinaryJsonExpressions()
    {
        Expression<Func<T, bool>> preprocessedPredicate = PreprocessPredicate();


        var builder = new UniversalExpressionBuilder<T>(preprocessedPredicate);
        var result = builder.Build();
        return result;
    }

    private bool IsUniversalFalse(Expression<Func<T, bool>> predicate)
    {
        return predicate.Body is ConstantExpression constant && constant.Value is bool value && !value;
    }

    private Expression<Func<T, bool>> PreprocessPredicate()
    {
        Expression<Func<T, bool>> predicate = MagicQuery.GetFinalPredicate();
        var visitor = new PredicateVisitor<T>();
        var newExpression = visitor.Visit(predicate.Body);

        return Expression.Lambda<Func<T, bool>>(newExpression, predicate.Parameters);
    }
}
