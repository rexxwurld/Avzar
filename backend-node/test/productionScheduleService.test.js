import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { previewShopSchedule } from '../src/auth/productionScheduleService.js';

const businessId = '00000000-0000-4000-8000-000000000002';
const workerId = '00000000-0000-4000-8000-000000000040';
const taskId = '00000000-0000-4000-8000-000000000050';

describe('production schedule service', () => {
  it('builds a database-backed, location-scoped preview with current task versions', async () => {
    const calls = [];
    const db = {
      query: async (sql, values) => {
        calls.push({ sql, values });
        if (sql.includes('FROM "Plans" p')) return { rows: [{ PlanCode: 'professional', Value: 'true' }] };
        if (sql.includes('FROM "Businesses"')) return { rows: [{ TimeZoneId: 'UTC' }] };
        if (sql.includes('FROM "BusinessHours"')) return { rows: Array.from({ length: 7 }, (_, dayOfWeek) => ({
          DayOfWeek: dayOfWeek,
          IsOpen: dayOfWeek === 1,
          OpensAt: dayOfWeek === 1 ? '09:00:00' : null,
          ClosesAt: dayOfWeek === 1 ? '17:00:00' : null,
        })) };
        if (sql.includes('FROM "ProductionTasks" t') && sql.includes('JOIN "Orders"')) return { rows: [{
          Id: taskId, AssignedMembershipId: workerId, EstimatedMinutes: 45, Priority: 'Urgent',
          DeadlineUtc: null, StageOrder: 1, Version: '7',
        }] };
        if (sql.includes('FROM "TaskDependencies"')) return { rows: [] };
        if (sql.includes('FROM "ProductionTasks"') && sql.includes('NOT (t."Id"')) return { rows: [] };
        if (sql.includes('FROM "BusinessHolidays"')) return { rows: [] };
        if (sql.includes('FROM "WorkerAvailability"')) return { rows: [] };
        if (sql.includes('FROM "WorkerLeaves"')) return { rows: [] };
        throw new Error(`Unexpected query: ${sql}`);
      },
    };

    const result = await previewShopSchedule(db, { businessId }, {
      startUtc: '2026-06-08T09:00:00Z',
      horizonDays: 5,
    }, ['00000000-0000-4000-8000-000000000060']);

    assert.equal(result.timeZoneId, 'UTC');
    assert.equal(result.isPreviewOnly, true);
    assert.deepEqual(result.taskVersions, { [taskId]: 7 });
    assert.deepEqual(result.scheduled, [{
      taskId,
      startUtc: '2026-06-08T09:00:00.000Z',
      endUtc: '2026-06-08T09:45:00.000Z',
      version: 7,
    }]);
    const taskQuery = calls.find(({ sql }) => sql.includes('FROM "ProductionTasks" t') && sql.includes('JOIN "Orders"'));
    assert.match(taskQuery.sql, /o\."LocationId" = ANY\(\$2::uuid\[\]\)/);
    assert.equal(taskQuery.values[0], businessId);
    assert.deepEqual(taskQuery.values[1], ['00000000-0000-4000-8000-000000000060']);
  });

  it('rejects a non-UTC schedule start before querying the database', async () => {
    let called = false;
    await assert.rejects(
      previewShopSchedule({ query: async () => { called = true; return { rows: [] }; } }, { businessId }, {
        startUtc: '2026-06-08T10:00:00+01:00',
        horizonDays: 1,
      }, null),
      (error) => error.code === 'schedule_start_must_be_utc',
    );
    assert.equal(called, false);
  });

  it('denies scheduling when the active plan does not include it', async () => {
    const db = {
      query: async (sql) => {
        if (sql.includes('FROM "Plans" p')) return { rows: [{ PlanCode: 'free', Value: 'false' }] };
        throw new Error(`Unexpected query: ${sql}`);
      },
    };
    await assert.rejects(
      previewShopSchedule(db, { businessId }, {
        startUtc: '2026-06-08T09:00:00Z',
        horizonDays: 1,
      }, null),
      (error) => error.code === 'feature_not_in_plan',
    );
  });
});
