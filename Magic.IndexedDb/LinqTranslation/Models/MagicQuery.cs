using Magic.IndexedDb.Helpers;
using Magic.IndexedDb.LinqTranslation.Extensions;
using Magic.IndexedDb.LinqTranslation.Interfaces;
using Magic.IndexedDb.LinqTranslation.Models;
using Magic.IndexedDb.Models;
using Magic.IndexedDb.SchemaAnnotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb
{
    internal class MagicQuery<T> : IMagicQuery<T>, IMagicQueryStaging<T> where T : class
    {
        internal string SchemaName { get; }
        internal IndexedDbManager Manager { get; }
        internal List<StoredMagicQuery> StoredMagicQueries { get; set; } = new List<StoredMagicQuery>();

        internal bool ResultsUnique { get; set; } = true;
        internal List<Expression<Func<T, bool>>> Predicates { get; } = new List<Expression<Func<T, bool>>>();

        public MagicQuery(string schemaName, IndexedDbManager manager)
        {
            Manager = manager;
            SchemaName = schemaName;
        }

        public MagicQuery(MagicQuery<T> _MagicQuery)
        {
            SchemaName = _MagicQuery.SchemaName;  // Keep reference
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

        public IMagicQueryOrderable<T> OrderBy(Expression<Func<T, object>> predicate)
            =>  new MagicQueryExtensions<T>(this).OrderBy(predicate);

        public IMagicQueryOrderable<T> OrderByDescending(Expression<Func<T, object>> predicate)
            => new MagicQueryExtensions<T>(this).OrderByDescending(predicate);

        public IMagicCursor<T> Cursor(Expression<Func<T, bool>> predicate)
            => new MagicCursor<T>(this).Cursor(predicate);

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

    }
}
