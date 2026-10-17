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
  return issueAccessToken({
    jwtSigningKey: env.JWT_SIGNING_KEY,
    jwtIssuer: env.JWT_ISSUER,
    jwtAudience: env.JWT_AUDIENCE,
    accessTokenMinutes: env.ACCESS_TOKEN_MINUTES,
  }, { userId, businessId, sessionId }).token;
}

describe('customers and measurements', () => {
  it('rejects customer access without customers.view permission', async () => {
    const pool = {
      query: async (sql, values) => {
        if (sql.includes('FROM "UserSessions"')) return { rows: [{ '?column?': 1 }] };
        if (sql.includes('FROM "Memberships"')) return { rows: [{ UserId: '00000000-0000-4000-8000-000000000001', BusinessId: '00000000-0000-4000-8000-000000000002', Email: 'owner@example.test', FullName: 'Owner', BusinessName: 'Shop', TimeZoneId: 'UTC', CurrencyCode: 'USD', IsOwner: false, Permissions: ['orders.view'] }] };
        return { rows: [] };
      },
    };

    const response = await requestApp(createApp(env, pool), '/api/customers', {
      headers: { authorization: `Bearer ${bearerToken()}` },
    });

    assert.equal(response.status, 403);
    assert.equal(response.body.code, 'forbidden');
  });

  it('allows customers and measurement templates to be listed and created with the correct permissions', async () => {
    const pool = {
      query: async (sql, values) => {
        if (sql.includes('FROM "UserSessions"')) return { rows: [{ '?column?': 1 }] };
        if (sql.includes('FROM "Memberships"')) return { rows: [{ UserId: '00000000-0000-4000-8000-000000000001', BusinessId: '00000000-0000-4000-8000-000000000002', Email: 'owner@example.test', FullName: 'Owner', BusinessName: 'Shop', TimeZoneId: 'UTC', CurrencyCode: 'USD', IsOwner: true, Permissions: ['customers.view', 'customers.manage', 'customers.merge', 'measurements.templates', 'measurements.record'] }] };
        return { rows: [] };
      },
    };

    const customers = await requestApp(createApp(env, pool), '/api/customers', {
      headers: { authorization: `Bearer ${bearerToken()}` },
    });
    assert.equal(customers.status, 200);
    assert.equal(Array.isArray(customers.body), true);

    const createCustomer = await requestApp(createApp(env, pool), '/api/customers', {
      method: 'POST',
      headers: { authorization: `Bearer ${bearerToken()}`, 'content-type': 'application/json' },
      body: JSON.stringify({ firstName: 'Nia', lastName: 'Adebayo', email: 'nia@example.test', phone: '+2348000000000' }),
    });
    assert.equal(createCustomer.status, 201);
    assert.equal(createCustomer.body.firstName, 'Nia');

    const templates = await requestApp(createApp(env, pool), '/api/measurement-templates', {
      headers: { authorization: `Bearer ${bearerToken()}` },
    });
    assert.equal(templates.status, 200);
    assert.equal(Array.isArray(templates.body), true);

    const createTemplate = await requestApp(createApp(env, pool), '/api/measurement-templates', {
      method: 'POST',
      headers: { authorization: `Bearer ${bearerToken()}`, 'content-type': 'application/json' },
      body: JSON.stringify({ name: 'Classic Shirt', fields: ['chest', 'waist'] }),
    });
    assert.equal(createTemplate.status, 201);
    assert.equal(createTemplate.body.name, 'Classic Shirt');
  });
});
