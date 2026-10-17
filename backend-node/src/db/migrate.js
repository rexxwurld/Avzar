// Forward-only SQL migration runner. Tracks state in "avzar_schema_migrations". Never touches EF's "__EFMigrationsHistory".
// Adoption rule: if the database already has the EF schema (table "__EFMigrationsHistory" and "AspNetUsers" exist) the runner
// records 0001_baseline as applied WITHOUT executing it, so production data is never recreated. On an empty database it runs it.
import { createHash } from 'node:crypto';
import { readdir, readFile } from 'node:fs/promises';
import path from 'node:path';

const LOCK_KEY = 7_262_026; // pg_advisory_lock key: one migrator at a time across replicas
const ADOPTED = '0001_baseline.sql';

export async function migrate(pool, dir, log = console) {
  const client = await pool.connect();
  try {
    await client.query('SELECT pg_advisory_lock($1)', [LOCK_KEY]);
    await client.query(`CREATE TABLE IF NOT EXISTS avzar_schema_migrations (
      name text PRIMARY KEY, checksum text NOT NULL, applied_at timestamptz NOT NULL DEFAULT now(), adopted boolean NOT NULL DEFAULT false)`);
    const done = new Map((await client.query('SELECT name, checksum, adopted FROM avzar_schema_migrations')).rows.map((r) => [r.name, { checksum: r.checksum, adopted: r.adopted }]));
    const files = (await readdir(dir)).filter((f) => /^\d{4}_.+\.sql$/.test(f)).sort();
    const applied = [];
    for (const f of files) {
      const sql = await readFile(path.join(dir, f), 'utf8');
      const checksum = createHash('sha256').update(sql).digest('hex');
      if (done.has(f)) {
        const prior = done.get(f);
        // An adopted baseline was recorded, not executed, so correcting its SQL for empty databases must not block existing ones.
        if (!prior.adopted && prior.checksum !== checksum) throw new Error(`Migration ${f} was modified after it was applied. Migrations are forward-only; add a new file instead.`);
        continue;
      }
      let adopt = false;
      if (f === ADOPTED) {
        const r = await client.query(`SELECT to_regclass('"__EFMigrationsHistory"') IS NOT NULL AS ef, to_regclass('"AspNetUsers"') IS NOT NULL AS users`);
        adopt = r.rows[0].ef && r.rows[0].users;
      }
      if (adopt) {
        await client.query('INSERT INTO avzar_schema_migrations(name, checksum, adopted) VALUES ($1,$2,true)', [f, checksum]);
        log.info?.(`adopted existing schema as ${f}`);
      } else {
        try {
          await client.query('BEGIN');
          await client.query(sql);
          await client.query('INSERT INTO avzar_schema_migrations(name, checksum) VALUES ($1,$2)', [f, checksum]);
          await client.query('COMMIT');
        } catch (e) { await client.query('ROLLBACK'); throw new Error(`Migration ${f} failed: ${e.message}`); }
        log.info?.(`applied ${f}`);
      }
      applied.push(f);
    }
    return applied;
  } finally {
    await client.query('SELECT pg_advisory_unlock($1)', [LOCK_KEY]).catch(() => {});
    client.release();
  }
}
