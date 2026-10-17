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

describe('catalog, pricing and inventory', () => {
  it('rejects design and inventory access without the relevant permissions', async () => {
    const pool = {
      query: async (sql, values) => {
        if (sql.includes('FROM "UserSessions"')) return { rows: [{ '?column?': 1 }] };
        if (sql.includes('FROM "Memberships"')) return { rows: [{ UserId: '00000000-0000-4000-8000-000000000001', BusinessId: '00000000-0000-4000-8000-000000000002', Email: 'owner@example.test', FullName: 'Owner', BusinessName: 'Shop', TimeZoneId: 'UTC', CurrencyCode: 'USD', IsOwner: false, Permissions: ['orders.view'] }] };
        return { rows: [] };
      },
    };

    const designs = await requestApp(createApp(env, pool), '/api/designs', {
      headers: { authorization: `Bearer ${bearerToken()}` },
    });
    assert.equal(designs.status, 403);
    assert.equal(designs.body.code, 'forbidden');

    const inventory = await requestApp(createApp(env, pool), '/api/inventory', {
      headers: { authorization: `Bearer ${bearerToken()}` },
    });
    assert.equal(inventory.status, 403);
    assert.equal(inventory.body.code, 'forbidden');
  });

  it('allows catalog, pricing and inventory data to be read with the correct permissions', async () => {
    const pool = {
      query: async (sql, values) => {
        if (sql.includes('FROM "UserSessions"')) return { rows: [{ '?column?': 1 }] };
        if (sql.includes('FROM "Memberships"')) return { rows: [{ UserId: '00000000-0000-4000-8000-000000000001', BusinessId: '00000000-0000-4000-8000-000000000002', Email: 'owner@example.test', FullName: 'Owner', BusinessName: 'Shop', TimeZoneId: 'UTC', CurrencyCode: 'USD', IsOwner: true, Permissions: ['designs.view', 'designs.manage', 'pricing.view', 'pricing.manage', 'inventory.view', 'inventory.manage'] }] };
        return { rows: [] };
      },
    };

    const designs = await requestApp(createApp(env, pool), '/api/designs', {
      headers: { authorization: `Bearer ${bearerToken()}` },
    });
    assert.equal(designs.status, 200);
    assert.equal(Array.isArray(designs.body), true);

    const categories = await requestApp(createApp(env, pool), '/api/design-categories', {
      headers: { authorization: `Bearer ${bearerToken()}` },
    });
    assert.equal(categories.status, 200);
    assert.equal(Array.isArray(categories.body), true);

    const collections = await requestApp(createApp(env, pool), '/api/collections', {
      headers: { authorization: `Bearer ${bearerToken()}` },
    });
    assert.equal(collections.status, 200);
    assert.equal(Array.isArray(collections.body), true);

    const pricing = await requestApp(createApp(env, pool), '/api/pricing', {
      headers: { authorization: `Bearer ${bearerToken()}` },
    });
    assert.equal(pricing.status, 200);
    assert.equal(Array.isArray(pricing.body), true);

    const inventory = await requestApp(createApp(env, pool), '/api/inventory', {
      headers: { authorization: `Bearer ${bearerToken()}` },
    });
    assert.equal(inventory.status, 200);
    assert.equal(Array.isArray(inventory.body), true);
  });

  it('allows catalog and inventory writes when the permissions are present', async () => {
    const pool = {
      query: async (sql, values) => {
        if (sql.includes('FROM "UserSessions"')) return { rows: [{ '?column?': 1 }] };
        if (sql.includes('FROM "Memberships"')) return { rows: [{ UserId: '00000000-0000-4000-8000-000000000001', BusinessId: '00000000-0000-4000-8000-000000000002', Email: 'owner@example.test', FullName: 'Owner', BusinessName: 'Shop', TimeZoneId: 'UTC', CurrencyCode: 'USD', IsOwner: true, Permissions: ['designs.view', 'designs.manage', 'pricing.view', 'pricing.manage', 'inventory.view', 'inventory.manage'] }] };
        return { rows: [] };
      },
    };

    const createDesign = await requestApp(createApp(env, pool), '/api/designs', {
      method: 'POST',
      headers: { authorization: `Bearer ${bearerToken()}`, 'content-type': 'application/json' },
      body: JSON.stringify({ name: 'Tailored Jacket', categoryId: '00000000-0000-4000-8000-000000000010', basePrice: 280000, currencyCode: 'NGN' }),
    });
    assert.equal(createDesign.status, 201);
    assert.equal(createDesign.body.name, 'Tailored Jacket');

    const createCategory = await requestApp(createApp(env, pool), '/api/design-categories', {
      method: 'POST',
      headers: { authorization: `Bearer ${bearerToken()}`, 'content-type': 'application/json' },
      body: JSON.stringify({ name: 'Outerwear', description: 'Jackets and coats' }),
    });
    assert.equal(createCategory.status, 201);
    assert.equal(createCategory.body.name, 'Outerwear');

    const createCollection = await requestApp(createApp(env, pool), '/api/collections', {
      method: 'POST',
      headers: { authorization: `Bearer ${bearerToken()}`, 'content-type': 'application/json' },
      body: JSON.stringify({ name: 'Spring Edit', slug: 'spring-edit' }),
    });
    assert.equal(createCollection.status, 201);
    assert.equal(createCollection.body.name, 'Spring Edit');

    const createInventory = await requestApp(createApp(env, pool), '/api/inventory', {
      method: 'POST',
      headers: { authorization: `Bearer ${bearerToken()}`, 'content-type': 'application/json' },
      body: JSON.stringify({ sku: 'FAB-1001', name: 'Cotton Blend', unit: 'metre', onHand: 30, reorderLevel: 8, locationId: '00000000-0000-4000-8000-000000000020' }),
    });
    assert.equal(createInventory.status, 201);
    assert.equal(createInventory.body.sku, 'FAB-1001');
  });
});
