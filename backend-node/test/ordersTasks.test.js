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

describe('orders and tasks', () => {
  it('rejects order and task access without the required permissions', async () => {
    const pool = {
      query: async (sql, values) => {
        if (sql.includes('FROM "UserSessions"')) return { rows: [{ '?column?': 1 }] };
        if (sql.includes('FROM "Memberships"')) return { rows: [{ UserId: '00000000-0000-4000-8000-000000000001', BusinessId: '00000000-0000-4000-8000-000000000002', Email: 'owner@example.test', FullName: 'Owner', BusinessName: 'Shop', TimeZoneId: 'UTC', CurrencyCode: 'USD', IsOwner: false, Permissions: ['customers.view'] }] };
        return { rows: [] };
      },
    };

    const orders = await requestApp(createApp(env, pool), '/api/orders', {
      headers: { authorization: `Bearer ${bearerToken()}` },
    });
    assert.equal(orders.status, 403);
    assert.equal(orders.body.code, 'forbidden');

    const tasks = await requestApp(createApp(env, pool), '/api/tasks', {
      headers: { authorization: `Bearer ${bearerToken()}` },
    });
    assert.equal(tasks.status, 403);
    assert.equal(tasks.body.code, 'forbidden');
  });

  it('allows orders, workflows, fittings and tasks to be read and created with the right permissions', async () => {
    const pool = {
      query: async (sql, values) => {
        if (sql.includes('FROM "UserSessions"')) return { rows: [{ '?column?': 1 }] };
        if (sql.includes('FROM "Memberships"')) return { rows: [{ UserId: '00000000-0000-4000-8000-000000000001', BusinessId: '00000000-0000-4000-8000-000000000002', Email: 'owner@example.test', FullName: 'Owner', BusinessName: 'Shop', TimeZoneId: 'UTC', CurrencyCode: 'USD', IsOwner: true, Permissions: ['orders.view', 'orders.manage', 'orders.confirm', 'fittings.manage', 'tasks.view', 'tasks.manage', 'tasks.assign', 'tasks.override', 'tasks.work', 'workflows.manage'] }] };
        return { rows: [] };
      },
    };

    const orders = await requestApp(createApp(env, pool), '/api/orders', {
      headers: { authorization: `Bearer ${bearerToken()}` },
    });
    assert.equal(orders.status, 200);
    assert.equal(Array.isArray(orders.body), true);

    const createOrder = await requestApp(createApp(env, pool), '/api/orders', {
      method: 'POST',
      headers: { authorization: `Bearer ${bearerToken()}`, 'content-type': 'application/json' },
      body: JSON.stringify({ customerId: '00000000-0000-4000-8000-000000000010', items: [{ designId: '00000000-0000-4000-8000-000000000020', quantity: 1 }], notes: 'Rush order' }),
    });
    assert.equal(createOrder.status, 201);
    assert.equal(createOrder.body.customerId, '00000000-0000-4000-8000-000000000010');

    const workflows = await requestApp(createApp(env, pool), '/api/workflows', {
      headers: { authorization: `Bearer ${bearerToken()}` },
    });
    assert.equal(workflows.status, 200);
    assert.equal(Array.isArray(workflows.body), true);

    const fittings = await requestApp(createApp(env, pool), '/api/fittings', {
      headers: { authorization: `Bearer ${bearerToken()}` },
    });
    assert.equal(fittings.status, 200);
    assert.equal(Array.isArray(fittings.body), true);

    const tasks = await requestApp(createApp(env, pool), '/api/tasks', {
      headers: { authorization: `Bearer ${bearerToken()}` },
    });
    assert.equal(tasks.status, 200);
    assert.equal(Array.isArray(tasks.body), true);

    const createTask = await requestApp(createApp(env, pool), '/api/tasks', {
      method: 'POST',
      headers: { authorization: `Bearer ${bearerToken()}`, 'content-type': 'application/json' },
      body: JSON.stringify({ title: 'Alter hem', orderId: '00000000-0000-4000-8000-000000000030', assigneeMembershipId: '00000000-0000-4000-8000-000000000040' }),
    });
    assert.equal(createTask.status, 201);
    assert.equal(createTask.body.title, 'Alter hem');
  });
});
