import express from 'express';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { loadEnv } from '../config/env.js';
import { proxyApiRequest } from './proxy.js';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const PWA_ROOT = path.resolve(HERE, '../../../pwa');

export function createApp(env = loadEnv()) {
  const app = express();
  app.disable('x-powered-by');
  app.set('trust proxy', false);

  app.use((_req, res, next) => {
    res.setHeader('x-content-type-options', 'nosniff');
    res.setHeader('referrer-policy', 'strict-origin-when-cross-origin');
    res.setHeader('permissions-policy', 'camera=(), microphone=(), geolocation=()');
    res.setHeader('content-security-policy', "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'; form-action 'self'");
    next();
  });

  app.get('/health/live', (_req, res) => res.status(200).json({ status: 'live', service: 'avzar-pwa-node' }));
  app.get('/health/ready', (_req, res) => res.status(503).json({ status: 'not_ready', reason: 'node_database_migration_and_cutover_checks_not_implemented' }));
  app.get('/api/v1', (_req, res) => res.status(200).json({ service: 'avzar-pwa-node', migrationStage: 'pwa-foundation', productionCutoverEnabled: false }));

  // Serve only the explicit static PWA shell. API requests are never served from cache.
  app.use(express.static(PWA_ROOT, {
    index: 'index.html',
    fallthrough: true,
    etag: true,
    maxAge: env.NODE_ENV === 'production' ? '1h' : 0,
    setHeaders(res, filePath) {
      if (filePath.endsWith('sw.js') || filePath.endsWith('manifest.webmanifest') || filePath.endsWith('index.html')) {
        res.setHeader('cache-control', 'no-cache');
      }
    },
  }));

  // Keep the existing ASP.NET API authoritative during the migration. Forward only /api routes,
  // allowlisted headers, same-origin browser writes, and no provider webhooks.
  app.use('/backend', (req, res) => proxyApiRequest(req, res, env.API_URL, env.TRUST_PROXY_HEADERS));

  app.get(/.*/, (req, res, next) => {
    if (req.path.startsWith('/api/') || req.path.startsWith('/health/') || req.path.startsWith('/backend/')) return next();
    res.sendFile(path.join(PWA_ROOT, 'index.html'));
  });

  app.use((req, res) => res.status(404).json({ code: 'not_found', message: 'The requested resource was not found.' }));
  app.use((error, _req, res, _next) => {
    const status = Number.isInteger(error?.status) && error.status >= 400 && error.status < 500 ? error.status : 500;
    res.status(status).json({ code: 'internal_error', message: status >= 500 ? 'An unexpected server error occurred.' : 'The request could not be processed.' });
  });
  return app;
}
