import express from 'express';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { loadEnv } from '../config/env.js';
import { createAccountRouter } from '../auth/accountRoutes.js';
import { createBillingRouter } from '../auth/billingRoutes.js';
import { createBusinessSettingsRouter } from '../auth/businessSettings.js';
import { createCatalogRouter } from '../auth/catalogRoutes.js';
import { createCustomerRouter } from '../auth/customerRoutes.js';
import { createFinanceRouter } from '../auth/financeRoutes.js';
import { createMediaRouter } from '../auth/mediaRoutes.js';
import { createMobileAuthRouter } from '../auth/mobileAuth.js';
import { createOrderRouter } from '../auth/orderRoutes.js';
import { createStaffRouter } from '../auth/staffRoutes.js';
import { createStorefrontRouter } from '../auth/storefrontRoutes.js';
import { errorHandler } from './errors.js';
import { checkDatabaseReadiness } from './readiness.js';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const PWA_ROOT = path.resolve(HERE, '../../../pwa');

export function createApp(env = loadEnv(), dbPool = null) {
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
  app.use(express.json({ limit: '1mb', strict: true }));

  app.get('/health/live', (_req, res) => res.status(200).json({ status: 'live', service: 'avzar-pwa-node' }));
  app.get('/health/ready', async (_req, res) => {
    const result = await checkDatabaseReadiness(dbPool);
    if (!result.ready) return res.status(503).json({ status: 'not_ready', reason: result.reason });
    return res.status(200).json({ status: 'ready', service: 'avzar-node', database: 'healthy' });
  });
  app.get('/api/v1', (_req, res) => res.status(200).json({ service: 'avzar-node' }));
  app.use('/api', createAccountRouter({ env, pool: dbPool }));
  app.use('/api', createBusinessSettingsRouter({ env, pool: dbPool }));
  app.use('/api', createStaffRouter({ env, pool: dbPool }));
  app.use('/api', createCustomerRouter({ env, pool: dbPool }));
  app.use('/api', createCatalogRouter({ env, pool: dbPool }));
  app.use('/api', createOrderRouter({ env, pool: dbPool }));
  app.use('/api', createFinanceRouter({ env, pool: dbPool }));
  app.use('/api', createMediaRouter({ env, pool: dbPool }));
  app.use('/api', createStorefrontRouter({ env, pool: dbPool }));
  app.use('/api', createBillingRouter({ env, pool: dbPool }));
  app.use('/api', createMobileAuthRouter({ env, pool: dbPool }));

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

  app.get(/.*/, (req, res, next) => {
    if (req.path.startsWith('/api/') || req.path.startsWith('/health/') || req.path.startsWith('/backend/')) return next();
    res.sendFile(path.join(PWA_ROOT, 'index.html'));
  });

  app.use((req, res) => res.status(404).json({ code: 'not_found', message: 'The requested resource was not found.' }));
  app.use(errorHandler());
  return app;
}
