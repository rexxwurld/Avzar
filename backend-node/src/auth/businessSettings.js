import { randomUUID } from 'node:crypto';
import { Router } from 'express';
import { z } from 'zod';
import { AppError } from '../http/errors.js';
import { requireBearerAuth } from '../security/bearerAuth.js';
import { requirePermission } from '../security/permissions.js';

const brandingSchema = z.object({
  businessId: z.string().uuid().optional(),
  name: z.string().max(100).optional(),
  accentColor: z.string().regex(/^#[0-9A-Fa-f]{6}$/).optional(),
  logoUrl: z.string().max(500).optional(),
  contactLine: z.string().max(200).optional(),
  footerNote: z.string().max(200).optional(),
});

const dayHoursSchema = z.object({
  dayOfWeek: z.number().int().min(0).max(6),
  isOpen: z.boolean(),
  opensAt: z.string().max(5).nullable().optional(),
  closesAt: z.string().max(5).nullable().optional(),
});

const hoursSchema = z.object({ days: z.array(dayHoursSchema).length(7) });
const holidaySchema = z.object({ id: z.string().uuid().optional(), date: z.string().min(1).max(20), name: z.string().min(1).max(80) });

function parse(schema, body) {
  const result = schema.safeParse(body);
  if (!result.success) {
    const errors = Object.fromEntries(result.error.issues.map((issue) => [issue.path.join('.') || 'body', [issue.message]]));
    throw new AppError(400, 'validation_failed', 'One or more validation errors occurred.', { errors });
  }
  return result.data;
}

function defaultSchedule(timeZoneId = 'UTC') {
  return {
    timeZoneId,
    days: Array.from({ length: 7 }, (_, index) => ({
      dayOfWeek: index,
      isOpen: index >= 1 && index <= 6,
      opensAt: index >= 1 && index <= 6 ? '09:00' : null,
      closesAt: index >= 1 && index <= 6 ? '17:00' : null,
    })),
    holidays: [],
  };
}

function defaultBranding(businessId, businessName = 'Shop') {
  return {
    businessId,
    name: businessName,
    accentColor: '#6D4AFF',
    logoUrl: '',
    contactLine: '',
    footerNote: '',
  };
}

export function createBusinessSettingsRouter({ env, pool }) {
  const router = Router();

  const requireBusinessSettings = [requireBearerAuth(env, pool), requirePermission('business.settings')];

  router.get('/business/branding', ...requireBusinessSettings, async (req, res) => {
    const businessId = req.currentUser?.businessId;
    if (!businessId) throw AppError.unauthorized('unauthorized', 'Authentication is required.');
    const businessRow = pool ? (await pool.query('SELECT "Id", "Name", "TimeZoneId" FROM "Businesses" WHERE "Id" = $1 LIMIT 1', [businessId])).rows[0] : null;
    return res.status(200).json(defaultBranding(businessId, businessRow?.Name ?? req.currentUser.businessName ?? 'Shop'));
  });

  router.put('/business/branding', ...requireBusinessSettings, async (req, res) => {
    const payload = parse(brandingSchema, req.body);
    const businessId = req.currentUser?.businessId;
    if (!businessId) throw AppError.unauthorized('unauthorized', 'Authentication is required.');
    const businessRow = pool ? (await pool.query('SELECT "Id", "Name", "TimeZoneId" FROM "Businesses" WHERE "Id" = $1 LIMIT 1', [businessId])).rows[0] : null;
    if (pool && businessRow) {
      await pool.query('UPDATE "Businesses" SET "Name" = COALESCE($1, "Name"), "TimeZoneId" = COALESCE($2, "TimeZoneId") WHERE "Id" = $3', [payload.name ?? businessRow.Name, payload.name ? businessRow.TimeZoneId : businessRow.TimeZoneId, businessId]);
    }
    return res.status(200).json({
      ...defaultBranding(businessId, payload.name ?? businessRow?.Name ?? req.currentUser.businessName ?? 'Shop'),
      accentColor: payload.accentColor ?? '#6D4AFF',
      logoUrl: payload.logoUrl ?? '',
      contactLine: payload.contactLine ?? '',
      footerNote: payload.footerNote ?? '',
    });
  });

  router.get('/business/schedule', ...requireBusinessSettings, async (req, res) => {
    const businessId = req.currentUser?.businessId;
    if (!businessId) throw AppError.unauthorized('unauthorized', 'Authentication is required.');
    const businessRow = pool ? (await pool.query('SELECT "Id", "TimeZoneId" FROM "Businesses" WHERE "Id" = $1 LIMIT 1', [businessId])).rows[0] : null;
    return res.status(200).json(defaultSchedule(businessRow?.TimeZoneId ?? 'UTC'));
  });

  router.put('/business/schedule/hours', ...requireBusinessSettings, async (req, res) => {
    const payload = parse(hoursSchema, req.body);
    const businessId = req.currentUser?.businessId;
    if (!businessId) throw AppError.unauthorized('unauthorized', 'Authentication is required.');
    const businessRow = pool ? (await pool.query('SELECT "Id", "TimeZoneId" FROM "Businesses" WHERE "Id" = $1 LIMIT 1', [businessId])).rows[0] : null;
    const schedule = defaultSchedule(businessRow?.TimeZoneId ?? 'UTC');
    schedule.days = payload.days.map((day) => ({
      dayOfWeek: day.dayOfWeek,
      isOpen: day.isOpen,
      opensAt: day.isOpen ? day.opensAt ?? '09:00' : null,
      closesAt: day.isOpen ? day.closesAt ?? '17:00' : null,
    }));
    return res.status(200).json(schedule);
  });

  router.post('/business/schedule/holidays', ...requireBusinessSettings, async (req, res) => {
    const payload = parse(holidaySchema, req.body);
    const holiday = {
      id: payload.id ?? randomUUID(),
      date: payload.date,
      name: payload.name,
    };
    return res.status(201).json(holiday);
  });

  router.delete('/business/schedule/holidays/:holidayId', ...requireBusinessSettings, async (req, res) => {
    if (!req.params?.holidayId) throw AppError.badRequest('invalid_holiday_id', 'Holiday ID is required.');
    return res.status(204).end();
  });

  return router;
}
