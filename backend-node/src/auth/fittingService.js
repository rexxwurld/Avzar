import { randomUUID } from 'node:crypto';
import { AppError } from '../http/errors.js';

const statuses = ['Scheduled', 'Completed', 'Cancelled', 'NoShow'];
const entitlementKey = 'appointments.monthly.max';

async function audit(db, user, action, fittingId, details = null) {
  await db.query(`
    INSERT INTO "AuditLogs" ("Id", "Action", "ActorUserId", "BusinessId", "CreatedAtUtc",
      "DetailsJson", "EntityId", "EntityType")
    VALUES ($1, $2, $3, $4, now(), $5::jsonb, $6, 'FittingAppointment')`,
  [randomUUID(), action, user.userId, user.businessId, details === null ? null : JSON.stringify(details), fittingId]);
}

function fittingDto(row) {
  return {
    id: row.Id,
    customerId: row.CustomerId,
    orderId: row.OrderId,
    locationId: row.LocationId,
    scheduledAtUtc: new Date(row.ScheduledAtUtc).toISOString(),
    durationMinutes: row.DurationMinutes,
    status: row.Status,
    notes: row.Notes,
  };
}

async function consumeAppointmentEntitlement(db, user) {
  const { rows } = await db.query(`
    SELECT COALESCE(override."Value", pe."Value", '0') AS "Value"
    FROM "Plans" p
    LEFT JOIN "PlanEntitlements" pe ON pe."PlanId" = p."Id" AND pe."Key" = $2
    LEFT JOIN LATERAL (
      SELECT o."Value" FROM "EntitlementOverrides" o
      WHERE o."BusinessId" = $1 AND o."Key" = $2
        AND (o."ExpiresAtUtc" IS NULL OR o."ExpiresAtUtc" > now())
      ORDER BY o."CreatedAtUtc" DESC LIMIT 1
    ) override ON TRUE
    WHERE p."Id" = COALESCE(
      (SELECT s."PlanId" FROM "Subscriptions" s
        WHERE s."BusinessId" = $1 AND s."Status" <> 'Expired' LIMIT 1),
      (SELECT free."Id" FROM "Plans" free WHERE free."Code" = 'free' LIMIT 1)
    )`,
  [user.businessId, entitlementKey]);
  if (!rows.length) throw new AppError(503, 'plans_not_seeded', 'Subscription plans are not configured on this server.');

  const rawLimit = String(rows[0].Value ?? '').trim();
  const limit = rawLimit === 'unlimited' ? null : (/^\d+$/.test(rawLimit) ? BigInt(rawLimit) : 0n);
  if (limit === 0n) {
    throw new AppError(403, 'plan_limit_reached', 'Your plan does not allow any appointments this month.', {
      entitlement: entitlementKey, limit: 0, used: 0,
    });
  }

  const { rows: consumed } = await db.query(`
    INSERT INTO "UsageCounters" ("BusinessId", "Key", "PeriodStartUtc", "Count")
    VALUES ($1, $2, date_trunc('month', now()), 1)
    ON CONFLICT ("BusinessId", "Key", "PeriodStartUtc")
    DO UPDATE SET "Count" = "UsageCounters"."Count" + 1
      WHERE $3::bigint IS NULL OR "UsageCounters"."Count" + 1 <= $3::bigint
    RETURNING "Count"`,
  [user.businessId, entitlementKey, limit === null ? null : limit.toString()]);
  if (!consumed.length) {
    const { rows: used } = await db.query(`
      SELECT "Count" FROM "UsageCounters"
      WHERE "BusinessId" = $1 AND "Key" = $2 AND "PeriodStartUtc" = date_trunc('month', now())`,
    [user.businessId, entitlementKey]);
    throw new AppError(403, 'plan_limit_reached', `Your plan allows ${limit} appointments this month, and you have used ${used[0]?.Count ?? 0}. Upgrade to unlock more.`, {
      entitlement: entitlementKey, limit: Number(limit), used: Number(used[0]?.Count ?? 0),
    });
  }
}

export async function listFittings(db, user, filters, allowedLocations) {
  const values = [user.businessId, allowedLocations];
  const conditions = ['f."BusinessId" = $1', '($2::uuid[] IS NULL OR f."LocationId" = ANY($2::uuid[]))'];
  for (const [name, column] of [['customerId', 'CustomerId'], ['orderId', 'OrderId']]) {
    if (filters[name]) {
      values.push(filters[name]);
      conditions.push(`f."${column}" = $${values.length}`);
    }
  }
  if (filters.fromUtc) {
    values.push(filters.fromUtc);
    conditions.push(`f."ScheduledAtUtc" >= $${values.length}`);
  }
  if (filters.toUtc) {
    values.push(filters.toUtc);
    conditions.push(`f."ScheduledAtUtc" < $${values.length}`);
  }
  const where = conditions.join(' AND ');
  const count = await db.query(`SELECT count(*)::int AS "TotalCount" FROM "Fittings" f WHERE ${where}`, values);
  const { rows } = await db.query(`
    SELECT f."Id", f."CustomerId", f."OrderId", f."LocationId", f."ScheduledAtUtc",
      f."DurationMinutes", f."Status", f."Notes"
    FROM "Fittings" f WHERE ${where}
    ORDER BY f."ScheduledAtUtc"
    LIMIT $${values.length + 1} OFFSET $${values.length + 2}`,
  [...values, filters.pageSize, (filters.page - 1) * filters.pageSize]);
  return {
    items: rows.map(fittingDto),
    page: filters.page,
    pageSize: filters.pageSize,
    totalCount: Number(count.rows[0]?.TotalCount ?? 0),
  };
}

export async function createFitting(pool, dbTransaction, user, payload, allowedLocations) {
  const scheduledAt = new Date(payload.scheduledAt);
  if (scheduledAt <= new Date()) throw AppError.badRequest('fitting_in_past', 'A fitting must be scheduled in the future.');
  const { rows: customers } = await pool.query(`
    SELECT "Status" FROM "Customers"
    WHERE "BusinessId" = $1 AND "Id" = $2`,
  [user.businessId, payload.customerId]);
  if (!customers.length) throw AppError.notFound('customer_not_found', 'Customer not found.');
  if (customers[0].Status !== 'Active') throw AppError.conflict('customer_not_active', 'Fittings can only be booked for active customers.');
  if (allowedLocations !== null && !allowedLocations.includes(payload.locationId)) {
    throw AppError.forbidden('location_forbidden', 'You do not have access to that location.');
  }
  const { rows: locations } = await pool.query(`
    SELECT 1 FROM "Locations" WHERE "BusinessId" = $1 AND "Id" = $2 AND "IsActive"`,
  [user.businessId, payload.locationId]);
  if (!locations.length) throw AppError.badRequest('unknown_location', 'Location not found or inactive.');
  if (payload.orderId) {
    const { rows: orders } = await pool.query(`
      SELECT 1 FROM "Orders" WHERE "BusinessId" = $1 AND "Id" = $2 AND "CustomerId" = $3
        AND ($4::uuid[] IS NULL OR "LocationId" = ANY($4::uuid[]))`,
    [user.businessId, payload.orderId, payload.customerId, allowedLocations]);
    if (!orders.length) throw AppError.badRequest('unknown_order', 'That order does not exist for this customer.');
  }

  return dbTransaction(pool, async (db) => {
    await consumeAppointmentEntitlement(db, user);
    const id = randomUUID();
    const { rows } = await db.query(`
      INSERT INTO "Fittings" ("Id", "BusinessId", "CustomerId", "OrderId", "LocationId", "ScheduledAtUtc",
        "DurationMinutes", "Status", "Notes", "CreatedByUserId", "CreatedAtUtc")
      VALUES ($1, $2, $3, $4, $5, $6, $7, 'Scheduled', $8, $9, now())
      RETURNING "Id", "CustomerId", "OrderId", "LocationId", "ScheduledAtUtc", "DurationMinutes", "Status", "Notes"`,
    [id, user.businessId, payload.customerId, payload.orderId ?? null, payload.locationId, scheduledAt,
      payload.durationMinutes, payload.notes?.trim() || null, user.userId]);
    await audit(db, user, 'fitting.created', id);
    return fittingDto(rows[0]);
  });
}

export async function setFittingStatus(pool, dbTransaction, user, fittingId, payload, allowedLocations) {
  if (!statuses.includes(payload.status)) throw AppError.badRequest('invalid_status', 'The fitting status is invalid.');
  return dbTransaction(pool, async (client) => {
    const { rows } = await client.query(`
        SELECT "Id", "CustomerId", "OrderId", "LocationId", "ScheduledAtUtc", "DurationMinutes", "Status", "Notes"
        FROM "Fittings"
        WHERE "BusinessId" = $1 AND "Id" = $2
          AND ($3::uuid[] IS NULL OR "LocationId" = ANY($3::uuid[]))
        FOR UPDATE`,
    [user.businessId, fittingId, allowedLocations]);
    if (!rows.length) throw AppError.notFound('fitting_not_found', 'Fitting not found.');
    const current = rows[0];
    if (current.Status === 'Completed' && payload.status !== 'Completed') {
      throw AppError.conflict('fitting_closed', 'A completed fitting cannot be reopened.');
    }
    const notes = payload.notes?.trim() || current.Notes;
    const { rows: updated } = await client.query(`
        UPDATE "Fittings" SET "Status" = $1, "Notes" = $2, "UpdatedAtUtc" = now()
        WHERE "BusinessId" = $3 AND "Id" = $4
        RETURNING "Id", "CustomerId", "OrderId", "LocationId", "ScheduledAtUtc", "DurationMinutes", "Status", "Notes"`,
    [payload.status, notes, user.businessId, fittingId]);
    await audit(client, user, 'fitting.status_changed', fittingId, { status: payload.status });
    return fittingDto(updated[0]);
  });
}
