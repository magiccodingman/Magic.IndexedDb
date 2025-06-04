using Magic.IndexedDb.LinqTranslation.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb.LinqTranslation.Models;

internal class MagicCursor<T> : IMagicCursor<T> where T : class
{
    public MagicQuery<T> MagicQuery { get; set; }
    public MagicCursor(MagicQuery<T> _magicQuery)
    {
        _magicQuery.ForceCursorMode = true;
        MagicQuery = _magicQuery;

    }

    public IMagicCursor<T> Cursor(Expression<Func<T, bool>> predicate)
    {

        var _MagicQuery = new MagicQuery<T>(MagicQuery);
        _MagicQuery.Predicates.Add(predicate);
        return new MagicCursor<T>(_MagicQuery); // Enable method chaining
    }

    public IMagicCursorPaginationTake<T> Take(int amount)
        => new MagicCursorExtension<T>(MagicQuery).Take(amount);

    public IMagicCursorPaginationTake<T> TakeLast(int amount)
        => new MagicCursorExtension<T>(MagicQuery).TakeLast(amount);

    public IMagicCursorSkip<T> Skip(int amount)
        => new MagicCursorExtension<T>(MagicQuery).Skip(amount);

    public IMagicCursorStage<T> OrderBy(Expression<Func<T, object>> predicate)
        => new MagicCursorExtension<T>(MagicQuery).OrderBy(predicate);

    public IMagicCursorStage<T> OrderByDescending(Expression<Func<T, object>> predicate)
        => new MagicCursorExtension<T>(MagicQuery).OrderByDescending(predicate);

    public async IAsyncEnumerable<T> AsAsyncEnumerable(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in new MagicQueryExtensions<T>(MagicQuery).AsAsyncEnumerable(cancellationToken)
                           .WithCancellation(cancellationToken))
        {
            yield return item;
        }
    }

    public async Task<List<T>> ToListAsync()
        => await new MagicQueryExtensions<T>(MagicQuery).ToListAsync();

    public async Task<T?> FirstOrDefaultAsync()
        => await new MagicQueryExtensions<T>(MagicQuery).FirstOrDefaultAsync();

    public async Task<T?> LastOrDefaultAsync()
        => await new MagicQueryExtensions<T>(MagicQuery).LastOrDefaultAsync();
}