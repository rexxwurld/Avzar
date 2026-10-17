import { Router } from 'express';
import { z } from 'zod';
import { AppError } from '../http/errors.js';
import { requireBearerAuth } from '../security/bearerAuth.js';
import { hashPassword, verifyPassword } from '../security/passwordHasher.js';

const forgotPasswordSchema = z.object({ email: z.string().trim().email().max(256) });
const resetPasswordSchema = z.object({ userId: z.string().uuid(), token: z.string().min(1).max(2000), newPassword: z.string().min(10).max(128) });
const changePasswordSchema = z.object({ currentPassword: z.string().min(1).max(128), newPassword: z.string().min(10).max(128) });

function parse(schema, body) {
  const result = schema.safeParse(body);
  if (!result.success) {
    const errors = Object.fromEntries(result.error.issues.map((issue) => [issue.path.join('.') || 'body', [issue.message]]));
    throw new AppError(400, 'validation_failed', 'One or more validation errors occurred.', { errors });
  }
  return result.data;
}

function toInt(value) {
  const asNumber = Number(value ?? 0);
  return Number.isFinite(asNumber) ? asNumber : 0;
}

export function createAccountRouter({ env, pool }) {
  const router = Router();

  router.post('/auth/confirm-email', async (req, res) => {
    const body = parse(z.object({ userId: z.string().uuid(), token: z.string().min(1).max(2000) }), req.body);
    if (!pool || !env.JWT_SIGNING_KEY) return res.status(204).end();
    await pool.query('SELECT 1 FROM "AspNetUsers" WHERE "Id" = $1 LIMIT 1', [body.userId]);
    return res.status(204).end();
  });

  router.post('/auth/forgot-password', async (req, res) => {
    const { email } = parse(forgotPasswordSchema, req.body);
    if (pool) {
      await pool.query('SELECT 1 FROM "AspNetUsers" WHERE "NormalizedEmail" = $1 LIMIT 1', [email.trim().toUpperCase()]);
    }
    return res.status(202).json({ status: 'accepted' });
  });

  router.post('/auth/reset-password', async (req, res) => {
    const body = parse(resetPasswordSchema, req.body);
    if (pool) {
      await pool.query('UPDATE "AspNetUsers" SET "PasswordHash" = $1 WHERE "Id" = $2', [await hashPassword(body.newPassword), body.userId]);
    }
    return res.status(204).end();
  });

  router.get('/account/security', requireBearerAuth(env, pool), async (req, res) => {
    if (!req.currentUser || !pool) {
      throw AppError.unauthorized('unauthorized', 'Authentication is required.');
    }
    const { rows } = await pool.query(`
      SELECT u."EmailConfirmed" AS "EmailConfirmed",
             u."TwoFactorEnabled" AS "TwoFactorEnabled",
             COALESCE((SELECT COUNT(*)::int FROM "UserSessions" WHERE "UserId" = u."Id" AND "RevokedAtUtc" IS NULL AND "AbsoluteExpiresAtUtc" > now()), 0) AS "ActiveSessions"
      FROM "AspNetUsers" u
      WHERE u."Id" = $1
      LIMIT 1`, [req.currentUser.userId]);
    const row = rows[0] ?? { EmailConfirmed: false, TwoFactorEnabled: false, ActiveSessions: 0 };
    return res.status(200).json({
      emailConfirmed: !!row.EmailConfirmed,
      twoFactorEnabled: !!row.TwoFactorEnabled,
      recoveryCodesLeft: 0,
      activeSessions: toInt(row.ActiveSessions),
    });
  });

  router.post('/account/change-password', requireBearerAuth(env, pool), async (req, res) => {
    if (!req.currentUser || !pool) {
      throw AppError.unauthorized('unauthorized', 'Authentication is required.');
    }
    const { currentPassword, newPassword } = parse(changePasswordSchema, req.body);
    const { rows } = await pool.query('SELECT "PasswordHash" FROM "AspNetUsers" WHERE "Id" = $1 LIMIT 1', [req.currentUser.userId]);
    const storedHash = rows[0]?.PasswordHash;
    const result = await verifyPassword(storedHash, currentPassword);
    if (!result.ok) {
      throw AppError.unauthorized('invalid_current_password', 'The current password is incorrect.');
    }
    const hashed = await hashPassword(newPassword);
    await pool.query('UPDATE "AspNetUsers" SET "PasswordHash" = $1 WHERE "Id" = $2', [hashed, req.currentUser.userId]);
    return res.status(204).end();
  });

  return router;
}
