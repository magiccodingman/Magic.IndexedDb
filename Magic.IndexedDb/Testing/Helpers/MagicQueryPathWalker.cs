using Magic.IndexedDb.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb.Testing.Helpers
{
    internal static class MagicInMemoryExecutor
    {
        public static List<T> Execute<T>(
        IEnumerable<T> allItems,
        List<string> trail,
        QueryTestBlueprint<T> bp
        ) where T : class, IMagicTableBase, new()
        {
            IEnumerable<T> result = allItems;

            // Where/Cursor
            foreach (var step in trail)
            {
                if (step == "Where" || step == "Cursor")
                    result = result.Where(bp.WherePredicates[0].Compile());
            }

            // Take/Skip IndexedDB reversal
            var hasTake = trail.Contains("Take");
            var hasSkip = trail.Contains("Skip");
            var skipFirst = hasTake && hasSkip && trail.IndexOf("Take") < trail.IndexOf("Skip");

            if (hasTake && hasSkip && skipFirst)
            {
                result = result.Skip(bp.SkipValues[0]);
                result = result.Take(bp.TakeValues[0]);
            }
            else
            {
                foreach (var step in trail)
                {
                    if (step == "Take") result = result.Take(bp.TakeValues[0]);
                    else if (step == "Skip") result = result.Skip(bp.SkipValues[0]);
                    else if (step == "TakeLast") result = result.Reverse().Take(bp.TakeLastValues[0]).Reverse();
                }
            }

            // OrderBy / OrderByDescending
            IOrderedEnumerable<T>? ordered = null;
            foreach (var step in trail)
            {
                if (step == "OrderBy")
                    ordered = result.OrderBy(bp.OrderBys[0].Compile());
                else if (step == "OrderByDescending")
                    ordered = result.OrderByDescending(bp.OrderByDescendings[0].Compile());
            }

            if (ordered != null)
            {
                // Apply compound key .ThenBy to match database ordering
                IMagicCompoundKey? compoundKey = new T().GetKeys();

                foreach (var prop in compoundKey.PropertyInfos)
                    ordered = ordered.ThenBy(x => prop.GetValue(x));

                result = ordered;
            }

            return result.ToList();
        }
    }

    public static class MagicQueryPathWalker
    {
        public static List<ExecutionPath<T>> GenerateAllPaths<T>(
            IMagicQuery<T> baseQuery,
            List<T> allPeople,
            QueryTestBlueprint<T> blueprint,
            int maxDepth = 6,
            Dictionary<string, int>? repetitionOverrides = null,
            Func<QueryTestBlueprint<T>, IMagicCursor<T>>? cursorProvider = null
        ) where T : class, IMagicTableBase, new()
        {
            var map = AllPaths.BuildTransitionMap<T>(repetitionOverrides);
            var results = new List<ExecutionPath<T>>();
            var seenPaths = new HashSet<string>();

            Explore(map, baseQuery, typeof(IMagicQuery<T>), allPeople, blueprint, results, seenPaths, maxDepth);

            if (cursorProvider != null)
            {
                var cursorQuery = cursorProvider(blueprint);
                Explore(map, cursorQuery, typeof(IMagicCursor<T>), allPeople, blueprint, results, seenPaths, maxDepth);
            }

            return results;
        }

        private static void Explore<T>(
            Dictionary<Type, List<AllPaths.Transition<T>>> map,
            object queryObj,
            Type currentType,
            List<T> allPeople,
            QueryTestBlueprint<T> blueprint,
            List<ExecutionPath<T>> results,
            HashSet<string> seenPaths,
            int maxDepth,
            List<string>? trail = null,
            Func<IQueryable<T>, IQueryable<T>>? linq = null,
            Dictionary<string, int>? used = null,
            int depth = 0
        ) where T : class, IMagicTableBase, new()
        {
            if (depth > maxDepth || !map.TryGetValue(currentType, out var transitions))
                return;

            trail ??= new();
            linq ??= q => q;
            used ??= new();

            foreach (var transition in transitions)
            {
                var nextUsed = new Dictionary<string, int>(used);
                if (!nextUsed.TryGetValue(transition.Name, out var count))
                    count = 0;
                if (count >= transition.MaxRepetitions) continue;
                nextUsed[transition.Name] = count + 1;

                try
                {
                    object nextQuery = transition.Execute(queryObj, blueprint);
                    var nextInterface = nextQuery.GetType().GetInterfaces().FirstOrDefault(i => i.Name.StartsWith("IMagic"));
                    if (nextInterface == null) continue;

                    var newTrail = trail.Append(transition.Name).ToList();
                    var pathKey = string.Join("→", newTrail);
                    if (!seenPaths.Add(pathKey)) continue;

                    Func<IQueryable<T>, IQueryable<T>> newLinq = q =>
                        ApplyLinqTrail(q, newTrail, blueprint);

                    if (nextQuery is IMagicExecute<T>)
                    {
                        results.Add(new ExecutionPath<T>
                        {
                            Name = pathKey,
                            ExecuteDb = async () => await ((IMagicExecute<T>)nextQuery).ToListAsync(),
                            ExecuteInMemory = () => MagicInMemoryExecutor.Execute(allPeople, newTrail, blueprint)
                        });
                    }

                    Explore(
                        map,
                        nextQuery,
                        nextInterface,
                        allPeople,
                        blueprint,
                        results,
                        seenPaths,
                        maxDepth,
                        newTrail,
                        newLinq,
                        nextUsed,
                        depth + 1
                    );
                }
                catch { }
            }
        }

        private static IQueryable<T> ApplyLinqTrail<T>(IQueryable<T> query, List<string> trail, QueryTestBlueprint<T> bp) where T : class
        {
            var transformed = query;
            var methods = new List<string>(trail);

            // Special case: Take then Skip → reverse to Skip then Take for LINQ to match IndexedDB
            if (methods.Contains("Take") && methods.Contains("Skip"))
            {
                var takeIndex = methods.IndexOf("Take");
                var skipIndex = methods.IndexOf("Skip");
                if (takeIndex < skipIndex)
                {
                    // Move Skip before Take
                    methods.RemoveAt(skipIndex);
                    methods.Insert(takeIndex, "Skip");
                    methods.Remove("Take");
                    methods.Insert(takeIndex + 1, "Take");
                }
            }

            foreach (var step in methods)
            {
                transformed = ApplyLinqStep(transformed, step, bp);
            }

            return transformed;
        }

        private static IQueryable<T> ApplyLinqStep<T>(IQueryable<T> query, string method, QueryTestBlueprint<T> bp) where T : class
        {
            return method switch
            {
                "Where" => query.Where(bp.WherePredicates[0]),
                "Cursor" => query.Where(bp.WherePredicates[0]),
                "OrderBy" => query.OrderBy(bp.OrderBys[0]),
                "OrderByDescending" => query.OrderByDescending(bp.OrderByDescendings[0]),
                "Take" => query.Take(bp.TakeValues[0]),
                "TakeLast" => query.Reverse().Take(bp.TakeLastValues[0]).Reverse(),
                "Skip" => query.Skip(bp.SkipValues[0]),
                _ => query
            };
        }
    }

    public class ExecutionPath<T> where T : class
    {
        public string Name { get; set; } = string.Empty;
        public Func<Task<List<T>>> ExecuteDb { get; set; } = default!;
        public Func<List<T>> ExecuteInMemory { get; set; } = default!;
    }
}
