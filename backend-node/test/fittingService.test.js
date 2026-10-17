import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { createFitting, listFittings, setFittingStatus } from '../src/auth/fittingService.js';

const businessId = '00000000-0000-4000-8000-000000000002';
const userId = '00000000-0000-4000-8000-000000000001';
const customerId = '00000000-0000-4000-8000-000000000010';
const locationId = '00000000-0000-4000-8000-000000000060';
const orderId = '00000000-0000-4000-8000-000000000050';
const fittingId = '00000000-0000-4000-8000-000000000070';
const user = { businessId, userId };
const allowedLocations = [locationId];
const none = { rows: [] };
const tx = (db) => async (_pool, callback) => callback(db);

describe('fitting persistence', () => {
  it('lists fitting pages filtered to the tenant and permitted locations', async () => {
    const calls = [];
    const fitting = {
      Id: fittingId, CustomerId: customerId, OrderId: orderId, LocationId: locationId,
      ScheduledAtUtc: '2026-11-01T10:00:00.000Z', DurationMinutes: 45, Status: 'Scheduled', Notes: 'First fitting',
    };
    const db = {
      query: async (sql, values) => {
        calls.push({ sql, values });
        if (sql.includes('count(*)')) return { rows: [{ TotalCount: 1 }] };
        return { rows: [fitting] };
      },
    };
    const result = await listFittings(db, user, {
      page: 1, pageSize: 10, customerId, fromUtc: new Date('2026-10-01T00:00:00Z'),
    }, allowedLocations);
    assert.equal(result.totalCount, 1);
    assert.equal(result.items[0].id, fittingId);
    assert.equal(result.items[0].scheduledAtUtc, fitting.ScheduledAtUtc);
    assert.match(calls[0].sql, /f\."BusinessId" = \$1/);
    assert.match(calls[0].sql, /f\."LocationId" = ANY\(\$2::uuid\[\]\)/);
    assert.deepEqual(calls[0].values.slice(0, 3), [businessId, allowedLocations, customerId]);
  });

  it('creates a future fitting, consumes plan usage, and audits within one transaction', async () => {
    const calls = [];
    const db = {
      query: async (sql, values) => {
        calls.push({ sql, values });
        if (sql.includes('FROM "Plans" p')) return { rows: [{ Value: '50' }] };
        if (sql.includes('INSERT INTO "UsageCounters"')) return { rows: [{ Count: '1' }] };
        if (sql.includes('INSERT INTO "Fittings"')) return { rows: [{
          Id: fittingId, CustomerId: customerId, OrderId: orderId, LocationId: locationId,
          ScheduledAtUtc: values[5], DurationMinutes: 45, Status: 'Scheduled', Notes: 'Bring fabric',
        }] };
        if (sql.includes('INSERT INTO "AuditLogs"')) return none;
        throw new Error(`Unexpected transaction query: ${sql}`);
      },
    };
    const pool = {
      query: async (sql) => {
        if (sql.includes('FROM "Customers"')) return { rows: [{ Status: 'Active' }] };
        if (sql.includes('FROM "Locations"')) return { rows: [{ '?column?': 1 }] };
        if (sql.includes('FROM "Orders"')) return { rows: [{ '?column?': 1 }] };
        throw new Error(`Unexpected pool query: ${sql}`);
      },
    };
    const result = await createFitting(pool, tx(db), user, {
      customerId, locationId, orderId, scheduledAt: '2026-11-01T10:00:00Z',
      durationMinutes: 45, notes: 'Bring fabric',
    }, allowedLocations);

    assert.equal(result.id, fittingId);
    assert.equal(result.durationMinutes, 45);
    assert.equal(result.status, 'Scheduled');
    assert.equal(calls.some(({ sql }) => sql.includes('INSERT INTO "UsageCounters"')), true);
    assert.equal(calls.some(({ sql, values }) => sql.includes('INSERT INTO "AuditLogs"') && values[1] === 'fitting.created'), true);
  });

  it('rejects the appointment limit without creating the fitting', async () => {
    let fittingInserted = false;
    const db = {
      query: async (sql) => {
        if (sql.includes('FROM "Plans" p')) return { rows: [{ Value: '0' }] };
        if (sql.includes('INSERT INTO "Fittings"')) fittingInserted = true;
        return none;
      },
    };
    const pool = {
      query: async (sql) => {
        if (sql.includes('FROM "Customers"')) return { rows: [{ Status: 'Active' }] };
        if (sql.includes('FROM "Locations"')) return { rows: [{ '?column?': 1 }] };
        return none;
      },
    };
    await assert.rejects(
      createFitting(pool, tx(db), user, {
        customerId, locationId, scheduledAt: '2026-11-01T10:00:00Z', durationMinutes: 30,
      }, allowedLocations),
      (error) => error.code === 'plan_limit_reached',
    );
    assert.equal(fittingInserted, false);
  });

  it('does not allow a completed fitting to be reopened', async () => {
    let updated = false;
    const db = {
      query: async (sql) => {
        if (sql.includes('FROM "Fittings"')) return { rows: [{
          Id: fittingId, CustomerId: customerId, OrderId: null, LocationId: locationId,
          ScheduledAtUtc: '2026-11-01T10:00:00Z', DurationMinutes: 30, Status: 'Completed', Notes: null,
        }] };
        if (sql.includes('UPDATE "Fittings"')) updated = true;
        return none;
      },
    };
    await assert.rejects(
      setFittingStatus({}, tx(db), user, fittingId, { status: 'Scheduled' }, allowedLocations),
      (error) => error.code === 'fitting_closed',
    );
    assert.equal(updated, false);
  });
});
