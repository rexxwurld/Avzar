import { randomUUID } from 'node:crypto';
import { Router } from 'express';
import { z } from 'zod';
import { AppError } from '../http/errors.js';
import { requireBearerAuth } from '../security/bearerAuth.js';
import { requirePermission } from '../security/permissions.js';

const storefrontSchema = z.object({
  name: z.string().trim().min(1).max(160),
  slug: z.string().trim().min(1).max(120),
  status: z.enum(['draft', 'published', 'archived']).optional(),
  description: z.string().trim().max(1000).optional(),
  heroImageUrl: z.string().url().optional(),
});

const inquirySchema = z.object({
  customerName: z.string().trim().min(1).max(160),
  email: z.string().trim().email().max(255),
  subject: z.string().trim().min(1).max(200),
  message: z.string().trim().min(1).max(2000),
});

function parse(schema, body) {
  const result = schema.safeParse(body);
  if (!result.success) {
    const errors = Object.fromEntries(result.error.issues.map((issue) => [issue.path.join('.') || 'body', [issue.message]]));
    throw new AppError(400, 'validation_failed', 'One or more validation errors occurred.', { errors });
  }
  return result.data;
}

export function createStorefrontRouter({ env, pool }) {
  const router = Router();

  const requireStorefrontView = [requireBearerAuth(env, pool), requirePermission('storefront.view')];
  const requireStorefrontEdit = [requireBearerAuth(env, pool), requirePermission('storefront.edit')];
  const requireStorefrontPublish = [requireBearerAuth(env, pool), requirePermission('storefront.publish')];
  const requireInquiriesView = [requireBearerAuth(env, pool), requirePermission('inquiries.view')];
  const requireInquiriesManage = [requireBearerAuth(env, pool), requirePermission('inquiries.manage')];

  router.get('/storefronts', ...requireStorefrontView, async (_req, res) => res.status(200).json([
    { id: randomUUID(), name: 'Studio storefront', slug: 'studio-storefront', status: 'draft', description: 'Handmade couture', heroImageUrl: 'https://cdn.example.test/hero.jpg' },
    { id: randomUUID(), name: 'Bridal collection', slug: 'bridal-collection', status: 'published', description: 'Wedding and occasion tailoring', heroImageUrl: 'https://cdn.example.test/bridal.jpg' },
  ]));

  router.get('/storefronts/:id', ...requireStorefrontView, async (req, res) => {
    if (!/^[0-9a-f-]{36}$/i.test(req.params.id)) throw AppError.badRequest('invalid_storefront_id', 'The storefront ID is invalid.');
    return res.status(200).json({ id: req.params.id, name: 'Studio storefront', slug: 'studio-storefront', status: 'draft', description: 'Handmade couture', heroImageUrl: 'https://cdn.example.test/hero.jpg' });
  });

  router.post('/storefronts', ...requireStorefrontEdit, async (req, res) => {
    const payload = parse(storefrontSchema, req.body);
    return res.status(201).json({ id: randomUUID(), name: payload.name, slug: payload.slug, status: payload.status ?? 'draft', description: payload.description ?? '', heroImageUrl: payload.heroImageUrl ?? 'https://cdn.example.test/default.jpg' });
  });

  router.put('/storefronts/:id', ...requireStorefrontEdit, async (req, res) => {
    if (!/^[0-9a-f-]{36}$/i.test(req.params.id)) throw AppError.badRequest('invalid_storefront_id', 'The storefront ID is invalid.');
    const payload = parse(storefrontSchema, req.body);
    return res.status(200).json({ id: req.params.id, name: payload.name, slug: payload.slug, status: payload.status ?? 'draft', description: payload.description ?? '', heroImageUrl: payload.heroImageUrl ?? 'https://cdn.example.test/default.jpg' });
  });

  router.post('/storefronts/:id/publish', ...requireStorefrontPublish, async (req, res) => {
    if (!/^[0-9a-f-]{36}$/i.test(req.params.id)) throw AppError.badRequest('invalid_storefront_id', 'The storefront ID is invalid.');
    return res.status(200).json({ id: req.params.id, name: 'Studio storefront', status: 'published', publishedAtUtc: new Date().toISOString() });
  });

  router.get('/public/site', async (_req, res) => {
    return res.status(200).json({
      siteName: 'Shop',
      slug: 'shop',
      locale: 'en-NG',
      currencyCode: 'NGN',
      contactEmail: 'hello@shop.example',
      homeTitle: 'Tailored fashion for everyday life',
    });
  });

  router.get('/inquiries', ...requireInquiriesView, async (_req, res) => res.status(200).json([
    { id: randomUUID(), customerName: 'Jane Doe', email: 'jane@example.test', subject: 'Custom order', message: 'Need a bridal gown', status: 'new', createdAtUtc: new Date().toISOString() },
    { id: randomUUID(), customerName: 'John Smith', email: 'john@example.test', subject: 'Alteration', message: 'Please review my suit', status: 'in_progress', createdAtUtc: new Date().toISOString() },
  ]));

  router.post('/inquiries', ...requireInquiriesManage, async (req, res) => {
    const payload = parse(inquirySchema, req.body);
    return res.status(201).json({
      id: randomUUID(),
      customerName: payload.customerName,
      email: payload.email,
      subject: payload.subject,
      message: payload.message,
      status: 'new',
      createdAtUtc: new Date().toISOString(),
    });
  });

  return router;
}
