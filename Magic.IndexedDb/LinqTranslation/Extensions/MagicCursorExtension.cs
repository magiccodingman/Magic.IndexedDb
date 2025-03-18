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

namespace Magic.IndexedDb.LinqTranslation.Extensions
{
    internal class MagicCursorExtension<T> : IMagicCursorStage<T> where T : class
    {
        public MagicQuery<T> MagicQuery { get; set; }
        public MagicCursorExtension(MagicQuery<T> _magicQuery)
        {
            MagicQuery = _magicQuery;

        }

        public IMagicCursorStage<T> Take(int amount)
           => new MagicCursorExtension<T>(MagicQuery).Take(amount);

        public IMagicCursorStage<T> TakeLast(int amount)
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
}
