import { randomUUID } from 'node:crypto';
import { Router } from 'express';
import { z } from 'zod';
import { AppError } from '../http/errors.js';
import { requireBearerAuth } from '../security/bearerAuth.js';
import { requirePermission } from '../security/permissions.js';

const planSchema = z.object({
  name: z.string().trim().min(1).max(80),
  price: z.number().min(0).max(1_000_000_000).optional(),
  currencyCode: z.string().trim().length(3).optional(),
  features: z.array(z.string().trim().min(1).max(80)).default([]),
});

const aiSchema = z.object({
  prompt: z.string().trim().min(1).max(2000),
  context: z.record(z.any()).default({}),
});

function parse(schema, body) {
  const result = schema.safeParse(body);
  if (!result.success) {
    const errors = Object.fromEntries(result.error.issues.map((issue) => [issue.path.join('.') || 'body', [issue.message]]));
    throw new AppError(400, 'validation_failed', 'One or more validation errors occurred.', { errors });
  }
  return result.data;
}

export function createBillingRouter({ env, pool }) {
  const router = Router();

  const requirePlansView = [requireBearerAuth(env, pool), requirePermission('plans.view')];
  const requirePlansManage = [requireBearerAuth(env, pool), requirePermission('plans.manage')];
  const requireBillingView = [requireBearerAuth(env, pool), requirePermission('billing.view')];
  const requireBillingManage = [requireBearerAuth(env, pool), requirePermission('billing.manage')];
  const requireAiView = [requireBearerAuth(env, pool), requirePermission('ai.view')];
  const requireAiUse = [requireBearerAuth(env, pool), requirePermission('ai.use')];

  router.get('/plans', ...requirePlansView, async (_req, res) => res.status(200).json([
    { id: randomUUID(), name: 'Starter', price: 50000, currencyCode: 'NGN', features: ['orders', 'customers'] },
    { id: randomUUID(), name: 'Pro', price: 200000, currencyCode: 'NGN', features: ['orders', 'customers', 'ai'] },
  ]));

  router.post('/plans', ...requirePlansManage, async (req, res) => {
    const payload = parse(planSchema, req.body);
    return res.status(201).json({ id: randomUUID(), name: payload.name, price: payload.price ?? 0, currencyCode: payload.currencyCode ?? 'NGN', features: payload.features });
  });

  router.get('/billing', ...requireBillingView, async (_req, res) => res.status(200).json([
    { id: randomUUID(), businessId: randomUUID(), status: 'active', planName: 'Pro', nextBillingAtUtc: new Date().toISOString() },
  ]));

  router.post('/billing/checkout', ...requireBillingManage, async (req, res) => {
    const payload = parse(z.object({ planId: z.string().uuid(), amount: z.number().min(0).max(1_000_000_000).optional() }), req.body);
    return res.status(201).json({ id: randomUUID(), planId: payload.planId, amount: payload.amount ?? 0, status: 'pending', redirectUrl: 'https://billing.example.test/checkout/' + randomUUID() });
  });

  router.get('/ai', ...requireAiView, async (_req, res) => res.status(200).json({
    provider: 'mock',
    available: true,
    model: 'gpt-4o-mini',
    dailyLimit: 100,
  }));

  router.get('/ai/assist', ...requireAiView, async (_req, res) => res.status(200).json({
    answer: 'AI assistance is available for this business.',
    model: 'gpt-4o-mini',
    context: {},
  }));

  router.post('/ai/assist', ...requireAiUse, async (req, res) => {
    const payload = parse(aiSchema, req.body);
    return res.status(200).json({ answer: `AI response for: ${payload.prompt.slice(0, 120)}`, model: 'gpt-4o-mini', context: payload.context ?? {} });
  });

  return router;
}
