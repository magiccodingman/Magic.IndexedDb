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
        //public List<Expression<Func<T, bool>>> CursorPredicates { get; set; } = new();
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
        }

        private static int RepeatMax = 1;

        public static Dictionary<Type, List<Transition<T>>> BuildTransitionMap<T>(Dictionary<string, int>? overrides = null) where T : class
        {
            int GetMax(string method, int defaultVal) =>
                overrides != null && overrides.TryGetValue(method, out var val) ? val : defaultVal;

            return new Dictionary<Type, List<Transition<T>>>
            {
                [typeof(IMagicQuery<T>)] = new List<Transition<T>>
            {
                new() { Name = "Where", Execute = (q, bp) => ((IMagicQuery<T>)q).Where(bp.WherePredicates[0]), MaxRepetitions = GetMax("Where", RepeatMax) },
                new() { Name = "Cursor", Execute = (q, bp) => ((IMagicQuery<T>)q).Cursor(bp.WherePredicates[0]), MaxRepetitions = GetMax("Cursor", RepeatMax) },
                new() { Name = "Take", Execute = (q, bp) => ((IMagicQuery<T>)q).Take(bp.TakeValues[0]), MaxRepetitions = GetMax("Take", RepeatMax) },
                new() { Name = "TakeLast", Execute = (q, bp) => ((IMagicQuery<T>)q).TakeLast(bp.TakeLastValues[0]), MaxRepetitions = GetMax("TakeLast", RepeatMax) },
                new() { Name = "Skip", Execute = (q, bp) => ((IMagicQuery<T>)q).Skip(bp.SkipValues[0]), MaxRepetitions = GetMax("Skip", RepeatMax) },
                new() { Name = "OrderBy", Execute = (q, bp) => ((IMagicQuery<T>)q).OrderBy(bp.OrderBys[0]), MaxRepetitions = GetMax("OrderBy", RepeatMax) },
                new() { Name = "OrderByDescending", Execute = (q, bp) => ((IMagicQuery<T>)q).OrderByDescending(bp.OrderByDescendings[0]), MaxRepetitions = GetMax("OrderByDescending", RepeatMax) }
            },

                [typeof(IMagicQueryStaging<T>)] = new List<Transition<T>>
            {
                new() { Name = "Where", Execute = (q, bp) => ((IMagicQueryStaging<T>)q).Where(bp.WherePredicates[0]), MaxRepetitions = GetMax("Where", RepeatMax) },
                new() { Name = "Take", Execute = (q, bp) => ((IMagicQueryStaging<T>)q).Take(bp.TakeValues[0]), MaxRepetitions = GetMax("Take", RepeatMax) },
                new() { Name = "TakeLast", Execute = (q, bp) => ((IMagicQueryStaging<T>)q).TakeLast(bp.TakeLastValues[0]), MaxRepetitions = GetMax("TakeLast", RepeatMax) },
                new() { Name = "Skip", Execute = (q, bp) => ((IMagicQueryStaging<T>)q).Skip(bp.SkipValues[0]), MaxRepetitions = GetMax("Skip", RepeatMax) }
            },

                [typeof(IMagicQueryPaginationTake<T>)] = new List<Transition<T>>
            {
                new() { Name = "Skip", Execute = (q, bp) => ((IMagicQueryPaginationTake<T>)q).Skip(bp.SkipValues[0]), MaxRepetitions = GetMax("Skip", RepeatMax) }
            },

                [typeof(IMagicQueryFinal<T>)] = new List<Transition<T>>(),

                [typeof(IMagicQueryOrderableTable<T>)] = new List<Transition<T>>
            {
                new() { Name = "Take", Execute = (q, bp) => ((IMagicQueryOrderableTable<T>)q).Take(bp.TakeValues[0]), MaxRepetitions = GetMax("Take", RepeatMax) },
                new() { Name = "TakeLast", Execute = (q, bp) => ((IMagicQueryOrderableTable<T>)q).TakeLast(bp.TakeLastValues[0]), MaxRepetitions = GetMax("TakeLast", RepeatMax) },
                new() { Name = "Skip", Execute = (q, bp) => ((IMagicQueryOrderableTable<T>)q).Skip(bp.SkipValues[0]), MaxRepetitions = GetMax("Skip", 1) }
            },

                [typeof(IMagicQueryOrderable<T>)] = new List<Transition<T>>
            {
                new() { Name = "Take", Execute = (q, bp) => ((IMagicQueryOrderable<T>)q).Take(bp.TakeValues[0]), MaxRepetitions = GetMax("Take", RepeatMax) },
                new() { Name = "TakeLast", Execute = (q, bp) => ((IMagicQueryOrderable<T>)q).TakeLast(bp.TakeLastValues[0]), MaxRepetitions = GetMax("TakeLast", RepeatMax) },
                new() { Name = "Skip", Execute = (q, bp) => ((IMagicQueryOrderable<T>)q).Skip(bp.SkipValues[0]), MaxRepetitions = GetMax("Skip", RepeatMax) }
            },

                [typeof(IMagicCursor<T>)] = new List<Transition<T>>
            {
                new() { Name = "Cursor", Execute = (q, bp) => ((IMagicCursor<T>)q).Cursor(bp.WherePredicates[0]), MaxRepetitions = GetMax("Cursor", RepeatMax) },
                new() { Name = "Take", Execute = (q, bp) => ((IMagicCursor<T>)q).Take(bp.TakeValues[0]), MaxRepetitions = GetMax("Take", RepeatMax) },
                new() { Name = "TakeLast", Execute = (q, bp) => ((IMagicCursor<T>)q).TakeLast(bp.TakeLastValues[0]), MaxRepetitions = GetMax("TakeLast", RepeatMax) },
                new() { Name = "Skip", Execute = (q, bp) => ((IMagicCursor<T>)q).Skip(bp.SkipValues[0]), MaxRepetitions = GetMax("Skip", RepeatMax) },
                new() { Name = "OrderBy", Execute = (q, bp) => ((IMagicCursor<T>)q).OrderBy(bp.OrderBys[0]), MaxRepetitions = GetMax("OrderBy", RepeatMax) },
                new() { Name = "OrderByDescending", Execute = (q, bp) => ((IMagicCursor<T>)q).OrderByDescending(bp.OrderByDescendings[0]), MaxRepetitions = GetMax("OrderByDescending", RepeatMax) }
            },

                [typeof(IMagicCursorStage<T>)] = new List<Transition<T>>
            {
                new() { Name = "Take", Execute = (q, bp) => ((IMagicCursorStage<T>)q).Take(bp.TakeValues[0]), MaxRepetitions = GetMax("Take", RepeatMax) },
                new() { Name = "TakeLast", Execute = (q, bp) => ((IMagicCursorStage<T>)q).TakeLast(bp.TakeLastValues[0]), MaxRepetitions = GetMax("TakeLast", RepeatMax) },
                new() { Name = "Skip", Execute = (q, bp) => ((IMagicCursorStage<T>)q).Skip(bp.SkipValues[0]), MaxRepetitions = GetMax("Skip", RepeatMax) },
                new() { Name = "OrderBy", Execute = (q, bp) => ((IMagicCursorStage<T>)q).OrderBy(bp.OrderBys[0]), MaxRepetitions = GetMax("OrderBy", RepeatMax) },
                new() { Name = "OrderByDescending", Execute = (q, bp) => ((IMagicCursorStage<T>)q).OrderByDescending(bp.OrderByDescendings[0]), MaxRepetitions = GetMax("OrderByDescending", RepeatMax) }
            },

                [typeof(IMagicCursorSkip<T>)] = new List<Transition<T>>
            {
                new() { Name = "Skip", Execute = (q, bp) => ((IMagicCursorSkip<T>)q).Skip(bp.SkipValues[0]), MaxRepetitions = GetMax("Skip", RepeatMax) },
                new() { Name = "OrderBy", Execute = (q, bp) => ((IMagicCursorSkip<T>)q).OrderBy(bp.OrderBys[0]), MaxRepetitions = GetMax("OrderBy", RepeatMax) },
                new() { Name = "OrderByDescending", Execute = (q, bp) => ((IMagicCursorSkip<T>)q).OrderByDescending(bp.OrderByDescendings[0]), MaxRepetitions = GetMax("OrderByDescending", RepeatMax) }
            }
            };
        }
    }
}
