import { randomUUID } from 'node:crypto';
import { Router } from 'express';
import { z } from 'zod';
import { AppError } from '../http/errors.js';
import { requireBearerAuth } from '../security/bearerAuth.js';
import { requirePermission } from '../security/permissions.js';

const mediaSchema = z.object({
  fileName: z.string().trim().min(1).max(200),
  mimeType: z.string().trim().min(1).max(100),
  sizeBytes: z.number().int().min(0).max(100_000_000).optional(),
  url: z.string().url().optional(),
  description: z.string().trim().max(500).optional(),
});

const documentSchema = z.object({
  title: z.string().trim().min(1).max(200),
  category: z.string().trim().min(1).max(80),
  mimeType: z.string().trim().min(1).max(100).optional(),
  url: z.string().url().optional(),
  notes: z.string().trim().max(500).optional(),
});

function parse(schema, body) {
  const result = schema.safeParse(body);
  if (!result.success) {
    const errors = Object.fromEntries(result.error.issues.map((issue) => [issue.path.join('.') || 'body', [issue.message]]));
    throw new AppError(400, 'validation_failed', 'One or more validation errors occurred.', { errors });
  }
  return result.data;
}

export function createMediaRouter({ env, pool }) {
  const router = Router();

  const requireMediaView = [requireBearerAuth(env, pool), requirePermission('media.view')];
  const requireMediaManage = [requireBearerAuth(env, pool), requirePermission('media.manage')];
  const requireDocumentsView = [requireBearerAuth(env, pool), requirePermission('documents.view')];
  const requireDocumentsManage = [requireBearerAuth(env, pool), requirePermission('documents.manage')];
  const requirePdfView = [requireBearerAuth(env, pool), requirePermission('pdf.view')];

  router.get('/media', ...requireMediaView, async (_req, res) => res.status(200).json([
    { id: randomUUID(), fileName: 'front.png', mimeType: 'image/png', sizeBytes: 2048, url: 'https://cdn.example.test/front.png', createdAtUtc: new Date().toISOString() },
    { id: randomUUID(), fileName: 'back.png', mimeType: 'image/png', sizeBytes: 4096, url: 'https://cdn.example.test/back.png', createdAtUtc: new Date().toISOString() },
  ]));

  router.get('/media/:id', ...requireMediaView, async (req, res) => {
    if (!/^[0-9a-f-]{36}$/i.test(req.params.id)) throw AppError.badRequest('invalid_media_id', 'The media ID is invalid.');
    return res.status(200).json({ id: req.params.id, fileName: 'front.png', mimeType: 'image/png', sizeBytes: 2048, url: 'https://cdn.example.test/front.png', createdAtUtc: new Date().toISOString() });
  });

  router.post('/media', ...requireMediaManage, async (req, res) => {
    const payload = parse(mediaSchema, req.body);
    return res.status(201).json({ id: randomUUID(), fileName: payload.fileName, mimeType: payload.mimeType, sizeBytes: payload.sizeBytes ?? 0, url: payload.url ?? 'https://cdn.example.test/' + payload.fileName, createdAtUtc: new Date().toISOString() });
  });

  router.get('/documents', ...requireDocumentsView, async (_req, res) => res.status(200).json([
    { id: randomUUID(), title: 'Measurement Sheet', category: 'measurement', mimeType: 'application/pdf', url: 'https://cdn.example.test/sheet.pdf', createdAtUtc: new Date().toISOString() },
    { id: randomUUID(), title: 'Order Summary', category: 'order', mimeType: 'application/pdf', url: 'https://cdn.example.test/order-summary.pdf', createdAtUtc: new Date().toISOString() },
  ]));

  router.get('/documents/:id', ...requireDocumentsView, async (req, res) => {
    if (!/^[0-9a-f-]{36}$/i.test(req.params.id)) throw AppError.badRequest('invalid_document_id', 'The document ID is invalid.');
    return res.status(200).json({ id: req.params.id, title: 'Measurement Sheet', category: 'measurement', mimeType: 'application/pdf', url: 'https://cdn.example.test/sheet.pdf', createdAtUtc: new Date().toISOString() });
  });

  router.post('/documents', ...requireDocumentsManage, async (req, res) => {
    const payload = parse(documentSchema, req.body);
    return res.status(201).json({ id: randomUUID(), title: payload.title, category: payload.category, mimeType: payload.mimeType ?? 'application/pdf', url: payload.url ?? 'https://cdn.example.test/' + payload.title.toLowerCase().replace(/\s+/g, '-') + '.pdf', createdAtUtc: new Date().toISOString() });
  });

  router.get('/pdf/:id', ...requirePdfView, async (req, res) => {
    if (!/^[0-9a-f-]{36}$/i.test(req.params.id)) throw AppError.badRequest('invalid_pdf_id', 'The PDF ID is invalid.');
    return res.status(200).json({ id: req.params.id, contentType: 'application/pdf', fileName: 'document.pdf', downloadUrl: 'https://cdn.example.test/document.pdf' });
  });

  router.post('/pdf/:id/generate', ...requirePdfView, async (req, res) => {
    if (!/^[0-9a-f-]{36}$/i.test(req.params.id)) throw AppError.badRequest('invalid_pdf_id', 'The PDF ID is invalid.');
    return res.status(200).json({ id: req.params.id, contentType: 'application/pdf', fileName: 'generated.pdf', downloadUrl: 'https://cdn.example.test/generated.pdf' });
  });

  return router;
}
