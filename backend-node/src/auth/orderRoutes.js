import { randomUUID } from 'node:crypto';
import { Router } from 'express';
import { z } from 'zod';
import { AppError } from '../http/errors.js';
import { requireBearerAuth } from '../security/bearerAuth.js';
import { requirePermission } from '../security/permissions.js';

const orderItemSchema = z.object({
  designId: z.string().uuid(),
  quantity: z.number().int().min(1).max(1000),
  unitPrice: z.number().min(0).max(1_000_000_000).optional(),
  notes: z.string().trim().max(250).optional(),
});

const orderSchema = z.object({
  customerId: z.string().uuid(),
  items: z.array(orderItemSchema).min(1),
  notes: z.string().trim().max(500).optional(),
  status: z.string().trim().max(40).optional(),
});

const fittingSchema = z.object({
  customerId: z.string().uuid(),
  orderId: z.string().uuid().optional(),
  dateUtc: z.string().datetime().optional(),
  notes: z.string().trim().max(500).optional(),
});

const workflowSchema = z.object({
  name: z.string().trim().min(1).max(120),
  description: z.string().trim().max(500).optional(),
  steps: z.array(z.object({ name: z.string().trim().min(1).max(80), required: z.boolean().optional() })).min(1),
});

const taskSchema = z.object({
  title: z.string().trim().min(1).max(120),
  orderId: z.string().uuid().optional(),
  assigneeMembershipId: z.string().uuid().optional(),
  status: z.string().trim().max(40).optional(),
  deadlineUtc: z.string().datetime().optional(),
  notes: z.string().trim().max(500).optional(),
});

const assignTaskSchema = z.object({
  assigneeMembershipId: z.string().uuid(),
  reason: z.string().trim().max(250).optional(),
});

function parse(schema, body) {
  const result = schema.safeParse(body);
  if (!result.success) {
    const errors = Object.fromEntries(result.error.issues.map((issue) => [issue.path.join('.') || 'body', [issue.message]]));
    throw new AppError(400, 'validation_failed', 'One or more validation errors occurred.', { errors });
  }
  return result.data;
}

function isUuid(value) {
  return typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(value);
}

export function createOrderRouter({ env, pool }) {
  const router = Router();

  const requireOrdersView = [requireBearerAuth(env, pool), requirePermission('orders.view')];
  const requireOrdersManage = [requireBearerAuth(env, pool), requirePermission('orders.manage')];
  const requireOrdersConfirm = [requireBearerAuth(env, pool), requirePermission('orders.confirm')];
  const requireFittingsManage = [requireBearerAuth(env, pool), requirePermission('fittings.manage')];
  const requireTasksView = [requireBearerAuth(env, pool), requirePermission('tasks.view')];
  const requireTasksManage = [requireBearerAuth(env, pool), requirePermission('tasks.manage')];
  const requireTasksAssign = [requireBearerAuth(env, pool), requirePermission('tasks.assign')];
  const requireTasksOverride = [requireBearerAuth(env, pool), requirePermission('tasks.override')];
  const requireTasksWork = [requireBearerAuth(env, pool), requirePermission('tasks.work')];
  const requireWorkflowsManage = [requireBearerAuth(env, pool), requirePermission('workflows.manage')];

  router.get('/orders', ...requireOrdersView, async (_req, res) => res.status(200).json([
    { id: randomUUID(), customerId: randomUUID(), status: 'draft', total: 420000, currencyCode: 'NGN' },
    { id: randomUUID(), customerId: randomUUID(), status: 'quoted', total: 520000, currencyCode: 'NGN' },
  ]));

  router.get('/orders/:id', ...requireOrdersView, async (req, res) => {
    if (!isUuid(req.params.id)) throw AppError.badRequest('invalid_order_id', 'The order ID is invalid.');
    return res.status(200).json({ id: req.params.id, customerId: randomUUID(), status: 'draft', total: 420000, currencyCode: 'NGN', items: [{ designId: randomUUID(), quantity: 1, unitPrice: 420000 }] });
  });

  router.get('/orders/:id/pricing', ...requireOrdersView, async (req, res) => {
    if (!isUuid(req.params.id)) throw AppError.badRequest('invalid_order_id', 'The order ID is invalid.');
    return res.status(200).json({ orderId: req.params.id, subtotal: 420000, tax: 0, total: 420000, currencyCode: 'NGN' });
  });

  router.post('/orders', ...requireOrdersManage, async (req, res) => {
    const payload = parse(orderSchema, req.body);
    return res.status(201).json({ id: randomUUID(), customerId: payload.customerId, status: payload.status ?? 'draft', total: 420000, currencyCode: 'NGN', items: payload.items });
  });

  router.put('/orders/:id', ...requireOrdersManage, async (req, res) => {
    if (!isUuid(req.params.id)) throw AppError.badRequest('invalid_order_id', 'The order ID is invalid.');
    const payload = parse(orderSchema, req.body);
    return res.status(200).json({ id: req.params.id, customerId: payload.customerId, status: payload.status ?? 'draft', total: 420000, currencyCode: 'NGN', items: payload.items });
  });

  router.post('/orders/:id/quote', ...requireOrdersManage, async (req, res) => {
    if (!isUuid(req.params.id)) throw AppError.badRequest('invalid_order_id', 'The order ID is invalid.');
    return res.status(200).json({ id: req.params.id, status: 'quoted', total: 420000, currencyCode: 'NGN' });
  });

  router.post('/orders/:id/confirm', ...requireOrdersConfirm, async (req, res) => {
    if (!isUuid(req.params.id)) throw AppError.badRequest('invalid_order_id', 'The order ID is invalid.');
    return res.status(200).json({ id: req.params.id, status: 'confirmed', total: 420000, currencyCode: 'NGN' });
  });

  router.post('/orders/:id/cancel', ...requireOrdersManage, async (req, res) => {
    if (!isUuid(req.params.id)) throw AppError.badRequest('invalid_order_id', 'The order ID is invalid.');
    const payload = parse(z.object({ reason: z.string().trim().min(1).max(250).optional() }), req.body);
    return res.status(200).json({ id: req.params.id, status: 'cancelled', reason: payload.reason ?? 'customer_request' });
  });

  router.get('/fittings', ...requireOrdersView, async (_req, res) => res.status(200).json([
    { id: randomUUID(), customerId: randomUUID(), orderId: randomUUID(), dateUtc: new Date().toISOString(), status: 'scheduled' },
  ]));

  router.post('/fittings', ...requireFittingsManage, async (req, res) => {
    const payload = parse(fittingSchema, req.body);
    return res.status(201).json({ id: randomUUID(), customerId: payload.customerId, orderId: payload.orderId ?? randomUUID(), dateUtc: payload.dateUtc ?? new Date().toISOString(), notes: payload.notes ?? '', status: 'scheduled' });
  });

  router.post('/fittings/:id/status', ...requireFittingsManage, async (req, res) => {
    if (!isUuid(req.params.id)) throw AppError.badRequest('invalid_fitting_id', 'The fitting ID is invalid.');
    const payload = parse(z.object({ status: z.string().trim().min(1).max(40) }), req.body);
    return res.status(200).json({ id: req.params.id, status: payload.status });
  });

  router.get('/workflows', ...requireTasksView, async (_req, res) => res.status(200).json([
    { id: randomUUID(), name: 'Standard Tailoring', description: 'Basic production flow', steps: [{ name: 'Cut', required: true }] },
  ]));

  router.get('/workflows/:id', ...requireTasksView, async (req, res) => {
    if (!isUuid(req.params.id)) throw AppError.badRequest('invalid_workflow_id', 'The workflow ID is invalid.');
    return res.status(200).json({ id: req.params.id, name: 'Standard Tailoring', description: 'Basic production flow', steps: [{ name: 'Cut', required: true }] });
  });

  router.post('/workflows', ...requireWorkflowsManage, async (req, res) => {
    const payload = parse(workflowSchema, req.body);
    return res.status(201).json({ id: randomUUID(), name: payload.name, description: payload.description ?? '', steps: payload.steps });
  });

  router.put('/workflows/:id', ...requireWorkflowsManage, async (req, res) => {
    if (!isUuid(req.params.id)) throw AppError.badRequest('invalid_workflow_id', 'The workflow ID is invalid.');
    const payload = parse(workflowSchema, req.body);
    return res.status(200).json({ id: req.params.id, name: payload.name, description: payload.description ?? '', steps: payload.steps });
  });

  router.get('/tasks', ...requireTasksView, async (_req, res) => res.status(200).json([
    { id: randomUUID(), title: 'Alter hem', status: 'open', orderId: randomUUID(), assigneeMembershipId: randomUUID() },
    { id: randomUUID(), title: 'Final fit', status: 'in_progress', orderId: randomUUID(), assigneeMembershipId: randomUUID() },
  ]));

  router.post('/tasks', ...requireTasksManage, async (req, res) => {
    const payload = parse(taskSchema, req.body);
    return res.status(201).json({
      id: randomUUID(),
      title: payload.title,
      status: payload.status ?? 'open',
      orderId: payload.orderId ?? randomUUID(),
      assigneeMembershipId: payload.assigneeMembershipId ?? randomUUID(),
      deadlineUtc: payload.deadlineUtc ?? new Date().toISOString(),
      notes: payload.notes ?? '',
    });
  });

  router.post('/tasks/schedule/preview', ...requireTasksView, async (req, res) => {
    const payload = parse(z.object({ tasks: z.array(z.object({ id: z.string().uuid().optional(), title: z.string().trim().min(1).max(120) })).default([]) }), req.body);
    return res.status(200).json({ preview: payload.tasks.map((task) => ({ id: task.id ?? randomUUID(), title: task.title, scheduled: true })), warnings: [] });
  });

  router.post('/tasks/schedule/apply', ...requireTasksManage, async (req, res) => {
    const payload = parse(z.object({ tasks: z.array(z.object({ id: z.string().uuid().optional(), title: z.string().trim().min(1).max(120), startUtc: z.string().datetime().optional() })).default([]) }), req.body);
    return res.status(200).json({ applied: payload.tasks.length, scheduleId: randomUUID() });
  });

  router.get('/tasks/:id', ...requireTasksView, async (req, res) => {
    if (!isUuid(req.params.id)) throw AppError.badRequest('invalid_task_id', 'The task ID is invalid.');
    return res.status(200).json({ id: req.params.id, title: 'Alter hem', status: 'open', orderId: randomUUID(), assigneeMembershipId: randomUUID(), notes: '' });
  });

  router.put('/tasks/:id', ...requireTasksManage, async (req, res) => {
    if (!isUuid(req.params.id)) throw AppError.badRequest('invalid_task_id', 'The task ID is invalid.');
    const payload = parse(taskSchema, req.body);
    return res.status(200).json({ id: req.params.id, title: payload.title, status: payload.status ?? 'open', orderId: payload.orderId ?? randomUUID(), assigneeMembershipId: payload.assigneeMembershipId ?? randomUUID(), notes: payload.notes ?? '' });
  });

  router.post('/tasks/:id/assign', ...requireTasksAssign, async (req, res) => {
    if (!isUuid(req.params.id)) throw AppError.badRequest('invalid_task_id', 'The task ID is invalid.');
    const payload = parse(assignTaskSchema, req.body);
    return res.status(200).json({ id: req.params.id, assigneeMembershipId: payload.assigneeMembershipId, reason: payload.reason ?? '' });
  });

  router.post('/tasks/:id/unblock', ...requireTasksManage, async (req, res) => {
    if (!isUuid(req.params.id)) throw AppError.badRequest('invalid_task_id', 'The task ID is invalid.');
    return res.status(200).json({ id: req.params.id, status: 'open' });
  });

  router.post('/tasks/:id/cancel', ...requireTasksManage, async (req, res) => {
    if (!isUuid(req.params.id)) throw AppError.badRequest('invalid_task_id', 'The task ID is invalid.');
    const payload = parse(z.object({ reason: z.string().trim().min(1).max(250) }), req.body);
    return res.status(200).json({ id: req.params.id, status: 'cancelled', reason: payload.reason });
  });

  router.post('/tasks/:id/override', ...requireTasksOverride, async (req, res) => {
    if (!isUuid(req.params.id)) throw AppError.badRequest('invalid_task_id', 'The task ID is invalid.');
    const payload = parse(z.object({ reason: z.string().trim().min(1).max(250) }), req.body);
    return res.status(200).json({ id: req.params.id, status: 'overridden', reason: payload.reason });
  });

  router.post('/orders/:orderId/tasks', ...requireTasksManage, async (req, res) => {
    if (!isUuid(req.params.orderId)) throw AppError.badRequest('invalid_order_id', 'The order ID is invalid.');
    const payload = parse(taskSchema, req.body);
    return res.status(201).json({ id: randomUUID(), title: payload.title, orderId: req.params.orderId, assigneeMembershipId: payload.assigneeMembershipId ?? randomUUID(), status: payload.status ?? 'open' });
  });

  router.post('/orders/:orderId/generate-tasks', ...requireTasksManage, async (req, res) => {
    if (!isUuid(req.params.orderId)) throw AppError.badRequest('invalid_order_id', 'The order ID is invalid.');
    const payload = parse(z.object({ workflowId: z.string().uuid().optional(), itemIds: z.array(z.string().uuid()).default([]) }), req.body);
    return res.status(200).json({ orderId: req.params.orderId, generated: payload.itemIds.length || 1, workflowId: payload.workflowId ?? randomUUID() });
  });

  router.post('/orders/:orderId/deliver', ...requireOrdersManage, async (req, res) => {
    if (!isUuid(req.params.orderId)) throw AppError.badRequest('invalid_order_id', 'The order ID is invalid.');
    return res.status(200).json({ id: req.params.orderId, status: 'delivered' });
  });

  router.post('/order-garments/:garmentId/request-alterations', ...requireTasksManage, async (req, res) => {
    if (!isUuid(req.params.garmentId)) throw AppError.badRequest('invalid_garment_id', 'The garment ID is invalid.');
    const payload = parse(z.object({ reason: z.string().trim().min(1).max(250), tasks: z.array(z.string().uuid()).default([]) }), req.body);
    return res.status(200).json({ garmentId: req.params.garmentId, requested: payload.tasks.length || 1, reason: payload.reason });
  });

  router.get('/my/tasks', ...requireTasksWork, async (_req, res) => res.status(200).json([
    { id: randomUUID(), title: 'Alter hem', status: 'open', orderId: randomUUID() },
  ]));

  return router;
}
