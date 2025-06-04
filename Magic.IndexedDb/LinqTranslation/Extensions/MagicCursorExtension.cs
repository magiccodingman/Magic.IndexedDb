using Magic.IndexedDb.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Magic.IndexedDb.LinqTranslation.Interfaces;
using Magic.IndexedDb.Helpers;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Collections;
using Magic.IndexedDb.Models.UniversalOperations;
using Magic.IndexedDb.Extensions;

namespace Magic.IndexedDb.LinqTranslation.Extensions;

internal class MagicCursorExtension<T> : IMagicCursorStage<T>, IMagicCursorSkip<T>, IMagicCursorPaginationTake<T>, IMagicCursorFinal<T> where T : class
{
    public MagicQuery<T> MagicQuery { get; set; }
    public MagicCursorExtension(MagicQuery<T> _magicQuery)
    {
        MagicQuery = _magicQuery;

    }

    public IMagicCursorPaginationTake<T> Take(int amount)
    {
        return new MagicCursorExtension<T>(SharedQueryExtensions.Take(this.MagicQuery, amount));
    }

    public IMagicCursorPaginationTake<T> TakeLast(int amount)
    {
        return new MagicCursorExtension<T>(
            SharedQueryExtensions.TakeLast(this.MagicQuery, amount)
        );
    }
    /*public IMagicCursorSkip<T> Skip(int amount)
        => new MagicCursorExtension<T>(MagicQuery).Skip(amount);*/
    public IMagicCursorSkip<T> Skip(int amount)
    {
        return new MagicCursorExtension<T>(
            SharedQueryExtensions.Skip(this.MagicQuery, amount)
        );
    }

    public IMagicCursorStage<T> OrderBy(Expression<Func<T, object>> predicate)
    {
        return new MagicCursorExtension<T>(
            SharedQueryExtensions.OrderBy(this.MagicQuery, predicate)
        );
    }

    public IMagicCursorStage<T> OrderByDescending(Expression<Func<T, object>> predicate)
    {
        return new MagicCursorExtension<T>(
            SharedQueryExtensions.OrderByDescending(this.MagicQuery, predicate)
        );
    }

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