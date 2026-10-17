import test from 'node:test';
import assert from 'node:assert/strict';
import { checkUpstreamReadiness } from '../src/http/readiness.js';

test('readiness remains false when API_URL is missing', async () => {
  assert.deepEqual(await checkUpstreamReadiness(undefined), {
    ready: false,
    reason: 'upstream_api_not_configured',
  });
});

test('readiness requires a successful upstream health response', async () => {
  const result = await checkUpstreamReadiness('https://api.example.test', async (url, options) => {
    assert.equal(String(url), 'https://api.example.test/health');
    assert.equal(options.cache, 'no-store');
    assert.equal(options.redirect, 'error');
    return new Response('Healthy', { status: 200 });
  });
  assert.deepEqual(result, { ready: true, reason: null });
});

test('unhealthy upstream returns a safe status without its response body', async () => {
  const result = await checkUpstreamReadiness('https://api.example.test', async () => new Response('database password detail', { status: 503 }));
  assert.deepEqual(result, { ready: false, reason: 'upstream_api_unhealthy' });
  assert.doesNotMatch(JSON.stringify(result), /database password/);
});

test('network failures are handled without leaking internal errors', async () => {
  const result = await checkUpstreamReadiness('https://api.example.test', async () => { throw new Error('private network detail'); });
  assert.deepEqual(result, { ready: false, reason: 'upstream_api_unavailable' });
  assert.doesNotMatch(JSON.stringify(result), /private network detail/);
});

test('invalid upstream configuration is rejected safely', async () => {
  assert.deepEqual(await checkUpstreamReadiness('not a URL'), {
    ready: false,
    reason: 'upstream_api_not_configured',
  });
  assert.deepEqual(await checkUpstreamReadiness('file:///etc/passwd'), {
    ready: false,
    reason: 'upstream_api_not_configured',
  });
});
