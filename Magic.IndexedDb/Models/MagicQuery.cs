using Magic.IndexedDb.Helpers;
using Magic.IndexedDb.SchemaAnnotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb.Models
{
    public class MagicQuery<T> where T : class
    {
        public string SchemaName { get; }
        public List<string> JsonQueries { get; }
        public IndexedDbManager Manager { get; }

        public MagicQuery(string schemaName, IndexedDbManager manager)
        {
            Manager = manager;
            SchemaName = schemaName;
            JsonQueries = new List<string>();
        }

        public List<StoredMagicQuery> storedMagicQueries { get; set; } = new List<StoredMagicQuery>();

        public bool ResultsUnique { get; set; } = true;

        /// <summary>
        /// Return a list of items in which the items do not have to be unique. Therefore, you can get 
        /// duplicate instances of an object depending on how you write your query.
        /// </summary>
        /// <param name="amount"></param>
        /// <returns></returns>
        public MagicQuery<T> ResultsNotUnique()
        {
            ResultsUnique = false;
            return this;
        }

        public MagicQuery<T> Take(int amount)
        {
            StoredMagicQuery smq = new StoredMagicQuery();
            smq.Name = MagicQueryFunctions.Take;
            smq.IntValue = amount;
            storedMagicQueries.Add(smq);
            return this;
        }

        public MagicQuery<T> TakeLast(int amount)
        {
            StoredMagicQuery smq = new StoredMagicQuery();
            smq.Name = MagicQueryFunctions.Take_Last;
            smq.IntValue = amount;
            storedMagicQueries.Add(smq);
            return this;
        }

        public MagicQuery<T> Skip(int amount)
        {
            StoredMagicQuery smq = new StoredMagicQuery();
            smq.Name = MagicQueryFunctions.Skip;
            smq.IntValue = amount;
            storedMagicQueries.Add(smq);
            return this;
        }

        //public MagicQuery<T> Reverse()
        //{
        //    StoredMagicQuery smq = new StoredMagicQuery();
        //    smq.Name = MagicQueryFunctions.Reverse;
        //    storedMagicQueries.Add(smq);
        //    return this;
        //}

        // Not yet working
        private MagicQuery<T> First()
        {
            StoredMagicQuery smq = new StoredMagicQuery();
            smq.Name = MagicQueryFunctions.First;
            storedMagicQueries.Add(smq);
            return this;
        }

        // Not yet working
        private MagicQuery<T> Last()
        {
            StoredMagicQuery smq = new StoredMagicQuery();
            smq.Name = MagicQueryFunctions.Last;
            storedMagicQueries.Add(smq);
            return this;
        }

        public async Task<IEnumerable<T>> Execute()
        {
            return await Manager.WhereV2Async<T>(SchemaName, JsonQueries, this, default) ?? Enumerable.Empty<T>();
        }

        public async Task<int> Count()
        {
            var result = await Manager.WhereV2Async<T>(SchemaName, JsonQueries, this, default);
            int num = result?.Count() ?? 0;
            return num;
        }


        // Not currently available in Dexie version 1,2, or 3
        public MagicQuery<T> OrderBy(Expression<Func<T, object>> predicate)
        {
            var memberExpression = GetMemberExpressionFromLambda(predicate);
            var propertyInfo = memberExpression.Member as PropertyInfo;

            if (propertyInfo == null)
            {
                throw new ArgumentException("The expression must represent a single property access.");
            }

            var indexDbAttr = propertyInfo.GetCustomAttribute<MagicIndexAttribute>();
            var uniqueIndexDbAttr = propertyInfo.GetCustomAttribute<MagicUniqueIndexAttribute>();
            var primaryKeyDbAttr = propertyInfo.GetCustomAttribute<MagicPrimaryKeyAttribute>();

            if (indexDbAttr == null && uniqueIndexDbAttr == null && primaryKeyDbAttr == null)
            {
                throw new ArgumentException("The selected property must have either MagicIndexAttribute, MagicUniqueIndexAttribute, or MagicPrimaryKeyAttribute.");
            }

            string? columnName = null;

            if (indexDbAttr != null)
                columnName = propertyInfo.GetPropertyColumnName<MagicIndexAttribute>();
            else if (primaryKeyDbAttr != null)
                columnName = propertyInfo.GetPropertyColumnName<MagicPrimaryKeyAttribute>();
            else if (uniqueIndexDbAttr != null)
                columnName = propertyInfo.GetPropertyColumnName<MagicUniqueIndexAttribute>();

            StoredMagicQuery smq = new StoredMagicQuery();
            smq.Name = MagicQueryFunctions.Order_By;
            smq.StringValue = columnName;
            storedMagicQueries.Add(smq);
            return this;
        }

        // Not currently available in Dexie version 1,2, or 3
        public MagicQuery<T> OrderByDescending(Expression<Func<T, object>> predicate)
        {
            var memberExpression = GetMemberExpressionFromLambda(predicate);
            var propertyInfo = memberExpression.Member as PropertyInfo;

            if (propertyInfo == null)
            {
                throw new ArgumentException("The expression must represent a single property access.");
            }

            var indexDbAttr = propertyInfo.GetCustomAttribute<MagicIndexAttribute>();
            var uniqueIndexDbAttr = propertyInfo.GetCustomAttribute<MagicUniqueIndexAttribute>();
            var primaryKeyDbAttr = propertyInfo.GetCustomAttribute<MagicPrimaryKeyAttribute>();

            if (indexDbAttr == null && uniqueIndexDbAttr == null && primaryKeyDbAttr == null)
            {
                throw new ArgumentException("The selected property must have either MagicIndexAttribute, MagicUniqueIndexAttribute, or MagicPrimaryKeyAttribute.");
            }

            string? columnName = null;

            if (indexDbAttr != null)
                columnName = propertyInfo.GetPropertyColumnName<MagicIndexAttribute>();
            else if (primaryKeyDbAttr != null)
                columnName = propertyInfo.GetPropertyColumnName<MagicPrimaryKeyAttribute>();
            else if (uniqueIndexDbAttr != null)
                columnName = propertyInfo.GetPropertyColumnName<MagicUniqueIndexAttribute>();

            StoredMagicQuery smq = new StoredMagicQuery();
            smq.Name = MagicQueryFunctions.Order_By_Descending;
            smq.StringValue = columnName;
            storedMagicQueries.Add(smq);
            return this;
        }

#pragma warning disable CS0693 // Mark members as static
        private MemberExpression GetMemberExpressionFromLambda<T>(Expression<Func<T, object>> expression)
#pragma warning restore CS0693 // Mark members as static
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

    }
}
