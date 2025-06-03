using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb.Testing.Helpers
{
    public class QueryTestBlueprint<T>
    {
        public List<Expression<Func<T, bool>>> WherePredicates { get; set; } = new();
        public List<Expression<Func<T, object>>> OrderBys { get; set; } = new();
        public List<Expression<Func<T, object>>> OrderByDescendings { get; set; } = new();
        public List<int> TakeValues { get; set; } = new();
        public List<int> SkipValues { get; set; } = new();
        public List<int> TakeLastValues { get; set; } = new();
    }

    public static class AllPaths
    {
        public class Transition<T>
        {
            public string Name { get; set; } = string.Empty;
            public Func<object, QueryTestBlueprint<T>, object> Execute { get; set; } = default!;
            public int MaxRepetitions { get; set; } = 1;
            public Type? Returns { get; set; }

        }

        private static int RepeatMax = 2;

        public static Dictionary<Type, List<Transition<T>>> BuildTransitionMap<T>(Dictionary<string, int>? overrides = null) where T : class
        {
            int GetMax(string method, int defaultVal) =>
                overrides != null && overrides.TryGetValue(method, out var val) ? val : defaultVal;

            return new Dictionary<Type, List<Transition<T>>>
            {
                [typeof(IMagicQuery<T>)] = new List<Transition<T>>
        {
            new() { Name = "Where", Execute = (q, bp) => ((IMagicQuery<T>)q).Where(bp.WherePredicates[0]), MaxRepetitions = GetMax("Where", RepeatMax), Returns = typeof(IMagicQueryStaging<T>) },
            new() { Name = "Cursor", Execute = (q, bp) => ((IMagicQuery<T>)q).Cursor(bp.WherePredicates[0]), MaxRepetitions = GetMax("Cursor", RepeatMax), Returns = typeof(IMagicCursor<T>) },
            new() { Name = "Take", Execute = (q, bp) => ((IMagicQuery<T>)q).Take(bp.TakeValues[0]), MaxRepetitions = GetMax("Take", RepeatMax), Returns = typeof(IMagicQueryPaginationTake<T>) },
            new() { Name = "TakeLast", Execute = (q, bp) => ((IMagicQuery<T>)q).TakeLast(bp.TakeLastValues[0]), MaxRepetitions = GetMax("TakeLast", RepeatMax), Returns = typeof(IMagicQueryFinal<T>) },
            new() { Name = "Skip", Execute = (q, bp) => ((IMagicQuery<T>)q).Skip(bp.SkipValues[0]), MaxRepetitions = GetMax("Skip", RepeatMax), Returns = typeof(IMagicQueryFinal<T>) },
            new() { Name = "OrderBy", Execute = (q, bp) => ((IMagicQuery<T>)q).OrderBy(bp.OrderBys[0]), MaxRepetitions = GetMax("OrderBy", RepeatMax), Returns = typeof(IMagicQueryOrderableTable<T>) },
            new() { Name = "OrderByDescending", Execute = (q, bp) => ((IMagicQuery<T>)q).OrderByDescending(bp.OrderByDescendings[0]), MaxRepetitions = GetMax("OrderByDescending", RepeatMax), Returns = typeof(IMagicQueryOrderableTable<T>) }
        },

                [typeof(IMagicQueryStaging<T>)] = new List<Transition<T>>
        {
            new() { Name = "Where", Execute = (q, bp) => ((IMagicQueryStaging<T>)q).Where(bp.WherePredicates[0]), MaxRepetitions = GetMax("Where", RepeatMax), Returns = typeof(IMagicQueryStaging<T>) },
            new() { Name = "Take", Execute = (q, bp) => ((IMagicQueryStaging<T>)q).Take(bp.TakeValues[0]), MaxRepetitions = GetMax("Take", RepeatMax), Returns = typeof(IMagicQueryPaginationTake<T>) },
            new() { Name = "TakeLast", Execute = (q, bp) => ((IMagicQueryStaging<T>)q).TakeLast(bp.TakeLastValues[0]), MaxRepetitions = GetMax("TakeLast", RepeatMax), Returns = typeof(IMagicQueryFinal<T>) },
            new() { Name = "Skip", Execute = (q, bp) => ((IMagicQueryStaging<T>)q).Skip(bp.SkipValues[0]), MaxRepetitions = GetMax("Skip", RepeatMax), Returns = typeof(IMagicQueryFinal<T>) }
        },

                [typeof(IMagicQueryPaginationTake<T>)] = new List<Transition<T>>() {
                new() { Name = "Skip", Execute = (q, bp) => ((IMagicQuery<T>)q).Skip(bp.SkipValues[0]), MaxRepetitions = GetMax("Skip", RepeatMax), Returns = typeof(IMagicQueryFinal<T>) }
                },

                [typeof(IMagicQueryFinal<T>)] = new List<Transition<T>>() /* terminal */,

                [typeof(IMagicQueryOrderableTable<T>)] = new List<Transition<T>>
        {
            new() { Name = "Take", Execute = (q, bp) => ((IMagicQueryOrderableTable<T>)q).Take(bp.TakeValues[0]), MaxRepetitions = GetMax("Take", RepeatMax), Returns = typeof(IMagicQueryPaginationTake<T>) },
            new() { Name = "TakeLast", Execute = (q, bp) => ((IMagicQueryOrderableTable<T>)q).TakeLast(bp.TakeLastValues[0]), MaxRepetitions = GetMax("TakeLast", RepeatMax), Returns = typeof(IMagicQueryFinal<T>) },
            new() { Name = "Skip", Execute = (q, bp) => ((IMagicQueryOrderableTable<T>)q).Skip(bp.SkipValues[0]), MaxRepetitions = GetMax("Skip", 1), Returns = typeof(IMagicQueryFinal<T>) }
        },

                [typeof(IMagicQueryOrderable<T>)] = new List<Transition<T>>
        {
            new() { Name = "Take", Execute = (q, bp) => ((IMagicQueryOrderableTable<T>)q).Take(bp.TakeValues[0]), MaxRepetitions = GetMax("Take", RepeatMax), Returns = typeof(IMagicQueryPaginationTake<T>) },
            new() { Name = "TakeLast", Execute = (q, bp) => ((IMagicQueryOrderableTable<T>)q).TakeLast(bp.TakeLastValues[0]), MaxRepetitions = GetMax("TakeLast", RepeatMax), Returns = typeof(IMagicQueryFinal<T>) },
            new() { Name = "Skip", Execute = (q, bp) => ((IMagicQueryOrderableTable<T>)q).Skip(bp.SkipValues[0]), MaxRepetitions = GetMax("Skip", 1), Returns = typeof(IMagicQueryFinal<T>) }
        },

                [typeof(IMagicCursor<T>)] = new List<Transition<T>>
        {
            new() { Name = "Cursor", Execute = (q, bp) => ((IMagicCursor<T>)q).Cursor(bp.WherePredicates[0]), MaxRepetitions = GetMax("Cursor", RepeatMax), Returns = typeof(IMagicCursor<T>) },
            new() { Name = "Take", Execute = (q, bp) => ((IMagicCursor<T>)q).Take(bp.TakeValues[0]), MaxRepetitions = GetMax("Take", RepeatMax), Returns = typeof(IMagicCursorPaginationTake<T>) },
            new() { Name = "TakeLast", Execute = (q, bp) => ((IMagicCursor<T>)q).TakeLast(bp.TakeLastValues[0]), MaxRepetitions = GetMax("TakeLast", RepeatMax), Returns = typeof(IMagicCursorPaginationTake<T>) },
            new() { Name = "Skip", Execute = (q, bp) => ((IMagicCursor<T>)q).Skip(bp.SkipValues[0]), MaxRepetitions = GetMax("Skip", RepeatMax), Returns = typeof(IMagicCursorSkip<T>) },
            new() { Name = "OrderBy", Execute = (q, bp) => ((IMagicCursor<T>)q).OrderBy(bp.OrderBys[0]), MaxRepetitions = GetMax("OrderBy", RepeatMax), Returns = typeof(IMagicCursorStage<T>) },
            new() { Name = "OrderByDescending", Execute = (q, bp) => ((IMagicCursor<T>)q).OrderByDescending(bp.OrderByDescendings[0]), MaxRepetitions = GetMax("OrderByDescending", RepeatMax), Returns = typeof(IMagicCursorStage<T>) }
        },

                [typeof(IMagicCursorStage<T>)] = new List<Transition<T>>
        {
            new() { Name = "Take", Execute = (q, bp) => ((IMagicCursorStage<T>)q).Take(bp.TakeValues[0]), MaxRepetitions = GetMax("Take", RepeatMax), Returns = typeof(IMagicCursorPaginationTake<T>) },
            new() { Name = "TakeLast", Execute = (q, bp) => ((IMagicCursorStage<T>)q).TakeLast(bp.TakeLastValues[0]), MaxRepetitions = GetMax("TakeLast", RepeatMax), Returns = typeof(IMagicCursorPaginationTake<T>) },
            new() { Name = "Skip", Execute = (q, bp) => ((IMagicCursorStage<T>)q).Skip(bp.SkipValues[0]), MaxRepetitions = GetMax("Skip", RepeatMax), Returns = typeof(IMagicCursorSkip<T>) }
        },

                [typeof(IMagicCursorSkip<T>)] = new List<Transition<T>>(), /* terminal */
                [typeof(IMagicCursorFinal<T>)] = new List<Transition<T>>(), /* terminal */
                [typeof(IMagicCursorPaginationTake<T>)] = new List<Transition<T>>()
                {
            new() { Name = "Skip", Execute = (q, bp) => ((IMagicCursorStage<T>)q).Skip(bp.SkipValues[0]), MaxRepetitions = GetMax("Skip", RepeatMax), Returns = typeof(IMagicCursorSkip<T>) }
                }
            };
        }

    }
}
