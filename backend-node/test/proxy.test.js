import test from 'node:test';
import assert from 'node:assert/strict';
import { isSameOrigin, requestBodyTooLarge, safeApiPath } from '../src/http/proxy.js';

test('safeApiPath allows only safe API routes', () => {
  assert.equal(safeApiPath('/backend/api/auth/me'), 'api/auth/me');
  assert.equal(safeApiPath('/backend/api/customers/00000000-0000-0000-0000-000000000000'), 'api/customers/00000000-0000-0000-0000-000000000000');
  assert.equal(safeApiPath('/backend/api/webhooks/email/provider'), null);
  assert.equal(safeApiPath('/backend/api/billing/subscription/webhook'), null);
  assert.equal(safeApiPath('/backend/api/../health'), null);
  assert.equal(safeApiPath('/backend/public/media/a'), null);
  assert.equal(safeApiPath('/backend/api/customers/%2fadmin'), null);
});

test('isSameOrigin refuses a present cross-origin origin', () => {
  assert.equal(isSameOrigin('https://avzar.example', 'avzar.example'), true);
  assert.equal(isSameOrigin('https://evil.example', 'avzar.example'), false);
  assert.equal(isSameOrigin('not a URL', 'avzar.example'), false);
  assert.equal(isSameOrigin(null, 'avzar.example'), true);
});


test('requestBodyTooLarge validates declared content length and handles chunked requests', () => {
  assert.equal(requestBodyTooLarge(undefined, 1024), false);
  assert.equal(requestBodyTooLarge('', 1024), false);
  assert.equal(requestBodyTooLarge('1024', 1024), false);
  assert.equal(requestBodyTooLarge('1025', 1024), true);
  assert.equal(requestBodyTooLarge('NaN', 1024), true);
  assert.equal(requestBodyTooLarge('-1', 1024), true);
});

test('proxy rejects oversized declared bodies before calling the upstream', async () => {
  const { proxyApiRequest } = await import('../src/http/proxy.js');
  let called = false;
  const response = { statusCode: 200, headersSent: false, status(code) { this.statusCode = code; return this; }, json(body) { this.body = body; this.headersSent = true; } };
  const req = {
    originalUrl: '/backend/api/customers', method: 'POST', headers: { 'content-length': '2048' },
    get(name) { return ({ origin: 'https://avzar.example', host: 'avzar.example' })[name]; },
  };
  await proxyApiRequest(req, response, 'https://api.example.test', false, 1024, 1000, async () => { called = true; return new Response('ok'); });
  assert.equal(response.statusCode, 413);
  assert.equal(response.body.code, 'request_body_too_large');
  assert.equal(called, false);
});

test('proxy enforces the body limit for chunked requests too', async () => {
  const { proxyApiRequest } = await import('../src/http/proxy.js');
  const response = { statusCode: 200, headersSent: false, status(code) { this.statusCode = code; return this; }, json(body) { this.body = body; this.headersSent = true; }, setHeader() {}, appendHeader() {}, end() {} };
  const req = {
    originalUrl: '/backend/api/customers', method: 'POST', headers: {},
    get(name) { return ({ origin: 'https://avzar.example', host: 'avzar.example' })[name]; },
    async *[Symbol.asyncIterator]() { yield Buffer.alloc(600); yield Buffer.alloc(600); },
  };
  const fakeFetch = async (_url, options) => {
    for await (const _chunk of options.body) { /* consume streamed body */ }
    return new Response('ok');
  };
  await proxyApiRequest(req, response, 'https://api.example.test', false, 1024, 1000, fakeFetch);
  assert.equal(response.statusCode, 413);
  assert.equal(response.body.code, 'request_body_too_large');
});

test('proxy forwards allowlisted request headers and preserves query strings', async () => {
  const { proxyApiRequest } = await import('../src/http/proxy.js');
  const response = { statusCode: 0, headersSent: false, status(code) { this.statusCode = code; return this; }, setHeader() {}, appendHeader() {}, end() { this.ended = true; } };
  const req = {
    originalUrl: '/backend/api/customers?page=2&search=Sam', method: 'GET',
    headers: { accept: 'application/json', cookie: 'session=abc', authorization: 'should-not-forward' },
    get(name) { return ({ origin: 'https://avzar.example', host: 'avzar.example' })[name]; },
  };
  let requestedUrl;
  let forwardedHeaders;
  await proxyApiRequest(req, response, 'https://api.example.test', false, 1024, 1000, async (url, options) => {
    requestedUrl = String(url);
    forwardedHeaders = options.headers;
    return new Response(null, { status: 204 });
  });
  assert.equal(requestedUrl, 'https://api.example.test/api/customers?page=2&search=Sam');
  assert.equal(forwardedHeaders.get('accept'), 'application/json');
  assert.equal(forwardedHeaders.get('cookie'), 'session=abc');
  assert.equal(forwardedHeaders.get('authorization'), null);
  assert.equal(response.statusCode, 204);
  assert.equal(response.ended, true);
});
