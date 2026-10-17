import test from 'node:test';
import assert from 'node:assert/strict';
import { isSameOrigin, safeApiPath } from '../src/http/proxy.js';

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
