import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { access, mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';

const [magicdb, source, output, reportFile] = process.argv.slice(2);
await mkdir(source, { recursive: true });

function run(args, expected = 0) {
    const result = spawnSync(magicdb, [...args, '--directory', source, '--json'], { encoding: 'utf8' });
    if (result.error) throw result.error;
    assert.equal(result.status, expected, [args.join(' '), result.stdout, result.stderr].join('\n'));
    return result;
}

const original = `export function transform(row) {
    if (row.id === 2) throw new Error('Synthetic original fault');
    return { ...row, value: 11 };
}`;
const repair = path.join(source, 'repair.mjs');
await writeFile(repair, `
export const repairProcessed = row => ({ ...row, value: row.value - 1, repairs: (row.repairs ?? 0) + 1 });
export const repairUnprocessed = row => ({ ...row, value: row.value + 9, repairs: (row.repairs ?? 0) + 1 });
export const verify = row => row.value === 10 && row.repairs === 1;
`);

const cases = [];
for (const [id, before, after] of [
    ['absent-to-null', undefined, null],
    ['null-to-absent', null, undefined],
    ['codec-change', 'fixture-record-v1', 'fixture-record-v2']
]) {
    const model = recordCodec => ({
        protocolVersion: 1, schemaId: id, numericFormat: 'legacy', requiredCapabilities: [],
        stores: [{ name: 'clients', keyPath: 'id', autoIncrement: false, indexes: [], fields: [
            { name: 'id', type: 'integer32', nullable: false, codec: 'json' },
            { name: 'name', type: 'string', nullable: false, codec: 'json' }
        ], recordCodec }]
    });
    const first = path.join(source, id + '-source.json');
    const target = path.join(source, id + '-target.json');
    await writeFile(first, JSON.stringify(model(before)));
    await writeFile(target, JSON.stringify(model(after)));
    run(['migrations', 'add', 'Initial', '--schema', first]);
    run(['migrations', 'add', 'Original', '--schema', target, '--empty', '--mode', 'resumable'], 3);

    const root = path.join(source, id);
    const historyFile = path.join(root, 'history.json');
    const history = await readFile(historyFile, 'utf8');
    const edge = JSON.parse(history).migrations[0];
    const migrationFile = path.join(root, edge.path);
    const migration = JSON.parse(await readFile(migrationFile, 'utf8'));
    migration.operations = [{ op: 'transform', store: 'clients', module: 'original.mjs',
        sha256: 'sha256:' + createHash('sha256').update(original).digest('hex') }];
    await writeFile(path.join(path.dirname(migrationFile), 'original.mjs'), original);
    await writeFile(migrationFile, JSON.stringify(migration));
    run(['migrations', 'validate', '--schema', target]);

    const add = ['migrations', 'recovery-add', '--database', id, '--revision', '1', '--id', 'repair', '--module', repair];
    if (id === 'codec-change') {
        const result = run(add, 3);
        const { code, message } = JSON.parse(result.stderr);
        assert.equal(code, 'MAGDB4210');
        assert.match(message, /Recovery cannot change the store codec/);
        assert.equal(await readFile(historyFile, 'utf8'), history);
        await assert.rejects(access(path.join(root, 'recoveries')), { code: 'ENOENT' });
        cases.push({ name: id, outcome: 'rejected', code });
        continue;
    }

    run(add);
    run(['migrations', 'validate', '--schema', target]);
    run(['migrations', 'publish', '--schema', target, '--output', output]);
    const published = path.join(output, id);
    const manifest = JSON.parse(await readFile(path.join(published, 'manifest.json'), 'utf8'));
    assert.equal(manifest.protocolVersion, 2);
    assert.equal(manifest.recoveries.length, 1);
    for (const [i, codec] of [before, after].entries()) {
        const schema = JSON.parse(await readFile(path.join(published, manifest.schemas[i].path), 'utf8'));
        assert.equal(Object.hasOwn(schema.stores[0], 'recordCodec'), codec !== undefined);
        assert.equal(schema.stores[0].recordCodec, codec);
    }
    cases.push({ name: id, outcome: 'published', binding: manifest.recoveries[0] });
}

await writeFile(reportFile, JSON.stringify({ expected: 3, executed: cases.length, passed: cases.length, failed: 0, skipped: 0, cases }, null, 2) + '\n');
