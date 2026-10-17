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

describe('storefront and portal', () => {
  it('rejects storefront and inquiry access without the required permissions', async () => {
    const pool = {
      query: async (sql, values) => {
        if (sql.includes('FROM "UserSessions"')) return { rows: [{ '?column?': 1 }] };
        if (sql.includes('FROM "Memberships"')) return { rows: [{ UserId: '00000000-0000-4000-8000-000000000001', BusinessId: '00000000-0000-4000-8000-000000000002', Email: 'owner@example.test', FullName: 'Owner', BusinessName: 'Shop', TimeZoneId: 'UTC', CurrencyCode: 'USD', IsOwner: false, Permissions: ['customers.view'] }] };
        return { rows: [] };
      },
    };

    const storefronts = await requestApp(createApp(env, pool), '/api/storefronts', {
      headers: { authorization: `Bearer ${bearerToken()}` },
    });
    assert.equal(storefronts.status, 403);
    assert.equal(storefronts.body.code, 'forbidden');

    const inquiries = await requestApp(createApp(env, pool), '/api/inquiries', {
      headers: { authorization: `Bearer ${bearerToken()}` },
    });
    assert.equal(inquiries.status, 403);
    assert.equal(inquiries.body.code, 'forbidden');
  });

  it('allows storefronts, public site, and inquiries to be read and created with the right permissions', async () => {
    const pool = {
      query: async (sql, values) => {
        if (sql.includes('FROM "UserSessions"')) return { rows: [{ '?column?': 1 }] };
        if (sql.includes('FROM "Memberships"')) return { rows: [{ UserId: '00000000-0000-4000-8000-000000000001', BusinessId: '00000000-0000-4000-8000-000000000002', Email: 'owner@example.test', FullName: 'Owner', BusinessName: 'Shop', TimeZoneId: 'UTC', CurrencyCode: 'USD', IsOwner: true, Permissions: ['storefront.view', 'storefront.edit', 'storefront.publish', 'inquiries.view', 'inquiries.manage'] }] };
        return { rows: [] };
      },
    };

    const storefronts = await requestApp(createApp(env, pool), '/api/storefronts', {
      headers: { authorization: `Bearer ${bearerToken()}` },
    });
    assert.equal(storefronts.status, 200);
    assert.equal(Array.isArray(storefronts.body), true);

    const createStorefront = await requestApp(createApp(env, pool), '/api/storefronts', {
      method: 'POST',
      headers: { authorization: `Bearer ${bearerToken()}`, 'content-type': 'application/json' },
      body: JSON.stringify({ name: 'Studio storefront', slug: 'studio-storefront', status: 'draft' }),
    });
    assert.equal(createStorefront.status, 201);
    assert.equal(createStorefront.body.name, 'Studio storefront');

    const publicSite = await requestApp(createApp(env, pool), '/api/public/site', {
      headers: { authorization: `Bearer ${bearerToken()}` },
    });
    assert.equal(publicSite.status, 200);
    assert.equal(publicSite.body.siteName, 'Shop');

    const inquiries = await requestApp(createApp(env, pool), '/api/inquiries', {
      headers: { authorization: `Bearer ${bearerToken()}` },
    });
    assert.equal(inquiries.status, 200);
    assert.equal(Array.isArray(inquiries.body), true);

    const createInquiry = await requestApp(createApp(env, pool), '/api/inquiries', {
      method: 'POST',
      headers: { authorization: `Bearer ${bearerToken()}`, 'content-type': 'application/json' },
      body: JSON.stringify({ customerName: 'Jane Doe', email: 'jane@example.test', subject: 'Custom order', message: 'Need a bridal gown' }),
    });
    assert.equal(createInquiry.status, 201);
    assert.equal(createInquiry.body.subject, 'Custom order');
  });
});
