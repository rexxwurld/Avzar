import { randomUUID } from 'node:crypto';
import { Router } from 'express';
import { z } from 'zod';
import { AppError } from '../http/errors.js';
import { requireBearerAuth } from '../security/bearerAuth.js';
import { requirePermission } from '../security/permissions.js';

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
  name: z.string().trim().min(1).max(120),
  description: z.string().trim().max(250).optional(),
});

const expenseSchema = z.object({
  categoryId: z.string().uuid(),
  amount: z.number().min(0).max(1_000_000_000),
  currencyCode: z.string().trim().length(3).optional(),
  description: z.string().trim().min(1).max(250),
  expenseDate: z.string().trim().max(30).optional(),
  vendor: z.string().trim().max(120).optional(),
  reference: z.string().trim().max(120).optional(),
});

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

  router.get('/expense-categories', ...requireExpensesView, async (_req, res) => res.status(200).json([
    { id: randomUUID(), name: 'Fabric', description: 'Textile and material purchases' },
    { id: randomUUID(), name: 'Utilities', description: 'Energy and shop utilities' },
  ]));

  router.post('/expense-categories', ...requireExpensesManage, async (req, res) => {
    const payload = parse(expenseCategorySchema, req.body);
    return res.status(201).json({ id: randomUUID(), name: payload.name, description: payload.description ?? '' });
  });

  router.get('/expenses', ...requireExpensesView, async (_req, res) => res.status(200).json([
    { id: randomUUID(), categoryId: randomUUID(), amount: 18000, currencyCode: 'NGN', description: 'Fabric restock', expenseDate: '2025-07-01', vendor: 'Material Hub', reference: 'EXP-001', isVoid: false },
    { id: randomUUID(), categoryId: randomUUID(), amount: 4200, currencyCode: 'NGN', description: 'Utility bill', expenseDate: '2025-07-02', vendor: 'Power Co', reference: 'EXP-002', isVoid: false },
  ]));

  router.get('/expenses/summary', ...requireExpensesView, async (_req, res) => res.status(200).json({ total: 22200, currencyCode: 'NGN', count: 2 }));

  router.get('/expenses/:id', ...requireExpensesView, async (req, res) => {
    if (!/^[0-9a-f-]{36}$/i.test(req.params.id)) throw AppError.badRequest('invalid_expense_id', 'The expense ID is invalid.');
    return res.status(200).json({ id: req.params.id, categoryId: randomUUID(), amount: 18000, currencyCode: 'NGN', description: 'Fabric restock', expenseDate: '2025-07-01', vendor: 'Material Hub', reference: 'EXP-001', isVoid: false });
  });

  router.post('/expenses', ...requireExpensesManage, async (req, res) => {
    const payload = parse(expenseSchema, req.body);
    return res.status(201).json({ id: randomUUID(), categoryId: payload.categoryId, amount: payload.amount, currencyCode: payload.currencyCode ?? 'NGN', description: payload.description, expenseDate: payload.expenseDate ?? '2025-07-01', vendor: payload.vendor ?? '', reference: payload.reference ?? '', isVoid: false });
  });

  return router;
}
