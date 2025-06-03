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

            // STEP 1: Apply Where/Cursor filters
            foreach (var step in trail)
            {
                if (step == "Where" || step == "Cursor")
                    result = result.Where(bp.WherePredicates[0].Compile());
            }

            // STEP 2: Apply explicit OrderBy / OrderByDescending
            IOrderedEnumerable<T>? ordered = null;
            bool hasExplicitOrder = false;

            foreach (var step in trail)
            {
                if (step == "OrderBy")
                {
                    ordered = result.OrderBy(bp.OrderBys[0].Compile());
                    hasExplicitOrder = true;
                }
                else if (step == "OrderByDescending")
                {
                    ordered = result.OrderByDescending(bp.OrderByDescendings[0].Compile());
                    hasExplicitOrder = true;
                }
            }

            // STEP 3: If ordered, add compound key tiebreakers for determinism
            if (ordered != null)
            {
                var compoundKey = new T().GetKeys();
                if (compoundKey?.PropertyInfos?.Length > 0)
                {
                    foreach (var prop in compoundKey.PropertyInfos)
                        ordered = ordered.ThenBy(x => prop.GetValue(x) ?? "");
                }

                result = ordered;
            }

            // STEP 4: If no explicit order, apply default ordering if Take/Skip is used
            if (!hasExplicitOrder && (trail.Contains("Take") || trail.Contains("Skip") || trail.Contains("TakeLast")))
            {
                IOrderedEnumerable<T>? stableOrdered = null;

                // Step 1: Apply IndexOrderingProperties first (e.g., from .Index("TestInt"))
                if (bp.IndexOrderingProperties?.Count > 0)
                {
                    foreach (var selector in bp.IndexOrderingProperties)
                    {
                        stableOrdered = stableOrdered == null
                            ? result.OrderBy(selector)
                            : stableOrdered.ThenBy(selector);
                    }
                }

                // Step 2: ALWAYS apply compound key fallback to break ties
                var compoundKey = new T().GetKeys();
                if (compoundKey?.PropertyInfos?.Length > 0)
                {
                    foreach (var prop in compoundKey.PropertyInfos)
                    {
                        stableOrdered = stableOrdered == null
                            ? result.OrderBy(x => prop.GetValue(x) ?? "")
                            : stableOrdered.ThenBy(x => prop.GetValue(x) ?? "");
                    }
                }

                if (stableOrdered != null)
                    result = stableOrdered;
            }


            // STEP 5: Detect if Skip should come before Take
            bool hasTake = trail.Contains("Take");
            bool hasSkip = trail.Contains("Skip");
            bool flipSkipAndTake = hasTake && hasSkip && trail.IndexOf("Take") < trail.IndexOf("Skip");

            if (flipSkipAndTake)
            {
                // IndexedDB-style → flip to match SQL semantics
                result = result.Skip(bp.SkipValues[0]);
                result = result.Take(bp.TakeValues[0]);
            }
            else
            {
                foreach (var step in trail)
                {
                    if (step == "Skip")
                        result = result.Skip(bp.SkipValues[0]);
                    else if (step == "Take")
                        result = result.Take(bp.TakeValues[0]);
                    else if (step == "TakeLast")
                        result = result.Reverse().Take(bp.TakeLastValues[0]).Reverse();
                }
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
            Func<QueryTestBlueprint<T>, IMagicCursor<T>>? cursorProvider = null,
            int? overrideMaxRepetitions = null
        ) where T : class, IMagicTableBase, new()
        {
            var map = AllPaths.BuildTransitionMap<T>(repetitionOverrides);
            var results = new List<ExecutionPath<T>>();
            var seenPaths = new HashSet<string>();

            Explore(map, baseQuery, typeof(IMagicQuery<T>), allPeople, blueprint, results, seenPaths, maxDepth, null, null, null, 0, overrideMaxRepetitions);

            if (cursorProvider != null)
            {
                var cursorQuery = cursorProvider(blueprint);
                Explore(map, cursorQuery, typeof(IMagicCursor<T>), allPeople, blueprint, results, seenPaths, maxDepth, null, null, null, 0, overrideMaxRepetitions);
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
    int depth = 0,
     int? overrideMaxRepetitions = null
) where T : class, IMagicTableBase, new()
        {
            if (depth > maxDepth || !map.TryGetValue(currentType, out var transitions))
                return;

            trail ??= new();
            linq ??= q => q;
            used ??= new();

            foreach (var transition in transitions)
            {
                int reps = transition.MaxRepetitions;
                if (overrideMaxRepetitions != null)
                    reps = overrideMaxRepetitions ?? transition.MaxRepetitions;

                var nextUsed = new Dictionary<string, int>(used);
                if (!nextUsed.TryGetValue(transition.Name, out var count))
                    count = 0;
                if (count >= reps) continue;
                nextUsed[transition.Name] = count + 1;

                List<string> newTrail = trail.Append(transition.Name).ToList();

                try
                {
                    object nextQuery = transition.Execute(queryObj, blueprint);

                    // Only follow the explicitly declared Returns interface
                    Type? nextInterface = transition.Returns;
                    if (nextInterface == null || !map.ContainsKey(nextInterface))
                        continue;

                    string pathKey = $"{string.Join("→", newTrail)}||{nextInterface.Name}";
                    if (!seenPaths.Add(pathKey)) continue;

                    Func<IQueryable<T>, IQueryable<T>> newLinq = q =>
                        ApplyLinqTrail(q, newTrail, blueprint);

                    if (nextQuery is IMagicExecute<T>)
                    {
                        // Create a blueprint clone with filtered IndexOrderingProperties
                        var localBlueprint = new QueryTestBlueprint<T>
                        {
                            WherePredicates = blueprint.WherePredicates,
                            OrderBys = blueprint.OrderBys,
                            OrderByDescendings = blueprint.OrderByDescendings,
                            SkipValues = blueprint.SkipValues,
                            TakeValues = blueprint.TakeValues,
                            TakeLastValues = blueprint.TakeLastValues,

                            // Only apply index ordering if a relevant Where is in the trail
                            IndexOrderingProperties = newTrail.Contains("Where") || newTrail.Contains("Cursor")
                                ? blueprint.IndexOrderingProperties
                                : null
                        };

                        results.Add(new ExecutionPath<T>
                        {
                            Name = pathKey,
                            ExecuteDb = async () => await ((IMagicExecute<T>)nextQuery).ToListAsync(),
                            ExecuteInMemory = () => MagicInMemoryExecutor.Execute(allPeople, newTrail, localBlueprint)
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
                        depth + 1,
                        overrideMaxRepetitions
                    );
                }
                catch
                {
                    // Swallow invalid transitions
                }

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
