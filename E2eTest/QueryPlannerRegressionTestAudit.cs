using E2eTestWebApp.TestPages;

namespace E2eTest;

[TestClass]
public sealed class QueryPlannerRegressionTestAudit : TestBase<QueryPlannerRegressionPage>
{
    [TestMethod]
    public Task Contradictory_and_constant_branches_preserve_boolean_truth() => RunCasesAsync("""
        await check('id,a', rows, and(condition('a', 'Equal', 1), condition('a', 'Equal', 2)), [], []);
        await check('id,a', rows, and(condition('a', 'GreaterThan', 3), condition('a', 'LessThan', 1)), [], []);
        const yes = condition('__constant', 'Equal', true);
        const no = condition('__constant', 'Equal', false);
        await check('id,a', rows, or(no, condition('a', 'Equal', 2)), [], [2]);
        await check('id,a', rows, and(no, condition('a', 'Equal', 2)), [], []);
        await check('id,a', rows, or(yes, condition('a', 'Equal', 2)), [], [1, 2, 3, 4]);
        await check('id,a', rows, and(yes, condition('a', 'Equal', 2)), [], [2]);
        """);

    [TestMethod]
    public Task Mixed_index_and_cursor_branches_apply_pagination_once() => RunCasesAsync("""
        const predicate = or(condition('a', 'Equal', 1), condition('b', 'Equal', 1));
        await check('id,a', rows, predicate, [addition('take', null, 2)], [1, 3]);
        await check('id,a', rows, predicate, [addition('first')], [1]);
        await check('id,a', rows, predicate, [addition('last')], [4]);
        // The staged API intentionally spells a page as Take(size).Skip(offset).
        await check('id,a', rows, predicate,
            [addition('take', null, 2), addition('skip', null, 1)], [3, 2]);
        """);

    [TestMethod]
    public Task Compound_indexes_are_used_only_for_representable_tuple_lookups() => RunCasesAsync("""
        await check('id,[a+b]', rows,
            and(condition('a', 'GreaterThan', 1), condition('b', 'LessThan', 3)), [], [2, 4]);
        await check('id,[a+b]', rows,
            and(condition('a', 'Equal', 1), condition('b', 'Equal', 1)), [], [3]);
        const textRows = [{ id: 1, a: 'A', b: 'B' }, { id: 2, a: 'a', b: 'B' }, { id: 3, a: 'z', b: 'C' }];
        await check('id,[a+b]', textRows,
            and(condition('a', 'Equal', 'a', { caseSensitive: false }), condition('b', 'Equal', 'B')), [], [1, 2]);
        """);

    [TestMethod]
    public Task Non_key_operands_and_missing_members_keep_cursor_predicate_semantics() => RunCasesAsync("""
        await check('id,flag', rows, condition('flag', 'Equal', true), [], [1, 3]);
        await check('id,nullable', rows, condition('nullable', 'In', [null, 1]), [], [1, 2, 3]);
        await check('id,nullable', rows, condition('nullable', 'Equal', null), [], [1, 3]);
        await check('id', rows, or(condition('nullable', 'Equal', 2), condition('a', 'Equal', 1)), [], [1, 3, 4]);
        await check('id', rows, condition('nullable', 'LessThan', 1), [], []);
        await check('id', rows, condition('nullable', 'GreaterThanOrEqual', 1), [], [2, 4]);
        """);

    [TestMethod]
    public Task Explicit_ordering_preserves_filtered_and_sparse_index_rows() => RunCasesAsync("""
        const predicate = condition('a', 'GreaterThan', 0);
        await check('id,a', rows, predicate, [addition('orderByDescending', 'a')], [4, 2, 1, 3]);
        await check('id,a,name', rows, predicate, [addition('orderBy', 'name')], [2, 4, 3, 1]);
        // OrderBy(...).FirstOrDefaultAsync(predicate) is a public typed route.
        await check('id,a,name', rows, predicate,
            [addition('orderBy', 'name'), addition('first')], [2]);
        await check('id,nullable', rows, null, [addition('orderBy', 'nullable')], [1, 3, 2, 4]);
        await check('id,nullable', rows, null, [addition('orderByDescending', 'nullable')], [4, 2, 1, 3]);
        await check('id,a', rows, null, [addition('orderBy', 'a')], [1, 3, 2, 4], true);
        """);

    [TestMethod]
    public Task Tail_selection_and_zero_counts_match_the_completed_sequence() => RunCasesAsync("""
        await check('id', rows, null, [addition('take', null, 0)], []);
        await check('id', rows, null, [addition('take', null, -1)], []);
        await check('id', rows, condition('b', 'GreaterThan', 0), [addition('take', null, -1)], []);
        await check('id', rows, null, [addition('skip', null, -1)], [1, 2, 3, 4]);
        await check('id', rows, condition('b', 'GreaterThan', 0), [addition('skip', null, -1)], [1, 2, 3, 4]);
        await check('id', rows, null, [addition('takeLast', null, 2)], [3, 4]);
        await check('id,a', rows, null,
            [addition('orderBy', 'a'), addition('takeLast', null, 2)], [2, 4]);
        await check('id,a', rows, null,
            [addition('orderBy', 'a'), addition('takeLast', null, 0)], []);
        await check('id,a', rows, null,
            [addition('orderBy', 'a'), addition('takeLast', null, -1)], []);
        """);

    [TestMethod]
    public Task Seeded_predicate_trees_match_an_independent_oracle_on_both_execution_paths() => RunCasesAsync("""
        let seed = 143;
        const random = count => {
            seed ^= seed << 13;
            seed ^= seed >>> 17;
            seed ^= seed << 5;
            return (seed >>> 0) % count;
        };
        const records = Array.from({ length: 32 }, (_, id) => ({
            id, a: random(6), b: random(6), name: ['Alpha', 'Beta', 'apple', 'Z', 'x'][random(5)],
            flag: random(2) === 1,
            ...(id % 3 === 0 ? {} : { nullable: id % 3 === 1 ? null : random(6) })
        }));
        const leaf = () => {
            const property = ['a', 'b', 'name', 'nullable', 'flag'][random(5)];
            if (property === 'name') {
                return condition(property, ['Equal', 'NotEqual', 'StartsWith', 'Contains'][random(4)],
                    ['a', 'A', 'B', 'x'][random(4)], { caseSensitive: random(2) === 1 });
            }
            if (property === 'flag') return condition(property, ['Equal', 'NotEqual'][random(2)], random(2) === 1);
            const operation = ['Equal', 'NotEqual', 'LessThan', 'GreaterThan', 'LessThanOrEqual', 'GreaterThanOrEqual', 'In'][random(7)];
            return condition(property, operation, operation === 'In' ? [null, 1, 3] : random(5) === 0 ? null : random(6));
        };
        const tree = depth => !depth || random(3) === 0
            ? leaf()
            : random(2) === 0 ? and(tree(depth - 1), tree(depth - 1)) : or(tree(depth - 1), tree(depth - 1));
        // This oracle evaluates the original expression, independently of planner
        // normalization, DNF rewriting, index choice and cursor comparison helpers.
        const matches = (node, row) => {
            if (node.nodeType === 0) {
                return node.operator === 0
                    ? node.children.every(child => matches(child, row))
                    : node.children.some(child => matches(child, row));
            }
            let { property, operation, value, isString, caseSensitive } = node.condition;
            let actual = row[property];
            if (isString && !caseSensitive) {
                if (typeof actual === 'string') actual = actual.toLowerCase();
                if (typeof value === 'string') value = value.toLowerCase();
            }
            switch (operation) {
                case 'Equal': return value == null ? actual == null : actual === value;
                case 'NotEqual': return value == null ? actual != null : actual !== value;
                case 'LessThan': return actual != null && value != null && actual < value;
                case 'GreaterThan': return actual != null && value != null && actual > value;
                case 'LessThanOrEqual': return actual != null && value != null && actual <= value;
                case 'GreaterThanOrEqual': return actual != null && value != null && actual >= value;
                case 'In': return value.some(candidate => candidate == null ? actual == null : actual === candidate);
                case 'StartsWith': return typeof actual === 'string' && actual.startsWith(value);
                case 'Contains': return typeof actual === 'string' && actual.includes(value);
                default: throw new Error('Unknown oracle operation: ' + operation);
            }
        };
        const db = new Dexie('planner-oracle-' + crypto.randomUUID());
        db.version(1).stores({ records: 'id,a,b,name,nullable,flag,[a+b]' });
        try {
            await db.open();
            await db.table('records').bulkAdd(records);
            for (let sample = 0; sample < 50; sample++) {
                const predicate = tree(2);
                const expected = records.filter(row => matches(predicate, row)).map(row => row.id).sort((a, b) => a - b);
                for (const forceCursor of [false, true]) {
                    const actual = (await magicQueryAsync(db, db.table('records'), predicate, [], forceCursor))
                        .map(row => row.id).sort((a, b) => a - b);
                    if (JSON.stringify(actual) !== JSON.stringify(expected)) {
                        throw new Error(JSON.stringify({ sample, forceCursor, predicate, expected, actual }));
                    }
                }
            }
        } finally {
            db.close();
            await db.delete();
        }
        """);

    private async Task RunCasesAsync(string cases)
    {
        await using var disposablePage = new DisposablePage(await NewPageAsync());
        await disposablePage.Page.EvaluateAsync("""
            async () => {
                const root = '/_content/Magic.IndexedDb/';
                const { default: Dexie } = await import(root + 'dexie/dexie.js');
                const { magicQueryAsync } = await import(root + 'magicLinqToIndexedDb.js');
                const { configureDebug, getLastQueryPlannerTrace } = await import(root + 'magicDbMethods.js');
                const condition = (property, operation, value, flags = {}) => ({
                    nodeType: 1,
                    condition: { property, operation, value, isString: typeof value === 'string', caseSensitive: true, ...flags }
                });
                const and = (...children) => ({ nodeType: 0, operator: 0, children });
                const or = (...children) => ({ nodeType: 0, operator: 1, children });
                const addition = (additionFunction, property = null, intValue = 0) => ({ additionFunction, property, intValue });
                const rows = [
                    { id: 1, a: 1, b: 2, name: 'Z', flag: true, nullable: null },
                    { id: 2, a: 2, b: 1, name: 'A', flag: false, nullable: 1 },
                    { id: 3, a: 1, b: 1, name: 'C', flag: true },
                    { id: 4, a: 3, b: 1, name: 'B', flag: false, nullable: 2 }
                ];
                const check = async (schema, records, predicate, additions, expected, requireNative = false) => {
                    const db = new Dexie('planner-audit-' + crypto.randomUUID());
                    db.version(1).stores({ records: schema });
                    try {
                        await db.open();
                        await db.table('records').bulkAdd(records);
                        configureDebug(requireNative);
                        const actual = (await magicQueryAsync(db, db.table('records'), predicate, additions)).map(row => row.id);
                        if (JSON.stringify(actual) !== JSON.stringify(expected)) {
                            throw new Error(JSON.stringify({ schema, predicate, additions, expected, actual }));
                        }
                        if (requireNative && !getLastQueryPlannerTrace()?.stages.some(stage => stage.stage === 'indexed-execution')) {
                            throw new Error('A complete ordinary index unnecessarily lost its native ordering path.');
                        }
                    } finally {
                        configureDebug(false);
                        db.close();
                        await db.delete();
                    }
                };
            """ + cases + "\n}");
    }
}
