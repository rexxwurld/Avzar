import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { createApp } from '../src/http/app.js';
import { isSameOrigin, safeApiPath } from '../src/http/proxy.js';

const testEnv = { NODE_ENV: 'test', PORT: 8081, HOST: '127.0.0.1', LOG_LEVEL: 'silent' };

async function requestApp(app, pathname) {
  const server = createServer(app);
  await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
  const address = server.address();
  try {
    const response = await fetch(`http://127.0.0.1:${address.port}${pathname}`);
    const text = await response.text();
    let body = text;
    try { body = JSON.parse(text); } catch { /* Static HTML and plain text remain strings. */ }
    return { status: response.status, body, text, headers: response.headers };
  } finally {
    await new Promise((resolve, reject) => server.close((error) => error ? reject(error) : resolve()));
  }
}

describe('Avzar PWA Node foundation', () => {
  it('exposes liveness and reports not ready when the upstream API is not configured', async () => {
    const app = createApp(testEnv);
    const live = await requestApp(app, '/health/live');
    assert.equal(live.status, 200);
    assert.equal(live.body.status, 'live');
    const ready = await requestApp(app, '/health/ready');
    assert.equal(ready.status, 503);
    assert.equal(ready.body.status, 'not_ready');
    assert.equal(ready.body.reason, 'upstream_api_not_configured');
  });

  it('reports ready only when the existing API health endpoint succeeds', async () => {
    let requestedUrl;
    const app = createApp({ ...testEnv, API_URL: 'https://api.example.test/base' }, async (url, options) => {
      requestedUrl = String(url);
      assert.equal(options.method, 'GET');
      assert.equal(options.cache, 'no-store');
      return new Response('Healthy', { status: 200 });
    });
    const ready = await requestApp(app, '/health/ready');
    assert.equal(requestedUrl, 'https://api.example.test/health');
    assert.equal(ready.status, 200);
    assert.equal(ready.body.status, 'ready');
    assert.equal(ready.body.upstream, 'healthy');
  });

  it('returns a safe 503 when the existing API is unhealthy or unavailable', async () => {
    const unhealthy = createApp({ ...testEnv, API_URL: 'https://api.example.test' }, async () => new Response('database detail', { status: 503 }));
    const unhealthyResponse = await requestApp(unhealthy, '/health/ready');
    assert.equal(unhealthyResponse.status, 503);
    assert.deepEqual(unhealthyResponse.body, { status: 'not_ready', reason: 'upstream_api_unhealthy' });

    const unavailable = createApp({ ...testEnv, API_URL: 'https://api.example.test' }, async () => { throw new Error('private upstream detail'); });
    const unavailableResponse = await requestApp(unavailable, '/health/ready');
    assert.equal(unavailableResponse.status, 503);
    assert.deepEqual(unavailableResponse.body, { status: 'not_ready', reason: 'upstream_api_unavailable' });
    assert.doesNotMatch(JSON.stringify(unavailableResponse.body), /private upstream detail/);
  });

  it('serves the real PWA shell and manifest', async () => {
    const app = createApp(testEnv);
    const home = await requestApp(app, '/');
    assert.equal(home.status, 200);
    assert.match(home.text, /Avzar/);
    const manifest = await requestApp(app, '/manifest.webmanifest');
    assert.equal(manifest.status, 200);
    assert.match(manifest.headers.get('content-type'), /json/);
    assert.match(manifest.text, /"display": "standalone"/);
  });

  it('does not fabricate migrated API functionality', async () => {
    const response = await requestApp(createApp(testEnv), '/backend/api/customers');
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
