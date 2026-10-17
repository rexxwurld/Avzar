import { randomUUID } from 'node:crypto';
import { Router } from 'express';
import { z } from 'zod';
import { AppError } from '../http/errors.js';
import { requireBearerAuth } from '../security/bearerAuth.js';
import { requirePermission } from '../security/permissions.js';

const customerSchema = z.object({
  firstName: z.string().trim().min(1).max(80).optional(),
  lastName: z.string().trim().min(1).max(80).optional(),
  email: z.string().trim().email().max(255).optional(),
  phone: z.string().trim().max(50).optional(),
  status: z.string().trim().max(40).optional(),
});

const measurementTemplateSchema = z.object({
  name: z.string().trim().min(1).max(120),
  description: z.string().trim().max(250).optional(),
  fields: z.array(z.string().trim().min(1).max(80)).min(1).default([]),
});

const communicationSchema = z.object({
  type: z.string().trim().min(1).max(40),
  note: z.string().trim().min(1).max(1000),
});

const measurementSetSchema = z.object({
  templateId: z.string().uuid().optional(),
  values: z.record(z.string(), z.union([z.string(), z.number(), z.boolean(), z.null()])).default({}),
});

function parse(schema, body) {
  const result = schema.safeParse(body);
  if (!result.success) {
    const errors = Object.fromEntries(result.error.issues.map((issue) => [issue.path.join('.') || 'body', [issue.message]]));
    throw new AppError(400, 'validation_failed', 'One or more validation errors occurred.', { errors });
  }
  return result.data;
}

function isGuid(value) {
  return typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(value);
}

export function createCustomerRouter({ env, pool }) {
  const router = Router();

  const requireCustomersView = [requireBearerAuth(env, pool), requirePermission('customers.view')];
  const requireCustomersManage = [requireBearerAuth(env, pool), requirePermission('customers.manage')];
  const requireMeasurementsTemplates = [requireBearerAuth(env, pool), requirePermission('measurements.templates')];
  const requireMeasurementsRecord = [requireBearerAuth(env, pool), requirePermission('measurements.record')];

  router.get('/customers', ...requireCustomersView, async (_req, res) => {
    const customers = [
      { id: randomUUID(), firstName: 'Ada', lastName: 'Okafor', email: 'ada@example.test', phone: '+2348000000001', status: 'active' },
      { id: randomUUID(), firstName: 'John', lastName: 'Smith', email: 'john@example.test', phone: '+2348000000002', status: 'prospect' },
    ];
    return res.status(200).json(customers);
  });

  router.get('/customers/:id', ...requireCustomersView, async (req, res) => {
    if (!isGuid(req.params.id)) throw AppError.badRequest('invalid_customer_id', 'The customer ID is invalid.');
    return res.status(200).json({
      id: req.params.id,
      firstName: 'Ada',
      lastName: 'Okafor',
      email: 'ada@example.test',
      phone: '+2348000000001',
      status: 'active',
    });
  });

  router.post('/customers', ...requireCustomersManage, async (req, res) => {
    const payload = parse(customerSchema, req.body);
    const customer = {
      id: randomUUID(),
      firstName: payload.firstName ?? 'New',
      lastName: payload.lastName ?? 'Customer',
      email: payload.email ?? 'new@example.test',
      phone: payload.phone ?? '',
      status: payload.status ?? 'active',
    };
    return res.status(201).json(customer);
  });

  router.put('/customers/:id', ...requireCustomersManage, async (req, res) => {
    if (!isGuid(req.params.id)) throw AppError.badRequest('invalid_customer_id', 'The customer ID is invalid.');
    const payload = parse(customerSchema, req.body);
    return res.status(200).json({
      id: req.params.id,
      firstName: payload.firstName ?? 'Updated',
      lastName: payload.lastName ?? 'Customer',
      email: payload.email ?? 'updated@example.test',
      phone: payload.phone ?? '',
      status: payload.status ?? 'active',
    });
  });

  router.post('/customers/:id/status', ...requireCustomersManage, async (req, res) => {
    if (!isGuid(req.params.id)) throw AppError.badRequest('invalid_customer_id', 'The customer ID is invalid.');
    const payload = parse(z.object({ status: z.string().trim().min(1).max(40) }), req.body);
    return res.status(200).json({ id: req.params.id, status: payload.status });
  });

  router.post('/customers/:sourceId/merge-into/:targetId', ...[requireBearerAuth(env, pool), requirePermission('customers.merge')], async (req, res) => {
    if (!isGuid(req.params.sourceId) || !isGuid(req.params.targetId)) {
      throw AppError.badRequest('invalid_customer_ids', 'The customer IDs are invalid.');
    }
    return res.status(200).json({ sourceId: req.params.sourceId, targetId: req.params.targetId, status: 'merged' });
  });

  router.get('/customers/:id/communications', ...requireCustomersView, async (req, res) => {
    if (!isGuid(req.params.id)) throw AppError.badRequest('invalid_customer_id', 'The customer ID is invalid.');
    return res.status(200).json([
      { id: randomUUID(), type: 'call', note: 'Followed up on measurements', createdAtUtc: new Date().toISOString() },
    ]);
  });

  router.post('/customers/:id/communications', ...requireCustomersManage, async (req, res) => {
    if (!isGuid(req.params.id)) throw AppError.badRequest('invalid_customer_id', 'The customer ID is invalid.');
    const payload = parse(communicationSchema, req.body);
    return res.status(201).json({ id: randomUUID(), customerId: req.params.id, type: payload.type, note: payload.note, createdAtUtc: new Date().toISOString() });
  });

  router.get('/customers/:id/measurements', ...requireCustomersView, async (req, res) => {
    if (!isGuid(req.params.id)) throw AppError.badRequest('invalid_customer_id', 'The customer ID is invalid.');
    return res.status(200).json([
      { id: randomUUID(), templateId: randomUUID(), values: { chest: 42, waist: 34 }, createdAtUtc: new Date().toISOString() },
    ]);
  });

  router.post('/customers/:id/measurements', ...requireMeasurementsRecord, async (req, res) => {
    if (!isGuid(req.params.id)) throw AppError.badRequest('invalid_customer_id', 'The customer ID is invalid.');
    const payload = parse(measurementSetSchema, req.body);
    return res.status(201).json({
      id: randomUUID(),
      customerId: req.params.id,
      templateId: payload.templateId ?? randomUUID(),
      values: payload.values,
      createdAtUtc: new Date().toISOString(),
    });
  });

  router.get('/measurement-templates', ...requireCustomersView, async (_req, res) => {
    return res.status(200).json([
      { id: randomUUID(), name: 'Classic Shirt', description: 'Standard shirt fit', fields: ['chest', 'waist'] },
    ]);
  });

  router.get('/measurement-templates/:id', ...requireCustomersView, async (req, res) => {
    if (!isGuid(req.params.id)) throw AppError.badRequest('invalid_template_id', 'The template ID is invalid.');
    return res.status(200).json({ id: req.params.id, name: 'Classic Shirt', description: 'Standard shirt fit', fields: ['chest', 'waist'] });
  });

  router.post('/measurement-templates', ...requireMeasurementsTemplates, async (req, res) => {
    const payload = parse(measurementTemplateSchema, req.body);
    return res.status(201).json({ id: randomUUID(), name: payload.name, description: payload.description ?? '', fields: payload.fields });
  });

  router.put('/measurement-templates/:id', ...requireMeasurementsTemplates, async (req, res) => {
    if (!isGuid(req.params.id)) throw AppError.badRequest('invalid_template_id', 'The template ID is invalid.');
    const payload = parse(measurementTemplateSchema, req.body);
    return res.status(200).json({ id: req.params.id, name: payload.name, description: payload.description ?? '', fields: payload.fields });
  });

  router.post('/measurement-sets/:id/revisions', ...requireMeasurementsRecord, async (req, res) => {
    if (!isGuid(req.params.id)) throw AppError.badRequest('invalid_measurement_set_id', 'The measurement set ID is invalid.');
    const payload = parse(measurementSetSchema, req.body);
    return res.status(200).json({ id: req.params.id, templateId: payload.templateId ?? randomUUID(), values: payload.values });
  });

  router.get('/measurement-sets/:id/history', ...requireCustomersView, async (req, res) => {
    if (!isGuid(req.params.id)) throw AppError.badRequest('invalid_measurement_set_id', 'The measurement set ID is invalid.');
    return res.status(200).json([{ id: 1, note: 'initial recording', createdAtUtc: new Date().toISOString() }]);
  });

  router.get('/measurement-sets/:id/compare', ...requireCustomersView, async (req, res) => {
    if (!isGuid(req.params.id)) throw AppError.badRequest('invalid_measurement_set_id', 'The measurement set ID is invalid.');
    return res.status(200).json([{ field: 'chest', before: 40, after: 42, delta: 2 }]);
  });

  return router;
}
