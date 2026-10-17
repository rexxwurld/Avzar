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

describe('finance', () => {
  it('rejects invoice and expense access without the required permissions', async () => {
    const pool = {
      query: async (sql, values) => {
        if (sql.includes('FROM "UserSessions"')) return { rows: [{ '?column?': 1 }] };
        if (sql.includes('FROM "Memberships"')) return { rows: [{ UserId: '00000000-0000-4000-8000-000000000001', BusinessId: '00000000-0000-4000-8000-000000000002', Email: 'owner@example.test', FullName: 'Owner', BusinessName: 'Shop', TimeZoneId: 'UTC', CurrencyCode: 'USD', IsOwner: false, Permissions: ['customers.view'] }] };
        return { rows: [] };
      },
    };

    const invoices = await requestApp(createApp(env, pool), '/api/invoices', {
      headers: { authorization: `Bearer ${bearerToken()}` },
    });
    assert.equal(invoices.status, 403);
    assert.equal(invoices.body.code, 'forbidden');

    const expenses = await requestApp(createApp(env, pool), '/api/expenses', {
      headers: { authorization: `Bearer ${bearerToken()}` },
    });
    assert.equal(expenses.status, 403);
    assert.equal(expenses.body.code, 'forbidden');
  });

  it('allows invoices, payments and expenses to be read and created with the right permissions', async () => {
    const pool = {
      query: async (sql, values) => {
        if (sql.includes('FROM "UserSessions"')) return { rows: [{ '?column?': 1 }] };
        if (sql.includes('FROM "Memberships"')) return { rows: [{ UserId: '00000000-0000-4000-8000-000000000001', BusinessId: '00000000-0000-4000-8000-000000000002', Email: 'owner@example.test', FullName: 'Owner', BusinessName: 'Shop', TimeZoneId: 'UTC', CurrencyCode: 'USD', IsOwner: true, Permissions: ['invoices.view', 'invoices.manage', 'payments.view', 'payments.record', 'payments.verify', 'expenses.view', 'expenses.manage'] }] };
        if (sql.includes('FROM "Plans" p')) return { rows: [{ Value: 'true' }] };
        if (sql.includes('SELECT 1 FROM "ExpenseCategories"')) return { rows: [{ '?column?': 1 }] };
        if (sql.includes('FROM "ExpenseCategories"')) return { rows: [{ Id: '00000000-0000-4000-8000-000000000050', Name: 'Fabric & materials', IsActive: true, SortOrder: 0 }] };
        if (sql.includes('count(*)') && sql.includes('FROM "Expenses"')) return { rows: [{ TotalCount: 0 }] };
        if (sql.includes('FROM "Expenses"')) return { rows: [] };
        if (sql.includes('FROM "Businesses"')) return { rows: [{ CurrencyCode: 'USD', TimeZoneId: 'UTC' }] };
        return { rows: [] };
      },
      connect: async () => ({
        query: async (sql, values) => {
          if (sql.includes('INSERT INTO "Expenses"')) return { rows: [{
            Id: values[0],
            CategoryId: values[2],
            ExpenseDate: values[4],
            Amount: String(values[5]),
            CurrencyCode: values[6],
            Description: values[7],
            Vendor: values[8],
            Reference: values[9],
            VoidedAtUtc: null,
            VoidReason: null,
            LocationId: values[3],
            CreatedAtUtc: '2026-10-17T12:00:00.000Z',
            Version: '1',
          }] };
          if (sql.includes('SELECT "Name" AS "CategoryName"')) return { rows: [{ CategoryName: 'Fabric & materials' }] };
          return { rows: [] };
        },
        release() {},
      }),
    };

    const invoices = await requestApp(createApp(env, pool), '/api/invoices', {
      headers: { authorization: `Bearer ${bearerToken()}` },
    });
    assert.equal(invoices.status, 200);
    assert.equal(Array.isArray(invoices.body), true);

    const createInvoice = await requestApp(createApp(env, pool), '/api/invoices', {
      method: 'POST',
      headers: { authorization: `Bearer ${bearerToken()}`, 'content-type': 'application/json' },
      body: JSON.stringify({ customerId: '00000000-0000-4000-8000-000000000010', amount: 250000, currencyCode: 'NGN', dueDate: '2025-07-01', notes: 'Alteration invoice' }),
    });
    assert.equal(createInvoice.status, 201);
    assert.equal(createInvoice.body.customerId, '00000000-0000-4000-8000-000000000010');

    const payments = await requestApp(createApp(env, pool), '/api/payments', {
      headers: { authorization: `Bearer ${bearerToken()}` },
    });
    assert.equal(payments.status, 200);
    assert.equal(Array.isArray(payments.body), true);

    const recordPayment = await requestApp(createApp(env, pool), '/api/invoices/' + '00000000-0000-4000-8000-000000000030' + '/payments', {
      method: 'POST',
      headers: { authorization: `Bearer ${bearerToken()}`, 'content-type': 'application/json' },
      body: JSON.stringify({ amount: 50000, method: 'bank_transfer', reference: 'REF-001' }),
    });
    assert.equal(recordPayment.status, 201);
    assert.equal(recordPayment.body.amount, 50000);

    const expenseCategories = await requestApp(createApp(env, pool), '/api/expense-categories', {
      headers: { authorization: `Bearer ${bearerToken()}` },
    });
    assert.equal(expenseCategories.status, 200);
    assert.equal(Array.isArray(expenseCategories.body), true);

    const expenses = await requestApp(createApp(env, pool), '/api/expenses', {
      headers: { authorization: `Bearer ${bearerToken()}` },
    });
    assert.equal(expenses.status, 200);
    assert.equal(Array.isArray(expenses.body.items), true);
    assert.equal(expenses.body.items.length, 0);

    const createExpense = await requestApp(createApp(env, pool), '/api/expenses', {
      method: 'POST',
      headers: { authorization: `Bearer ${bearerToken()}`, 'content-type': 'application/json' },
      body: JSON.stringify({ categoryId: '00000000-0000-4000-8000-000000000050', amount: 18000, currencyCode: 'NGN', description: 'Fabric restock', expenseDate: '2025-07-01' }),
    });
    assert.equal(createExpense.status, 201);
    assert.equal(createExpense.body.description, 'Fabric restock');
  });
});
