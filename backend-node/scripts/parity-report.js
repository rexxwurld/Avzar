// Compares the C# endpoint inventory with Node route registrations and flags handlers that never
// touch the database or a service (placeholder handlers). Uses only Node built-ins.
import { readFileSync, readdirSync, statSync, writeFileSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const inventory = JSON.parse(readFileSync(join(root, 'migration/endpoint-inventory.json'), 'utf8'));
const norm = (p) => p.replace(/^\/api/, '').replace(/\{[^}]*\}|:[A-Za-z_]+/g, '{}').replace(/\/$/, '') || '/';

function walk(dir) {
  return readdirSync(dir).flatMap((n) => {
    const f = join(dir, n);
    return statSync(f).isDirectory() ? walk(f) : f.endsWith('.js') ? [f] : [];
  });
}

const found = new Map(); // "METHOD path" -> { file, real }
const re = /router\.(get|post|put|patch|delete)\(\s*(['"`])([^'"`]+)\2([\s\S]*?)(?=\n\s*router\.|\n\s*return router)/g;
for (const file of walk(join(root, 'src'))) {
  const src = readFileSync(file, 'utf8');
  for (const m of src.matchAll(re)) {
    const body = m[4];
    const real = /\b(pool|client|db)\.query\(|\bawait\s+\w+Service\.|withTransaction\(/.test(body);
    found.set(`${m[1].toUpperCase()} ${norm(m[3])}`, { file, real });
  }
}

const rows = inventory.map((e) => {
  const hit = found.get(`${e.method} ${norm(e.path)}`);
  return { method: e.method, path: e.path, source: e.file, node: !hit ? 'missing' : hit.real ? 'implemented' : 'placeholder' };
});
const count = (s) => rows.filter((r) => r.node === s).length;
const summary = { total: rows.length, implemented: count('implemented'), placeholder: count('placeholder'), missing: count('missing') };
writeFileSync(join(root, 'migration/parity-report.json'), JSON.stringify({ summary, rows }, null, 1));
console.log(JSON.stringify(summary));
if (process.argv.includes('--strict') && (summary.placeholder || summary.missing)) process.exit(1);
