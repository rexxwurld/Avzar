import { randomUUID } from 'node:crypto';
import { Router } from 'express';
import { z } from 'zod';
import { AppError } from '../http/errors.js';
import { requireBearerAuth } from '../security/bearerAuth.js';
import { requirePermission } from '../security/permissions.js';
import {
  createExpense,
  exportExpenses,
  getExpense,
  listExpenseCategories,
  listExpenses,
  saveExpenseCategory,
  summarizeExpenses,
  updateExpense,
  voidExpense,
} from './expenseService.js';

const invoiceSchema = z.object({
  customerId: z.string().uuid(),
  amount: z.number().min(0).max(1_000_000_000).optional(),
  currencyCode: z.string().trim().length(3).optional(),
  dueDate: z.string().trim().max(30).optional(),
  notes: z.string().trim().max(500).optional(),
});

const paymentSchema = z.object({
  amount: z.number().min(0).max(1_000_000_000),
  method: z.string().trim().min(1).max(60),
  reference: z.string().trim().min(1).max(120).optional(),
  status: z.string().trim().max(40).optional(),
});

const expenseCategorySchema = z.object({
  name: z.string().trim().min(2).max(80),
  isActive: z.boolean().default(true),
  sortOrder: z.number().int().min(0).max(10_000).default(0),
});

const expenseSchema = z.object({
  categoryId: z.string().uuid(),
  amount: z.number().min(0.01).max(1_000_000_000),
  description: z.string().trim().min(2).max(300),
  expenseDate: z.string().regex(/^\d{4}-\d{2}-\d{2}$/),
  vendor: z.string().trim().max(120).optional(),
  reference: z.string().trim().max(100).optional(),
  locationId: z.string().uuid().nullable().optional(),
  version: z.number().int().min(0).max(4_294_967_295).optional(),
});
const voidExpenseSchema = z.object({ reason: z.string().trim().min(3).max(500) });

function transaction(pool, callback) {
  return pool.connect().then(async (client) => {
    try {
      await client.query('BEGIN');
      const result = await callback(client);
      await client.query('COMMIT');
      return result;
    } catch (error) {
      await client.query('ROLLBACK');
      throw error;
    } finally {
      client.release();
    }
  });
}

async function allowedLocations(pool, user) {
  if (user.isOwner || user.permissions.includes('locations.all')) return null;
  const { rows } = await pool.query(`
    SELECT DISTINCT ml."LocationId"
    FROM "Memberships" m
    JOIN "MembershipLocations" ml ON ml."MembershipId" = m."Id" AND ml."BusinessId" = m."BusinessId"
    WHERE m."UserId" = $1 AND m."BusinessId" = $2 AND m."Status" = 0`,
  [user.userId, user.businessId]);
  return rows.map((row) => row.LocationId);
}

const isUuid = (value) => typeof value === 'string'
  && /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(value);

function parseExpenseDateQuery(value, name) {
  if (value === undefined) return undefined;
  const result = String(value);
  if (!/^\d{4}-\d{2}-\d{2}$/.test(result) || Number.isNaN(Date.parse(`${result}T00:00:00Z`))
      || new Date(`${result}T00:00:00Z`).toISOString().slice(0, 10) !== result) {
    throw AppError.badRequest(`invalid_${name}`, `${name} must be a valid date in YYYY-MM-DD format.`);
  }
  return result;
}

function expenseListQuery(req) {
  const page = Number.parseInt(req.query.page ?? '1', 10);
  const pageSize = Number.parseInt(req.query.pageSize ?? '25', 10);
  if (!Number.isSafeInteger(page) || page < 1 || !Number.isSafeInteger(pageSize) || pageSize < 1 || pageSize > 100) {
    throw AppError.badRequest('invalid_paging', 'Page must be positive and pageSize must be between 1 and 100.');
  }
  const query = { page, pageSize, includeVoid: false };
  for (const field of ['categoryId', 'locationId']) {
    if (req.query[field] !== undefined) {
      if (!isUuid(String(req.query[field]))) throw AppError.badRequest(`invalid_${field}`, `The ${field} is invalid.`);
      query[field] = String(req.query[field]);
    }
  }
  query.from = parseExpenseDateQuery(req.query.from, 'from');
  query.to = parseExpenseDateQuery(req.query.to, 'to');
  if (query.from && query.to && query.from > query.to) {
    throw AppError.badRequest('invalid_date_range', 'The from date must not be later than the to date.');
  }
  if (req.query.includeVoid !== undefined) {
    if (!['true', 'false'].includes(String(req.query.includeVoid))) {
      throw AppError.badRequest('invalid_include_void', 'includeVoid must be true or false.');
    }
    query.includeVoid = String(req.query.includeVoid) === 'true';
  }
  if (req.query.search !== undefined) query.search = String(req.query.search).trim().slice(0, 200);
  return query;
}

function parse(schema, body) {
  const result = schema.safeParse(body);
  if (!result.success) {
    const errors = Object.fromEntries(result.error.issues.map((issue) => [issue.path.join('.') || 'body', [issue.message]]));
    throw new AppError(400, 'validation_failed', 'One or more validation errors occurred.', { errors });
  }
  return result.data;
}

export function createFinanceRouter({ env, pool }) {
  const router = Router();

  const requireInvoiceView = [requireBearerAuth(env, pool), requirePermission('invoices.view')];
  const requireInvoiceManage = [requireBearerAuth(env, pool), requirePermission('invoices.manage')];
  const requirePaymentView = [requireBearerAuth(env, pool), requirePermission('payments.view')];
  const requirePaymentRecord = [requireBearerAuth(env, pool), requirePermission('payments.record')];
  const requirePaymentVerify = [requireBearerAuth(env, pool), requirePermission('payments.verify')];
  const requireExpensesView = [requireBearerAuth(env, pool), requirePermission('expenses.view')];
  const requireExpensesManage = [requireBearerAuth(env, pool), requirePermission('expenses.manage')];

  router.get('/invoices', ...requireInvoiceView, async (_req, res) => res.status(200).json([
    { id: randomUUID(), customerId: randomUUID(), amount: 250000, currencyCode: 'NGN', status: 'draft', dueDate: '2025-07-01', notes: 'Alteration invoice' },
    { id: randomUUID(), customerId: randomUUID(), amount: 160000, currencyCode: 'NGN', status: 'issued', dueDate: '2025-07-15', notes: 'Repair invoice' },
  ]));

  router.get('/invoices/:id', ...requireInvoiceView, async (req, res) => {
    if (!/^[0-9a-f-]{36}$/i.test(req.params.id)) throw AppError.badRequest('invalid_invoice_id', 'The invoice ID is invalid.');
    return res.status(200).json({ id: req.params.id, customerId: randomUUID(), amount: 250000, currencyCode: 'NGN', status: 'draft', dueDate: '2025-07-01', notes: 'Alteration invoice' });
  });

  router.post('/invoices', ...requireInvoiceManage, async (req, res) => {
    const payload = parse(invoiceSchema, req.body);
    return res.status(201).json({ id: randomUUID(), customerId: payload.customerId, amount: payload.amount ?? 0, currencyCode: payload.currencyCode ?? 'NGN', status: 'draft', dueDate: payload.dueDate ?? '2025-07-01', notes: payload.notes ?? '' });
  });

  router.post('/invoices/:id/payments', ...requirePaymentRecord, async (req, res) => {
    if (!/^[0-9a-f-]{36}$/i.test(req.params.id)) throw AppError.badRequest('invalid_invoice_id', 'The invoice ID is invalid.');
    const payload = parse(paymentSchema, req.body);
    return res.status(201).json({ id: randomUUID(), invoiceId: req.params.id, amount: payload.amount, method: payload.method, reference: payload.reference ?? '', status: payload.status ?? 'pending', createdAtUtc: new Date().toISOString() });
  });

  router.get('/payments', ...requirePaymentView, async (_req, res) => res.status(200).json([
    { id: randomUUID(), invoiceId: randomUUID(), amount: 50000, method: 'bank_transfer', reference: 'REF-001', status: 'verified', createdAtUtc: new Date().toISOString() },
    { id: randomUUID(), invoiceId: randomUUID(), amount: 25000, method: 'cash', reference: 'REF-002', status: 'pending', createdAtUtc: new Date().toISOString() },
  ]));

  router.get('/payments/:id', ...requirePaymentView, async (req, res) => {
    if (!/^[0-9a-f-]{36}$/i.test(req.params.id)) throw AppError.badRequest('invalid_payment_id', 'The payment ID is invalid.');
    return res.status(200).json({ id: req.params.id, invoiceId: randomUUID(), amount: 50000, method: 'bank_transfer', reference: 'REF-001', status: 'verified', createdAtUtc: new Date().toISOString() });
  });

  router.post('/payments/:id/verify', ...requirePaymentVerify, async (req, res) => {
    if (!/^[0-9a-f-]{36}$/i.test(req.params.id)) throw AppError.badRequest('invalid_payment_id', 'The payment ID is invalid.');
    return res.status(200).json({ id: req.params.id, status: 'verified', verifiedAtUtc: new Date().toISOString() });
  });

  router.post('/payments/:id/reject', ...requirePaymentVerify, async (req, res) => {
    if (!/^[0-9a-f-]{36}$/i.test(req.params.id)) throw AppError.badRequest('invalid_payment_id', 'The payment ID is invalid.');
    return res.status(200).json({ id: req.params.id, status: 'rejected', rejectedAtUtc: new Date().toISOString() });
  });

  router.get('/expense-categories', ...requireExpensesView, async (req, res) => {
    return res.status(200).json(await listExpenseCategories(pool, req.currentUser));
  });

  router.post('/expense-categories', ...requireExpensesManage, async (req, res) => {
    const payload = parse(expenseCategorySchema, req.body);
    return res.status(201).json(await saveExpenseCategory(pool, transaction, req.currentUser, null, payload));
  });

  router.put('/expense-categories/:id', ...requireExpensesManage, async (req, res) => {
    if (!isUuid(req.params.id)) throw AppError.badRequest('invalid_category_id', 'The category ID is invalid.');
    const payload = parse(expenseCategorySchema, req.body);
    return res.status(200).json(await saveExpenseCategory(pool, transaction, req.currentUser, req.params.id, payload));
  });

  router.get('/expenses/export', ...requireExpensesView, async (req, res) => {
    const query = expenseListQuery(req);
    const csv = await exportExpenses(pool, req.currentUser, query, await allowedLocations(pool, req.currentUser));
    const date = new Date().toISOString().slice(0, 10).replaceAll('-', '');
    res.setHeader('content-disposition', `attachment; filename="expenses-${date}.csv"`);
    return res.status(200).type('text/csv; charset=utf-8').send(csv);
  });

  router.get('/expenses', ...requireExpensesView, async (req, res) => {
    return res.status(200).json(await listExpenses(pool, req.currentUser, expenseListQuery(req),
      await allowedLocations(pool, req.currentUser)));
  });

  router.get('/expenses/summary', ...requireExpensesView, async (req, res) => {
    const from = parseExpenseDateQuery(req.query.from, 'from');
    const to = parseExpenseDateQuery(req.query.to, 'to');
    if (from && to && from > to) throw AppError.badRequest('invalid_date_range', 'The from date must not be later than the to date.');
    let locationId;
    if (req.query.locationId !== undefined) {
      if (!isUuid(String(req.query.locationId))) throw AppError.badRequest('invalid_location_id', 'The location ID is invalid.');
      locationId = String(req.query.locationId);
    }
    return res.status(200).json(await summarizeExpenses(pool, req.currentUser, { from, to, locationId },
      await allowedLocations(pool, req.currentUser)));
  });

  router.get('/expenses/:id', ...requireExpensesView, async (req, res) => {
    if (!isUuid(req.params.id)) throw AppError.badRequest('invalid_expense_id', 'The expense ID is invalid.');
    return res.status(200).json(await getExpense(pool, req.currentUser, req.params.id,
      await allowedLocations(pool, req.currentUser)));
  });

  router.post('/expenses', ...requireExpensesManage, async (req, res) => {
    const payload = parse(expenseSchema, req.body);
    return res.status(201).json(await createExpense(pool, transaction, req.currentUser, payload,
      await allowedLocations(pool, req.currentUser)));
  });

  router.put('/expenses/:id', ...requireExpensesManage, async (req, res) => {
    if (!isUuid(req.params.id)) throw AppError.badRequest('invalid_expense_id', 'The expense ID is invalid.');
    const payload = parse(expenseSchema, req.body);
    return res.status(200).json(await updateExpense(pool, transaction, req.currentUser, req.params.id, payload,
      await allowedLocations(pool, req.currentUser)));
  });

  router.post('/expenses/:id/void', ...requireExpensesManage, async (req, res) => {
    if (!isUuid(req.params.id)) throw AppError.badRequest('invalid_expense_id', 'The expense ID is invalid.');
    const { reason } = parse(voidExpenseSchema, req.body);
    await voidExpense(pool, transaction, req.currentUser, req.params.id, reason,
      await allowedLocations(pool, req.currentUser));
    return res.status(200).json(await getExpense(pool, req.currentUser, req.params.id,
      await allowedLocations(pool, req.currentUser)));
  });

  return router;
}
