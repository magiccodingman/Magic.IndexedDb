﻿using Magic.IndexedDb.Models;
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
using Magic.IndexedDb.LinqTranslation.Models;

namespace Magic.IndexedDb.LinqTranslation.Extensions
{
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
        /// EXPERIMENTAL FEATURE: 
        /// True IAsyncEnumerable between C# Blazor and JS. How?! 
        /// It's god damn magic! IMPORTANT NOTE: the order in which items 
        /// are returned may not be the order you specified. Your ordering 
        /// is properly utilized inside of IndexDB, but the returned process 
        /// due to IndexDB limitations can't return the same order. Please re-apply 
        /// your desired ordering after your results are brought back if order is important.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
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
        /// The order you apply does get applied correctly in the query, 
        /// but the returned results will not be in the same order. 
        /// If order matters, you must apply the order again on return. 
        /// This is a fundemental limitation of IndexDB. 
        /// </summary>
        /// <param name="predicate"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<IEnumerable<T>> WhereAsync(
    Expression<Func<T, bool>> predicate)
        {
            var items = await ToListAsync();
            return items.Where(predicate.Compile()); // Apply predicate after materialization
        }

        private FilterNode nestedOrFilter { get => GetCollectedBinaryJsonExpressions(); }

        /// <summary>
        /// The order you apply does get applied correctly in the query, 
        /// but the returned results will not be in the same order. 
        /// If order matters, you must apply the order again on return. 
        /// This is a fundemental limitation of IndexDB. 
        /// </summary>
        /// <returns></returns>
        public async Task<List<T>> ToListAsync()
        {
            return (await MagicQuery.Manager.LinqToIndexedDb<T>(
                nestedOrFilter, MagicQuery, default))?.ToList() ?? new List<T>();
        }

        public IMagicQueryPaginationTake<T> Take(int amount)
        {
            var _MagicQuery = new MagicQuery<T>(this.MagicQuery);
            StoredMagicQuery smq = new StoredMagicQuery();
            smq.additionFunction = MagicQueryFunctions.Take;
            smq.intValue = amount;
            _MagicQuery.StoredMagicQueries.Add(smq);
            return new MagicQueryExtensions<T>(_MagicQuery);
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
            var _MagicQuery = new MagicQuery<T>(this.MagicQuery);
            StoredMagicQuery smq = new StoredMagicQuery();
            smq.additionFunction = MagicQueryFunctions.Take_Last;
            smq.intValue = amount;
            _MagicQuery.StoredMagicQueries.Add(smq);
            return new MagicQueryExtensions<T>(_MagicQuery);
        }

        public IMagicQueryFinal<T> Skip(int amount)
        {
            var _MagicQuery = new MagicQuery<T>(this.MagicQuery);
            StoredMagicQuery smq = new StoredMagicQuery();
            smq.additionFunction = MagicQueryFunctions.Skip;
            smq.intValue = amount;
            _MagicQuery.StoredMagicQueries.Add(smq);
            return new MagicQueryExtensions<T>(_MagicQuery);
        }

        // Not currently available in Dexie version 1,2, or 3
        public IMagicQueryOrderableTable<T> OrderBy(Expression<Func<T, object>> predicate)
        {
            var memberExpression = GetMemberExpressionFromLambda(predicate);
            var propertyInfo = memberExpression.Member as PropertyInfo;

            if (propertyInfo == null)
            {
                throw new ArgumentException("The expression must represent a single property access.");
            }
            MagicPropertyEntry mpe = PropertyMappingCache.GetPropertyEntry<T>(propertyInfo);


            if (!mpe.PrimaryKey && !mpe.Indexed && !mpe.UniqueIndex)
            {
                //throw new ArgumentException("The selected property must have either MagicIndexAttribute, MagicUniqueIndexAttribute, or MagicPrimaryKeyAttribute.");
            }

            var _MagicQuery = new MagicQuery<T>(this.MagicQuery);
            StoredMagicQuery smq = new StoredMagicQuery();
            smq.additionFunction = MagicQueryFunctions.Order_By;
            smq.property = mpe.JsPropertyName;
            _MagicQuery.StoredMagicQueries.Add(smq);
            return new MagicQueryExtensions<T>(_MagicQuery);
        }

        // Not currently available in Dexie version 1,2, or 3
        public IMagicQueryOrderableTable<T> OrderByDescending(Expression<Func<T, object>> predicate)
        {
            var memberExpression = GetMemberExpressionFromLambda(predicate);
            var propertyInfo = memberExpression.Member as PropertyInfo;

            if (propertyInfo == null)
            {
                throw new ArgumentException("The expression must represent a single property access.");
            }

            var _MagicQuery = new MagicQuery<T>(this.MagicQuery);
            StoredMagicQuery smq = new StoredMagicQuery();
            smq.additionFunction = MagicQueryFunctions.Order_By_Descending;

            // this process could be much more performant
            smq.property = PropertyMappingCache.GetJsPropertyName<T>(propertyInfo);
            _MagicQuery.StoredMagicQueries.Add(smq);
            return new MagicQueryExtensions<T>(_MagicQuery);
        }

        private MemberExpression GetMemberExpressionFromLambda(Expression<Func<T, object>> expression)
        {
            if (expression.Body is MemberExpression)
            {
                return (MemberExpression)expression.Body;
            }
            else if (expression.Body is UnaryExpression && ((UnaryExpression)expression.Body).Operand is MemberExpression)
            {
                return (MemberExpression)((UnaryExpression)expression.Body).Operand;
            }
            else
            {
                throw new ArgumentException("The expression must represent a single property access.");
            }
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
}
