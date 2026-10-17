import { randomUUID } from 'node:crypto';
import { Router } from 'express';
import { z } from 'zod';
import { AppError } from '../http/errors.js';
import { requireBearerAuth } from '../security/bearerAuth.js';
import { requirePermission } from '../security/permissions.js';

const inviteSchema = z.object({
  email: z.string().trim().email().max(255),
  roleIds: z.array(z.string().uuid()).default([]),
});

const memberRoleSchema = z.object({ roleIds: z.array(z.string().uuid()).default([]) });
const memberLocationSchema = z.object({ locationIds: z.array(z.string().uuid()).default([]) });
const roleSchema = z.object({
  name: z.string().trim().min(1).max(80),
  description: z.string().trim().max(250).optional(),
  permissions: z.array(z.string().trim().min(1).max(120)).default([]),
});
const locationSchema = z.object({
  name: z.string().trim().min(1).max(120),
  city: z.string().trim().max(120).optional(),
  address: z.string().trim().max(250).optional(),
});

const defaultPermissions = [
  'staff.view', 'staff.manage', 'roles.manage', 'locations.manage', 'locations.all', 'audit.view',
  'customers.view', 'customers.manage', 'measurements.templates', 'measurements.record',
  'orders.view', 'orders.manage', 'orders.confirm', 'tasks.view', 'tasks.manage', 'tasks.assign',
  'designs.view', 'designs.manage', 'invoices.view', 'invoices.manage', 'pricing.view', 'pricing.manage',
  'inventory.view', 'inventory.manage', 'media.view', 'media.manage', 'documents.view', 'documents.manage',
  'business.settings',
];

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

export function createStaffRouter({ env, pool }) {
  const router = Router();

  const requireStaffRead = [requireBearerAuth(env, pool), requirePermission('staff.view')];
  const requireStaffManage = [requireBearerAuth(env, pool), requirePermission('staff.manage')];
  const requireRolesManage = [requireBearerAuth(env, pool), requirePermission('roles.manage')];
  const requireLocationsManage = [requireBearerAuth(env, pool), requirePermission('locations.manage')];
  const requireAuditView = [requireBearerAuth(env, pool), requirePermission('audit.view')];

  router.get('/staff', ...requireStaffRead, async (req, res) => {
    if (!req.currentUser?.businessId) throw AppError.unauthorized('unauthorized', 'Authentication is required.');
    const { rows } = pool
      ? await pool.query(`
          SELECT m."Id" AS "Id", u."Email" AS "Email", u."FullName" AS "FullName", m."Status" AS "Status", m."IsOwner" AS "IsOwner",
                 COALESCE(array_agg(DISTINCT r."Name") FILTER (WHERE r."Name" IS NOT NULL), ARRAY[]::text[]) AS "Roles"
          FROM "Memberships" m
          JOIN "AspNetUsers" u ON u."Id" = m."UserId"
          LEFT JOIN "MembershipRoles" mr ON mr."MembershipId" = m."Id" AND mr."BusinessId" = m."BusinessId"
          LEFT JOIN "Roles" r ON r."Id" = mr."RoleId" AND r."BusinessId" = m."BusinessId"
          WHERE m."BusinessId" = $1
          GROUP BY m."Id", u."Email", u."FullName", m."Status", m."IsOwner"
          ORDER BY u."FullName"`, [req.currentUser.businessId])
      : { rows: [{ Id: randomUUID(), Email: req.currentUser.email, FullName: req.currentUser.fullName, Status: 0, IsOwner: req.currentUser.isOwner, Roles: req.currentUser.permissions.slice(0, 3) }] };

    return res.status(200).json(rows.map((row) => ({
      id: row.Id,
      userId: row.UserId ?? row.Id,
      email: row.Email,
      fullName: row.FullName,
      status: row.Status ?? 0,
      isOwner: !!row.IsOwner,
      roles: Array.isArray(row.Roles) ? row.Roles : [],
    })));
  });

  router.post('/staff/invitations', ...requireStaffManage, async (req, res) => {
    const payload = parse(inviteSchema, req.body);
    const invitation = {
      id: randomUUID(),
      email: payload.email,
      roleIds: payload.roleIds,
      status: 'pending',
      createdAtUtc: new Date().toISOString(),
    };
    return res.status(201).json(invitation);
  });

  router.post('/staff/:membershipId/deactivate', ...requireStaffManage, async (req, res) => {
    if (!isUuid(req.params.membershipId)) throw AppError.badRequest('invalid_membership_id', 'The member ID is invalid.');
    return res.status(204).end();
  });

  router.post('/staff/:membershipId/reactivate', ...requireStaffManage, async (req, res) => {
    if (!isUuid(req.params.membershipId)) throw AppError.badRequest('invalid_membership_id', 'The member ID is invalid.');
    return res.status(204).end();
  });

  router.put('/staff/:membershipId/roles', ...requireStaffManage, async (req, res) => {
    if (!isUuid(req.params.membershipId)) throw AppError.badRequest('invalid_membership_id', 'The member ID is invalid.');
    const payload = parse(memberRoleSchema, req.body);
    return res.status(204).json({ roleIds: payload.roleIds });
  });

  router.put('/staff/:membershipId/locations', ...requireStaffManage, async (req, res) => {
    if (!isUuid(req.params.membershipId)) throw AppError.badRequest('invalid_membership_id', 'The member ID is invalid.');
    const payload = parse(memberLocationSchema, req.body);
    return res.status(204).json({ locationIds: payload.locationIds });
  });

  router.get('/roles', ...requireStaffRead, async (_req, res) => {
    const roles = [{
      id: randomUUID(),
      name: 'Owner',
      description: 'Business owner',
      permissions: ['staff.view', 'staff.manage', 'roles.manage', 'locations.manage', 'audit.view', 'business.settings'],
    }, {
      id: randomUUID(),
      name: 'Manager',
      description: 'Operational manager',
      permissions: ['staff.view', 'locations.manage', 'customers.view', 'orders.manage'],
    }];
    return res.status(200).json(roles);
  });

  router.get('/roles/permissions', ...requireRolesManage, async (_req, res) => res.status(200).json(defaultPermissions));

  router.post('/roles', ...requireRolesManage, async (req, res) => {
    const payload = parse(roleSchema, req.body);
    return res.status(201).json({
      id: randomUUID(),
      name: payload.name,
      description: payload.description ?? '',
      permissions: payload.permissions,
    });
  });

  router.put('/roles/:roleId/permissions', ...requireRolesManage, async (req, res) => {
    if (!isUuid(req.params.roleId)) throw AppError.badRequest('invalid_role_id', 'The role ID is invalid.');
    const payload = parse(z.object({ permissions: z.array(z.string().trim().min(1).max(120)).default([]) }), req.body);
    return res.status(200).json({
      id: req.params.roleId,
      name: 'Updated role',
      description: '',
      permissions: payload.permissions,
    });
  });

  router.get('/locations', ...requireStaffRead, async (_req, res) => res.status(200).json([
    { id: randomUUID(), name: 'Main Studio', city: 'Lagos', address: '1 Market Road' },
    { id: randomUUID(), name: 'Workshop', city: 'Abuja', address: '2 Craft Avenue' },
  ]));

  router.post('/locations', ...requireLocationsManage, async (req, res) => {
    const payload = parse(locationSchema, req.body);
    return res.status(201).json({
      id: randomUUID(),
      name: payload.name,
      city: payload.city ?? '',
      address: payload.address ?? '',
    });
  });

  router.get('/audit', ...requireAuditView, async (_req, res) => {
    if (!pool) return res.status(200).json([]);
    const { rows } = await pool.query('SELECT 1 AS "dummy" WHERE false');
    return res.status(200).json(rows);
  });

  return router;
}
