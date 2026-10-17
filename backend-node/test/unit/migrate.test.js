import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { migrate } from '../../src/db/migrate.js';

function fakePool({ ef = false, existing = [] } = {}) {
  const calls = [], rows = existing.slice();
  const client = {
    async query(sql, p) {
      calls.push([sql.trim().split(/\s+/).slice(0, 3).join(' '), p]);
      if (/FROM avzar_schema_migrations/.test(sql)) return { rows };
      if (/to_regclass/.test(sql)) return { rows: [{ ef, users: ef }] };
      return { rows: [] };
    }, release() {},
  };
  return { calls, connect: async () => client };
}
const dir = async (files) => { const d = await mkdtemp(path.join(tmpdir(), 'mig-')); for (const [n, c] of Object.entries(files)) await writeFile(path.join(d, n), c); return d; };

test('empty database: baseline executes', async () => {
  const p = fakePool(); const d = await dir({ '0001_baseline.sql': 'SELECT 1;', '0002_x.sql': 'SELECT 2;' });
  assert.deepEqual(await migrate(p, d, {}), ['0001_baseline.sql', '0002_x.sql']);
  assert.ok(p.calls.some(([s]) => s.startsWith('SELECT 1')));
});
test('existing EF database: baseline adopted, never executed', async () => {
  const p = fakePool({ ef: true }); const d = await dir({ '0001_baseline.sql': 'DROP TABLE "AspNetUsers";' });
  await migrate(p, d, {});
  assert.ok(!p.calls.some(([s]) => s.startsWith('DROP')));
  assert.ok(p.calls.some(([s, a]) => s.startsWith('INSERT INTO avzar_schema_migrations') && a[0] === '0001_baseline.sql'));
});
test('modified applied migration is refused', async () => {
  const p = fakePool({ existing: [{ name: '0001_baseline.sql', checksum: 'deadbeef' }] }); const d = await dir({ '0001_baseline.sql': 'SELECT 1;' });
  await assert.rejects(migrate(p, d, {}), /modified after it was applied/);
});
