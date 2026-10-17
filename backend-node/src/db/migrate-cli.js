// Applies pending SQL migrations from ../../migrations. Usage: DATABASE_URL=... npm run migrate
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import pg from 'pg';
import { migrate } from './migrate.js';

const url = process.env.DATABASE_URL;
if (!url) { console.error('DATABASE_URL is required.'); process.exit(1); }
const pool = new pg.Pool({ connectionString: url, max: 2 });
try {
  const dir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../migrations');
  const applied = await migrate(pool, dir, console);
  console.info(JSON.stringify({ event: 'migrations_done', applied }));
} catch (error) {
  console.error(error.message);
  process.exitCode = 1;
} finally {
  await pool.end();
}
