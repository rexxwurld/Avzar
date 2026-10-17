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

describe('media and documents', () => {
  it('rejects media and document access without the required permissions', async () => {
    const pool = {
      query: async (sql, values) => {
        if (sql.includes('FROM "UserSessions"')) return { rows: [{ '?column?': 1 }] };
        if (sql.includes('FROM "Memberships"')) return { rows: [{ UserId: '00000000-0000-4000-8000-000000000001', BusinessId: '00000000-0000-4000-8000-000000000002', Email: 'owner@example.test', FullName: 'Owner', BusinessName: 'Shop', TimeZoneId: 'UTC', CurrencyCode: 'USD', IsOwner: false, Permissions: ['customers.view'] }] };
        return { rows: [] };
      },
    };

    const media = await requestApp(createApp(env, pool), '/api/media', {
      headers: { authorization: `Bearer ${bearerToken()}` },
    });
    assert.equal(media.status, 403);
    assert.equal(media.body.code, 'forbidden');

    const documents = await requestApp(createApp(env, pool), '/api/documents', {
      headers: { authorization: `Bearer ${bearerToken()}` },
    });
    assert.equal(documents.status, 403);
    assert.equal(documents.body.code, 'forbidden');
  });

  it('allows media, documents, and pdf routes to be listed and created with the right permissions', async () => {
    const pool = {
      query: async (sql, values) => {
        if (sql.includes('FROM "UserSessions"')) return { rows: [{ '?column?': 1 }] };
        if (sql.includes('FROM "Memberships"')) return { rows: [{ UserId: '00000000-0000-4000-8000-000000000001', BusinessId: '00000000-0000-4000-8000-000000000002', Email: 'owner@example.test', FullName: 'Owner', BusinessName: 'Shop', TimeZoneId: 'UTC', CurrencyCode: 'USD', IsOwner: true, Permissions: ['media.view', 'media.manage', 'documents.view', 'documents.manage', 'pdf.view', 'pdf.generate'] }] };
        return { rows: [] };
      },
    };

    const media = await requestApp(createApp(env, pool), '/api/media', {
      headers: { authorization: `Bearer ${bearerToken()}` },
    });
    assert.equal(media.status, 200);
    assert.equal(Array.isArray(media.body), true);

    const createMedia = await requestApp(createApp(env, pool), '/api/media', {
      method: 'POST',
      headers: { authorization: `Bearer ${bearerToken()}`, 'content-type': 'application/json' },
      body: JSON.stringify({ fileName: 'front.png', mimeType: 'image/png', sizeBytes: 2048, url: 'https://cdn.example.test/front.png' }),
    });
    assert.equal(createMedia.status, 201);
    assert.equal(createMedia.body.fileName, 'front.png');

    const documents = await requestApp(createApp(env, pool), '/api/documents', {
      headers: { authorization: `Bearer ${bearerToken()}` },
    });
    assert.equal(documents.status, 200);
    assert.equal(Array.isArray(documents.body), true);

    const createDocument = await requestApp(createApp(env, pool), '/api/documents', {
      method: 'POST',
      headers: { authorization: `Bearer ${bearerToken()}`, 'content-type': 'application/json' },
      body: JSON.stringify({ title: 'Measurement Sheet', category: 'measurement', mimeType: 'application/pdf', url: 'https://cdn.example.test/sheet.pdf' }),
    });
    assert.equal(createDocument.status, 201);
    assert.equal(createDocument.body.title, 'Measurement Sheet');

    const pdf = await requestApp(createApp(env, pool), '/api/pdf/00000000-0000-4000-8000-000000000030', {
      headers: { authorization: `Bearer ${bearerToken()}` },
    });
    assert.equal(pdf.status, 200);
    assert.equal(pdf.body.contentType, 'application/pdf');
  });
});
