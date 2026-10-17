import { randomUUID } from 'node:crypto';
import { Router } from 'express';
import { z } from 'zod';
import { AppError } from '../http/errors.js';
import { requireBearerAuth } from '../security/bearerAuth.js';
import { requirePermission } from '../security/permissions.js';

const designSchema = z.object({
  name: z.string().trim().min(1).max(120),
  categoryId: z.string().uuid().optional(),
  basePrice: z.number().min(0).max(1_000_000_000).optional(),
  currencyCode: z.string().trim().length(3).optional(),
  tags: z.array(z.string().trim().min(1).max(40)).default([]),
});

const categorySchema = z.object({
  name: z.string().trim().min(1).max(120),
  description: z.string().trim().max(250).optional(),
});

const collectionSchema = z.object({
  name: z.string().trim().min(1).max(120),
  slug: z.string().trim().min(1).max(120),
  description: z.string().trim().max(500).optional(),
});

const pricingSchema = z.object({
  designId: z.string().uuid().optional(),
  basePrice: z.number().min(0).max(1_000_000_000).optional(),
  markupPercent: z.number().min(0).max(1000).optional(),
});

const inventorySchema = z.object({
  sku: z.string().trim().min(1).max(80),
  name: z.string().trim().min(1).max(120),
  unit: z.string().trim().min(1).max(40),
  onHand: z.number().min(0).max(1_000_000).optional(),
  reorderLevel: z.number().min(0).max(1_000_000).optional(),
  locationId: z.string().uuid().optional(),
});

function parse(schema, body) {
  const result = schema.safeParse(body);
  if (!result.success) {
    const errors = Object.fromEntries(result.error.issues.map((issue) => [issue.path.join('.') || 'body', [issue.message]]));
    throw new AppError(400, 'validation_failed', 'One or more validation errors occurred.', { errors });
  }
  return result.data;
}

export function createCatalogRouter({ env, pool }) {
  const router = Router();

  const requireDesignsView = [requireBearerAuth(env, pool), requirePermission('designs.view')];
  const requireDesignsManage = [requireBearerAuth(env, pool), requirePermission('designs.manage')];
  const requirePricingView = [requireBearerAuth(env, pool), requirePermission('pricing.view')];
  const requirePricingManage = [requireBearerAuth(env, pool), requirePermission('pricing.manage')];
  const requireInventoryView = [requireBearerAuth(env, pool), requirePermission('inventory.view')];
  const requireInventoryManage = [requireBearerAuth(env, pool), requirePermission('inventory.manage')];

  router.get('/designs', ...requireDesignsView, async (_req, res) => res.status(200).json([
    { id: randomUUID(), name: 'Tailored Jacket', categoryId: randomUUID(), basePrice: 280000, currencyCode: 'NGN', tags: ['outerwear'] },
    { id: randomUUID(), name: 'Merino Blazer', categoryId: randomUUID(), basePrice: 320000, currencyCode: 'NGN', tags: ['formal'] },
  ]));

  router.get('/designs/:id', ...requireDesignsView, async (req, res) => {
    if (!/^[0-9a-f-]{36}$/i.test(req.params.id)) throw AppError.badRequest('invalid_design_id', 'The design ID is invalid.');
    return res.status(200).json({ id: req.params.id, name: 'Tailored Jacket', categoryId: randomUUID(), basePrice: 280000, currencyCode: 'NGN', tags: ['outerwear'] });
  });

  router.post('/designs', ...requireDesignsManage, async (req, res) => {
    const payload = parse(designSchema, req.body);
    return res.status(201).json({ id: randomUUID(), name: payload.name, categoryId: payload.categoryId ?? randomUUID(), basePrice: payload.basePrice ?? 0, currencyCode: payload.currencyCode ?? 'NGN', tags: payload.tags });
  });

  router.put('/designs/:id', ...requireDesignsManage, async (req, res) => {
    if (!/^[0-9a-f-]{36}$/i.test(req.params.id)) throw AppError.badRequest('invalid_design_id', 'The design ID is invalid.');
    const payload = parse(designSchema, req.body);
    return res.status(200).json({ id: req.params.id, name: payload.name, categoryId: payload.categoryId ?? randomUUID(), basePrice: payload.basePrice ?? 0, currencyCode: payload.currencyCode ?? 'NGN', tags: payload.tags });
  });

  router.post('/designs/:id/status', ...requireDesignsManage, async (req, res) => {
    if (!/^[0-9a-f-]{36}$/i.test(req.params.id)) throw AppError.badRequest('invalid_design_id', 'The design ID is invalid.');
    const payload = parse(z.object({ status: z.string().trim().min(1).max(40) }), req.body);
    return res.status(200).json({ id: req.params.id, status: payload.status });
  });

  router.put('/designs/:id/images', ...requireDesignsManage, async (req, res) => {
    if (!/^[0-9a-f-]{36}$/i.test(req.params.id)) throw AppError.badRequest('invalid_design_id', 'The design ID is invalid.');
    const payload = parse(z.object({ imageUrls: z.array(z.string().url()).default([]) }), req.body);
    return res.status(200).json({ id: req.params.id, imageUrls: payload.imageUrls });
  });

  router.post('/designs/:id/visibility', ...requireDesignsManage, async (req, res) => {
    if (!/^[0-9a-f-]{36}$/i.test(req.params.id)) throw AppError.badRequest('invalid_design_id', 'The design ID is invalid.');
    const payload = parse(z.object({ visibility: z.enum(['public', 'private']) }), req.body);
    return res.status(200).json({ id: req.params.id, visibility: payload.visibility });
  });

  router.get('/design-categories', ...requireDesignsView, async (_req, res) => res.status(200).json([
    { id: randomUUID(), name: 'Outerwear', description: 'Jackets and coats' },
    { id: randomUUID(), name: 'Formalwear', description: 'Dress and tailored fits' },
  ]));

  router.post('/design-categories', ...requireDesignsManage, async (req, res) => {
    const payload = parse(categorySchema, req.body);
    return res.status(201).json({ id: randomUUID(), name: payload.name, description: payload.description ?? '' });
  });

  router.put('/design-categories/:id', ...requireDesignsManage, async (req, res) => {
    if (!/^[0-9a-f-]{36}$/i.test(req.params.id)) throw AppError.badRequest('invalid_category_id', 'The category ID is invalid.');
    const payload = parse(categorySchema, req.body);
    return res.status(200).json({ id: req.params.id, name: payload.name, description: payload.description ?? '' });
  });

  router.delete('/design-categories/:id', ...requireDesignsManage, async (req, res) => {
    if (!/^[0-9a-f-]{36}$/i.test(req.params.id)) throw AppError.badRequest('invalid_category_id', 'The category ID is invalid.');
    return res.status(204).end();
  });

  router.get('/collections', ...requireDesignsView, async (_req, res) => res.status(200).json([
    { id: randomUUID(), name: 'Spring Edit', slug: 'spring-edit', description: '', sortOrder: 1, designCount: 12 },
  ]));

  router.post('/collections', ...requireDesignsManage, async (req, res) => {
    const payload = parse(collectionSchema, req.body);
    return res.status(201).json({ id: randomUUID(), name: payload.name, slug: payload.slug, description: payload.description ?? '', sortOrder: 1, designCount: 0 });
  });

  router.put('/collections/:id', ...requireDesignsManage, async (req, res) => {
    if (!/^[0-9a-f-]{36}$/i.test(req.params.id)) throw AppError.badRequest('invalid_collection_id', 'The collection ID is invalid.');
    const payload = parse(collectionSchema, req.body);
    return res.status(200).json({ id: req.params.id, name: payload.name, slug: payload.slug, description: payload.description ?? '', sortOrder: 1, designCount: 0 });
  });

  router.get('/pricing', ...requirePricingView, async (_req, res) => res.status(200).json([
    { id: randomUUID(), designId: randomUUID(), basePrice: 280000, markupPercent: 15, finalPrice: 322000, currencyCode: 'NGN' },
  ]));

  router.post('/pricing', ...requirePricingManage, async (req, res) => {
    const payload = parse(pricingSchema, req.body);
    return res.status(201).json({ id: randomUUID(), designId: payload.designId ?? randomUUID(), basePrice: payload.basePrice ?? 0, markupPercent: payload.markupPercent ?? 0, finalPrice: (payload.basePrice ?? 0) * (1 + ((payload.markupPercent ?? 0) / 100)), currencyCode: 'NGN' });
  });

  router.get('/inventory', ...requireInventoryView, async (_req, res) => res.status(200).json([
    { id: randomUUID(), sku: 'FAB-1001', name: 'Cotton Blend', unit: 'metre', onHand: 30, reorderLevel: 8, locationId: randomUUID(), reserved: 4, available: 26, lowStock: false },
  ]));

  router.get('/inventory/locations', ...requireInventoryView, async (_req, res) => res.status(200).json([
    { id: randomUUID(), name: 'Main Studio', city: 'Lagos' },
  ]));

  router.post('/inventory', ...requireInventoryManage, async (req, res) => {
    const payload = parse(inventorySchema, req.body);
    return res.status(201).json({ id: randomUUID(), sku: payload.sku, name: payload.name, unit: payload.unit, onHand: payload.onHand ?? 0, reorderLevel: payload.reorderLevel ?? 0, locationId: payload.locationId ?? randomUUID(), reserved: 0, available: payload.onHand ?? 0, lowStock: false });
  });

  router.post('/inventory/:id/:actionName', ...requireInventoryManage, async (req, res) => {
    if (!/^[0-9a-f-]{36}$/i.test(req.params.id)) throw AppError.badRequest('invalid_inventory_id', 'The inventory ID is invalid.');
    const payload = parse(z.object({ delta: z.number().int().min(-100000).max(100000).default(0), reason: z.string().trim().max(200).optional() }), req.body);
    return res.status(200).json({ id: req.params.id, delta: payload.delta, reason: payload.reason ?? '', updatedAtUtc: new Date().toISOString() });
  });

  router.post('/inventory/tasks/:taskId/reserve', ...requireInventoryManage, async (req, res) => {
    if (!/^[0-9a-f-]{36}$/i.test(req.params.taskId)) throw AppError.badRequest('invalid_task_id', 'The task ID is invalid.');
    return res.status(200).json([{ id: randomUUID(), sku: 'FAB-1001', name: 'Cotton Blend', reserved: 2 }]);
  });

  router.post('/inventory/tasks/:taskId/release', ...requireInventoryManage, async (req, res) => {
    if (!/^[0-9a-f-]{36}$/i.test(req.params.taskId)) throw AppError.badRequest('invalid_task_id', 'The task ID is invalid.');
    return res.status(200).json([{ id: randomUUID(), sku: 'FAB-1001', name: 'Cotton Blend', reserved: 0 }]);
  });

  router.post('/inventory/tasks/:taskId/consume', ...requireInventoryManage, async (req, res) => {
    if (!/^[0-9a-f-]{36}$/i.test(req.params.taskId)) throw AppError.badRequest('invalid_task_id', 'The task ID is invalid.');
    return res.status(200).json([{ id: randomUUID(), sku: 'FAB-1001', name: 'Cotton Blend', onHand: 24 }]);
  });

  router.get('/inventory/:id/movements', ...requireInventoryView, async (req, res) => {
    if (!/^[0-9a-f-]{36}$/i.test(req.params.id)) throw AppError.badRequest('invalid_inventory_id', 'The inventory ID is invalid.');
    return res.status(200).json([{ id: randomUUID(), quantity: 5, reason: 'stock-in', createdAtUtc: new Date().toISOString() }]);
  });

  return router;
}
