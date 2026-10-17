import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import request from 'supertest';
import { createApp } from '../src/http/app.js';
import { isSameOrigin, safeApiPath } from '../src/http/proxy.js';

const testEnv = { NODE_ENV: 'test', PORT: 8081, HOST: '127.0.0.1', LOG_LEVEL: 'silent' };

describe('Avzar PWA Node foundation', () => {
  it('exposes liveness without claiming production readiness', async () => {
    const app = createApp(testEnv);
    const live = await request(app).get('/health/live');
    assert.equal(live.status, 200);
    assert.equal(live.body.status, 'live');
    const ready = await request(app).get('/health/ready');
    assert.equal(ready.status, 503);
    assert.equal(ready.body.status, 'not_ready');
  });

  it('serves the real PWA shell and manifest', async () => {
    const app = createApp(testEnv);
    const home = await request(app).get('/');
    assert.equal(home.status, 200);
    assert.match(home.text, /Avzar/);
    const manifest = await request(app).get('/manifest.webmanifest');
    assert.equal(manifest.status, 200);
    assert.match(manifest.headers['content-type'], /json/);
    assert.match(manifest.text, /"display": "standalone"/);
  });

  it('does not fabricate migrated API functionality', async () => {
    const response = await request(createApp(testEnv)).get('/backend/api/customers');
    assert.equal(response.status, 503);
    assert.equal(response.body.code, 'upstream_not_configured');
  });

  it('only permits safe API paths and blocks provider webhooks', () => {
    assert.equal(safeApiPath('/backend/api/auth/me'), 'api/auth/me');
    assert.equal(safeApiPath('/backend/api/customers/abc'), 'api/customers/abc');
    assert.equal(safeApiPath('/backend/api/webhooks/email/provider'), null);
    assert.equal(safeApiPath('/backend/api/billing/subscription/webhook'), null);
    assert.equal(safeApiPath('/backend/api/../health'), null);
    assert.equal(safeApiPath('/backend/public/media/a'), null);
  });

  it('rejects a present cross-origin origin on browser writes', () => {
    assert.equal(isSameOrigin('https://avzar.example', 'avzar.example'), true);
    assert.equal(isSameOrigin('https://evil.example', 'avzar.example'), false);
    assert.equal(isSameOrigin('not a URL', 'avzar.example'), false);
  });
});
