import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { createApp } from '../src/http/app.js';
import { issueAccessToken } from '../src/security/jwt.js';

const env = {
  NODE_ENV: 'test',
  PORT: 8081,
  HOST: '127.0.0.1',
  LOG_LEVEL: 'silent',
  JWT_SIGNING_KEY: 'test-signing-key-with-at-least-thirty-two-characters',
  JWT_ISSUER: 'avzar-api',
  JWT_AUDIENCE: 'avzar-mobile',
  ACCESS_TOKEN_MINUTES: 10,
};

async function requestApp(app, pathname, init = {}) {
  const server = createServer(app);
  await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
  const address = server.address();
  try {
    const response = await fetch(`http://127.0.0.1:${address.port}${pathname}`, init);
    const text = await response.text();
    let body = text;
    try { body = JSON.parse(text); } catch { /* ignore */ }
    return { status: response.status, body, text, headers: response.headers };
  } finally {
    await new Promise((resolve, reject) => server.close((error) => error ? reject(error) : resolve()));
  }
}

function bearerToken(userId = '00000000-0000-4000-8000-000000000001', businessId = '00000000-0000-4000-8000-000000000002', sessionId = '00000000-0000-4000-8000-000000000003') {
  return issueAccessToken({ jwtSigningKey: env.JWT_SIGNING_KEY, jwtIssuer: env.JWT_ISSUER, jwtAudience: env.JWT_AUDIENCE, accessTokenMinutes: env.ACCESS_TOKEN_MINUTES }, { userId, businessId, sessionId }).token;
}

describe('billing and AI', () => {
  it('rejects billing and AI access without the required permissions', async () => {
    const pool = {
      query: async (sql) => {
        if (sql.includes('FROM "UserSessions"')) return { rows: [{ '?column?': 1 }] };
        if (sql.includes('FROM "Memberships"')) return { rows: [{ UserId: '00000000-0000-4000-8000-000000000001', BusinessId: '00000000-0000-4000-8000-000000000002', Email: 'owner@example.test', FullName: 'Owner', BusinessName: 'Shop', TimeZoneId: 'UTC', CurrencyCode: 'USD', IsOwner: false, Permissions: ['customers.view'] }] };
        return { rows: [] };
      },
    };

    const plans = await requestApp(createApp(env, pool), '/api/plans', { headers: { authorization: `Bearer ${bearerToken()}` } });
    assert.equal(plans.status, 403);
    assert.equal(plans.body.code, 'forbidden');

    const ai = await requestApp(createApp(env, pool), '/api/ai/assist', { headers: { authorization: `Bearer ${bearerToken()}` } });
    assert.equal(ai.status, 403);
    assert.equal(ai.body.code, 'forbidden');
  });

  it('allows billing and AI routes to be read and created with the right permissions', async () => {
    const pool = {
      query: async (sql) => {
        if (sql.includes('FROM "UserSessions"')) return { rows: [{ '?column?': 1 }] };
        if (sql.includes('FROM "Memberships"')) return { rows: [{ UserId: '00000000-0000-4000-8000-000000000001', BusinessId: '00000000-0000-4000-8000-000000000002', Email: 'owner@example.test', FullName: 'Owner', BusinessName: 'Shop', TimeZoneId: 'UTC', CurrencyCode: 'USD', IsOwner: true, Permissions: ['plans.view', 'plans.manage', 'billing.view', 'billing.manage', 'ai.view', 'ai.use'] }] };
        return { rows: [] };
      },
    };

    const plans = await requestApp(createApp(env, pool), '/api/plans', { headers: { authorization: `Bearer ${bearerToken()}` } });
    assert.equal(plans.status, 200);
    assert.equal(Array.isArray(plans.body), true);

    const plan = await requestApp(createApp(env, pool), '/api/plans', {
      method: 'POST',
      headers: { authorization: `Bearer ${bearerToken()}`, 'content-type': 'application/json' },
      body: JSON.stringify({ name: 'Pro', price: 200000, currencyCode: 'NGN', features: ['fittings', 'ai'] }),
    });
    assert.equal(plan.status, 201);
    assert.equal(plan.body.name, 'Pro');

    const billing = await requestApp(createApp(env, pool), '/api/billing', { headers: { authorization: `Bearer ${bearerToken()}` } });
    assert.equal(billing.status, 200);
    assert.equal(Array.isArray(billing.body), true);

    const ai = await requestApp(createApp(env, pool), '/api/ai/assist', {
      method: 'POST',
      headers: { authorization: `Bearer ${bearerToken()}`, 'content-type': 'application/json' },
      body: JSON.stringify({ prompt: 'Write a fitting summary', context: { businessId: '00000000-0000-4000-8000-000000000002' } }),
    });
    assert.equal(ai.status, 200);
    assert.equal(typeof ai.body.answer, 'string');
  });
});
