import { randomUUID } from 'node:crypto';
import { Router } from 'express';
import { z } from 'zod';
import { AppError } from '../http/errors.js';
import { requireBearerAuth } from '../security/bearerAuth.js';
import { requirePermission } from '../security/permissions.js';
import { applyShopSchedule, previewShopSchedule } from './productionScheduleService.js';
import {
  addWorkerLeave,
  cancelWorkerLeave,
  getWorkerAvailability,
  getWorkerCalendar,
  listPendingWorkerLeave,
  listWorkerLeave,
  requestMyLeave,
  reviewWorkerLeave,
  setWorkerAvailability,
} from './workerScheduleService.js';
import { createFitting, listFittings, setFittingStatus } from './fittingService.js';

const garmentSchema = z.object({
  id: z.string().uuid().optional(),
  name: z.string().trim().min(1).max(120),
  category: z.string().trim().max(80).nullish(),
  quantity: z.number().int().min(1).max(10_000).default(1),
  unitPrice: z.number().min(0).max(100_000_000),
  fabric: z.string().trim().max(120).nullish(),
  colour: z.string().trim().max(60).nullish(),
  styleInstructions: z.string().trim().max(4000).nullish(),
  alterationNotes: z.string().trim().max(2000).nullish(),
  measurementSetId: z.string().uuid().nullish(),
  measurementRevision: z.number().int().min(1).nullish(),
  designId: z.string().uuid().nullish(),
});

const orderHeaderSchema = z.object({
  locationId: z.string().uuid(),
  promisedDate: z.string().regex(/^\d{4}-\d{2}-\d{2}$/),
  priority: z.enum(['Low', 'Normal', 'High', 'Urgent']).default('Normal'),
  deliveryMethod: z.enum(['Collection', 'Delivery']).default('Collection'),
  deliveryAddress: z.string().trim().max(500).nullish(),
  deliveryInstructions: z.string().trim().max(1000).nullish(),
  notes: z.string().trim().max(4000).nullish(),
  discount: z.number().min(0).max(100_000_000).default(0),
  garments: z.array(garmentSchema).min(1).max(100),
});

const createOrderSchema = orderHeaderSchema.extend({ customerId: z.string().uuid() });
const updateOrderSchema = orderHeaderSchema.extend({
  version: z.number().int().min(0).max(4_294_967_295),
});

const fittingSchema = z.object({
  customerId: z.string().uuid(),
  locationId: z.string().uuid(),
  orderId: z.string().uuid().optional(),
  scheduledAt: z.string().datetime({ offset: true }),
  durationMinutes: z.number().int().min(5).max(480).default(30),
  notes: z.string().trim().max(2000).nullable().optional(),
});
const fittingStatusSchema = z.object({
  status: z.enum(['Scheduled', 'Completed', 'Cancelled', 'NoShow']),
  notes: z.string().trim().max(2000).nullable().optional(),
});

const workflowSchema = z.object({
  name: z.string().trim().min(2).max(80),
  description: z.string().trim().max(500).optional(),
  isDefault: z.boolean().default(false),
  isActive: z.boolean().default(true),
  garmentCategories: z.array(z.string().trim().min(1).max(80)).max(50).default([]),
  stages: z.array(z.object({
    key: z.string().regex(/^[a-z][a-z0-9_]{0,39}$/),
    name: z.string().trim().min(2).max(80),
    instructions: z.string().trim().max(2000).nullish(),
    requiredSkill: z.string().trim().max(60).nullish(),
    defaultMinutes: z.number().int().min(1).max(1440).nullish(),
    isConditional: z.boolean().default(false),
    completionChecks: z.array(z.string().trim().min(1).max(200)).max(10).default([]),
    dependsOn: z.array(z.string().regex(/^[a-z][a-z0-9_]{0,39}$/)).max(30).optional(),
  })).min(1).max(30),
});

const materialRequirementSchema = z.object({
  name: z.string().trim().min(1).max(120),
  quantity: z.number().positive().max(1_000_000),
  unit: z.string().trim().min(1).max(20),
});

const createTaskSchema = z.object({
  title: z.string().trim().min(2).max(160),
  orderGarmentId: z.string().uuid().optional(),
  stageName: z.string().trim().max(80).optional(),
  instructions: z.string().trim().max(4000).nullish(),
  requiredSkill: z.string().trim().max(60).nullish(),
  estimatedMinutes: z.number().int().min(1).max(100_000).nullish(),
  priority: z.enum(['Low', 'Normal', 'High', 'Urgent']).default('Normal'),
  deadline: z.string().datetime().nullish(),
  completionChecks: z.array(z.string().trim().min(1).max(200)).max(10).default([]),
  requiredMaterials: z.array(materialRequirementSchema).max(100).default([]),
  dependsOnTaskIds: z.array(z.string().uuid()).max(100).default([]),
});
const addTaskSchema = createTaskSchema.extend({ orderId: z.string().uuid() });

const updateTaskSchema = z.object({
  version: z.number().int().min(0).max(4_294_967_295),
  instructions: z.string().trim().max(4000).nullish(),
  priority: z.enum(['Low', 'Normal', 'High', 'Urgent']).optional(),
  deadline: z.string().datetime().nullish(),
  estimatedMinutes: z.number().int().min(1).max(100_000).nullish(),
  requiredSkill: z.string().trim().max(60).nullish(),
  requiredMaterials: z.array(materialRequirementSchema).max(100).optional(),
});

const assignTaskSchema = z.object({
  membershipId: z.string().uuid().nullish(),
  reason: z.string().trim().max(500).optional(),
  override: z.boolean().default(false),
});
const taskNoteSchema = z.object({ note: z.string().trim().max(1000).nullish() });
const cancelTaskSchema = z.object({ reason: z.string().trim().min(3).max(500) });
const overrideTaskSchema = z.object({
  action: z.enum(['Start', 'Complete']),
  reason: z.string().trim().min(5).max(500),
});
const completeTaskSchema = z.object({
  notes: z.string().trim().max(2000).nullish(),
  completedChecks: z.array(z.string().trim().max(200)).max(10).default([]),
  alterationsRequired: z.boolean().default(false),
  alterationNotes: z.string().trim().max(2000).nullish(),
});
const reportTaskIssueSchema = z.object({
  type: z.enum(['MissingMaterials', 'Defect', 'Clarification', 'Other']),
  description: z.string().trim().min(3).max(2000),
});
const requestAlterationsSchema = z.object({ notes: z.string().trim().min(3).max(2000) });

const generateTasksSchema = z.object({
  workflowOverrides: z.record(z.string().uuid(), z.string().uuid()).optional(),
});
const setWorkerSkillsSchema = z.object({
  skills: z.array(z.string().trim().min(1).max(60)).max(30),
});
const schedulePreviewSchema = z.object({
  startUtc: z.string().datetime({ offset: true }),
  horizonDays: z.number().int().min(1).max(366).default(60),
});
const scheduleApplySchema = schedulePreviewSchema.extend({
  expectedTaskVersions: z.record(z.string().uuid(), z.number().int().min(0).max(4_294_967_295)),
  expectedSlots: z.array(z.object({
    taskId: z.string().uuid(),
    startUtc: z.string().datetime({ offset: true }),
    endUtc: z.string().datetime({ offset: true }),
    version: z.number().int().min(0).max(4_294_967_295),
  })),
});
const workerAvailabilityDaySchema = z.object({
  dayOfWeek: z.number().int().min(0).max(6),
  isAvailable: z.boolean(),
  opensAt: z.string().regex(/^([01]\d|2[0-3]):[0-5]\d(?::[0-5]\d(?:\.\d{1,6})?)?$/).nullable().optional(),
  closesAt: z.string().regex(/^([01]\d|2[0-3]):[0-5]\d(?::[0-5]\d(?:\.\d{1,6})?)?$/).nullable().optional(),
  capacityMinutes: z.number().int().min(0).max(1440),
});
const setWorkerAvailabilitySchema = z.object({ days: z.array(workerAvailabilityDaySchema).length(7) });
const workerLeaveSchema = z.object({
  startsAtUtc: z.string().datetime({ offset: true }),
  endsAtUtc: z.string().datetime({ offset: true }),
  reason: z.string().trim().min(2).max(300),
});
const workerLeaveReviewSchema = z.object({
  approve: z.boolean(),
  note: z.string().trim().max(500).nullable().optional(),
});

function validateWorkflow(payload) {
  const keys = payload.stages.map((stage) => stage.key);
  const problems = [];
  if (new Set(keys).size !== keys.length) problems.push('Stage keys must be unique.');
  if (payload.stages.every((stage) => stage.isConditional)) problems.push('At least one stage must be non-conditional.');
  const dependencies = new Map();
  payload.stages.forEach((stage, index) => {
    const deps = [...new Set(stage.dependsOn ?? (index > 0 ? [keys[index - 1]] : []))];
    if (stage.isConditional && deps.length === 0) problems.push(`Conditional stage '${stage.key}' needs at least one prerequisite.`);
    if (deps.includes(stage.key)) problems.push(`Stage '${stage.key}' cannot depend on itself.`);
    if (deps.some((key) => !keys.includes(key))) problems.push(`Stage '${stage.key}' depends on an unknown stage.`);
    dependencies.set(stage.key, deps);
  });
  const visited = new Set();
  const visiting = new Set();
  function hasCycle(key) {
    if (visiting.has(key)) return true;
    if (visited.has(key)) return false;
    visiting.add(key);
    for (const dependency of dependencies.get(key) ?? []) {
      if (dependencies.has(dependency) && hasCycle(dependency)) return true;
    }
    visiting.delete(key);
    visited.add(key);
    return false;
  }
  if (problems.length === 0 && keys.some(hasCycle)) problems.push('Stage dependencies contain a cycle.');
  if (problems.length) throw AppError.badRequest('invalid_workflow', problems.join(' '));
  return dependencies;
}

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

const clean = (value) => (typeof value === 'string' && value.trim() ? value.trim() : null);
const money = (value) => Math.round((value + Number.EPSILON) * 100) / 100;
const toMoney = (value) => Number(value).toFixed(2);

async function transaction(pool, callback) {
  const client = await pool.connect();
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

async function ensureLocation(db, businessId, locationId, allowed) {
  if (allowed !== null && !allowed.includes(locationId)) {
    throw AppError.forbidden('location_forbidden', 'You do not have access to that location.');
  }
  const { rows } = await db.query(`
    SELECT 1 FROM "Locations" WHERE "BusinessId" = $1 AND "Id" = $2 AND "IsActive"`,
  [businessId, locationId]);
  if (!rows.length) throw AppError.badRequest('unknown_location', 'Location not found or inactive.');
}

async function auditOrder(db, req, action, orderId, details = null) {
  await db.query(`
    INSERT INTO "AuditLogs" ("Id", "Action", "ActorUserId", "BusinessId", "CreatedAtUtc", "DetailsJson", "EntityId", "EntityType")
    VALUES ($1, $2, $3, $4, now(), $5::jsonb, $6, 'Order')`,
  [randomUUID(), action, req.currentUser.userId, req.currentUser.businessId, details ? JSON.stringify(details) : null, orderId]);
}

async function generateProductionTasks(db, businessId, orderId, actorUserId, workflowOverrides = {}) {
  const { rows: orderRows } = await db.query(`
    SELECT o."Status", o."PromisedDate", o."Priority", b."TimeZoneId"
    FROM "Orders" o JOIN "Businesses" b ON b."Id" = o."BusinessId"
    WHERE o."BusinessId" = $1 AND o."Id" = $2`,
  [businessId, orderId]);
  if (!orderRows.length) throw AppError.notFound('order_not_found', 'Order not found.');
  const order = orderRows[0];
  if (!['Confirmed', 'InProduction'].includes(order.Status)) {
    throw AppError.conflict('order_not_active', 'Tasks can only be generated for confirmed orders in production.');
  }

  const { rows: garments } = await db.query(`
    SELECT g."Id", g."Name", g."Quantity", g."Category"
    FROM "OrderGarments" g
    WHERE g."BusinessId" = $1 AND g."OrderId" = $2 ORDER BY g."LineNumber"`,
  [businessId, orderId]);
  const { rows: plannedRows } = await db.query(`
    SELECT DISTINCT "OrderGarmentId" FROM "ProductionTasks"
    WHERE "BusinessId" = $1 AND "OrderId" = $2`,
  [businessId, orderId]);
  const alreadyPlanned = new Set(plannedRows.map((row) => row.OrderGarmentId));
  const { rows: workflows } = await db.query(`
    SELECT "Id", "Name", "IsDefault", "GarmentCategories" FROM "Workflows"
    WHERE "BusinessId" = $1 AND "IsActive" ORDER BY "IsDefault" DESC, "Name"`,
  [businessId]);
  const workflowIds = workflows.map((workflow) => workflow.Id);
  const { rows: stages } = workflowIds.length ? await db.query(`
    SELECT "Id", "WorkflowId", "Name", "SortOrder", "Instructions", "RequiredSkill", "DefaultMinutes",
      "IsConditional", "CompletionChecks"
    FROM "Stages" WHERE "BusinessId" = $1 AND "WorkflowId" = ANY($2::uuid[])
    ORDER BY "WorkflowId", "SortOrder"`,
  [businessId, workflowIds]) : { rows: [] };
  const { rows: stageDependencies } = workflowIds.length ? await db.query(`
    SELECT d."StageId", d."DependsOnStageId"
    FROM "StageDependencies" d
    WHERE d."BusinessId" = $1 AND d."StageId" = ANY($2::uuid[])`,
  [businessId, stages.map((stage) => stage.Id)]) : { rows: [] };

  const stagesByWorkflow = new Map();
  for (const stage of stages) {
    const workflowStages = stagesByWorkflow.get(stage.WorkflowId) ?? [];
    workflowStages.push(stage);
    stagesByWorkflow.set(stage.WorkflowId, workflowStages);
  }
  const defaultWorkflow = workflows.find((workflow) => workflow.IsDefault);
  const deadlineResult = await db.query(`
    SELECT (($1::date + time '23:59') AT TIME ZONE $2) AS "DeadlineUtc"`,
  [order.PromisedDate, order.TimeZoneId]);
  const deadlineUtc = deadlineResult.rows[0].DeadlineUtc;

  let tasksCreated = 0;
  let garmentsWithoutWorkflow = 0;
  let garmentsAlreadyPlanned = 0;
  for (const garment of garments) {
    if (alreadyPlanned.has(garment.Id)) {
      garmentsAlreadyPlanned++;
      continue;
    }

    const requestedWorkflowId = workflowOverrides[garment.Id];
    const workflow = requestedWorkflowId
      ? workflows.find((candidate) => candidate.Id === requestedWorkflowId)
      : workflows.find((candidate) => garment.Category
        && (candidate.GarmentCategories ?? []).some((category) => category.toLowerCase() === garment.Category.toLowerCase()))
        ?? defaultWorkflow;
    if (requestedWorkflowId && !workflow) {
      throw AppError.badRequest('unknown_workflow', 'A chosen workflow does not exist or is inactive.');
    }
    if (!workflow) {
      garmentsWithoutWorkflow++;
      continue;
    }

    const workflowStages = stagesByWorkflow.get(workflow.Id) ?? [];
    const taskIdsByStage = new Map();
    for (const stage of workflowStages) {
      const taskId = randomUUID();
      taskIdsByStage.set(stage.Id, taskId);
      const conditional = stage.IsConditional;
      await db.query(`
        INSERT INTO "ProductionTasks" (
          "Id", "BusinessId", "OrderId", "OrderGarmentId", "StageId", "StageName", "StageOrder",
          "Title", "Instructions", "Quantity", "RequiredSkill", "EstimatedMinutes", "DeadlineUtc",
          "Priority", "IsConditional", "Status", "CancelReason", "RequiredMaterialsJson", "RequiredChecks", "CompletedChecks",
          "CreatedByUserId", "CreatedAtUtc"
        ) VALUES (
          $1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, $16, $17, '[]'::jsonb, $18::text[], '{}'::text[], $19, now()
        )`,
      [taskId, businessId, orderId, garment.Id, stage.Id, stage.Name, stage.SortOrder,
        `${stage.Name} - ${garment.Name}`, stage.Instructions, garment.Quantity, stage.RequiredSkill,
        stage.DefaultMinutes === null ? null : stage.DefaultMinutes * garment.Quantity, deadlineUtc,
        order.Priority, conditional, conditional ? 'Cancelled' : 'Pending',
        conditional ? 'Not required (conditional step)' : null, stage.CompletionChecks ?? [], actorUserId]);
      await db.query(`
        INSERT INTO "TaskEvents" ("Id", "BusinessId", "TaskId", "Type", "FromStatus", "ToStatus", "ActorUserId", "DataJson", "CreatedAtUtc")
        VALUES ($1, $2, $3, 'generated', NULL, $4, $5, $6::jsonb, now())`,
      [randomUUID(), businessId, taskId, conditional ? 'Cancelled' : 'Pending', actorUserId,
        JSON.stringify({ workflow: workflow.Name })]);
      tasksCreated++;
    }

    for (const dependency of stageDependencies) {
      const taskId = taskIdsByStage.get(dependency.StageId);
      const dependsOnTaskId = taskIdsByStage.get(dependency.DependsOnStageId);
      if (!taskId || !dependsOnTaskId) continue;
      await db.query(`
        INSERT INTO "TaskDependencies" ("TaskId", "DependsOnTaskId", "BusinessId")
        VALUES ($1, $2, $3)`,
      [taskId, dependsOnTaskId, businessId]);
    }
  }
  return { tasksCreated, garmentsWithoutWorkflow, garmentsAlreadyPlanned };
}

async function loadWorkflow(db, businessId, id) {
  const { rows: workflowRows } = await db.query(`
    SELECT "Id", "Name", "Description", "IsDefault", "IsActive", "GarmentCategories"
    FROM "Workflows" WHERE "BusinessId" = $1 AND "Id" = $2`,
  [businessId, id]);
  if (!workflowRows.length) throw AppError.notFound('workflow_not_found', 'Workflow not found.');
  const workflow = workflowRows[0];
  const { rows: stages } = await db.query(`
    SELECT "Id", "Key", "Name", "SortOrder", "Instructions", "RequiredSkill", "DefaultMinutes",
      "IsConditional", "CompletionChecks"
    FROM "Stages" WHERE "BusinessId" = $1 AND "WorkflowId" = $2 ORDER BY "SortOrder"`,
  [businessId, id]);
  const stageIds = stages.map((stage) => stage.Id);
  const { rows: dependencyRows } = stageIds.length ? await db.query(`
    SELECT d."StageId", d."DependsOnStageId", prerequisite."Key" AS "DependsOnKey"
    FROM "StageDependencies" d
    JOIN "Stages" prerequisite ON prerequisite."BusinessId" = d."BusinessId" AND prerequisite."Id" = d."DependsOnStageId"
    WHERE d."BusinessId" = $1 AND d."StageId" = ANY($2::uuid[])`,
  [businessId, stageIds]) : { rows: [] };
  return {
    id: workflow.Id,
    name: workflow.Name,
    description: workflow.Description,
    isDefault: workflow.IsDefault,
    isActive: workflow.IsActive,
    garmentCategories: workflow.GarmentCategories ?? [],
    stages: stages.map((stage) => ({
      id: stage.Id,
      key: stage.Key,
      name: stage.Name,
      sortOrder: stage.SortOrder,
      instructions: stage.Instructions,
      requiredSkill: stage.RequiredSkill,
      defaultMinutes: stage.DefaultMinutes,
      isConditional: stage.IsConditional,
      completionChecks: stage.CompletionChecks ?? [],
      dependsOn: dependencyRows.filter((dependency) => dependency.StageId === stage.Id)
        .map((dependency) => dependency.DependsOnKey).sort(),
    })),
  };
}

async function auditWorkflow(db, req, action, workflowId, name) {
  await db.query(`
    INSERT INTO "AuditLogs" ("Id", "Action", "ActorUserId", "BusinessId", "CreatedAtUtc", "DetailsJson", "EntityId", "EntityType")
    VALUES ($1, $2, $3, $4, now(), $5::jsonb, $6, 'ProductionWorkflow')`,
  [randomUUID(), action, req.currentUser.userId, req.currentUser.businessId,
    JSON.stringify({ name }), workflowId]);
}

function taskSummary(row, now = new Date()) {
  const statusOpen = !['Completed', 'Cancelled'].includes(row.Status);
  const eligible = ['Pending', 'Scheduled'].includes(row.Status)
    && Number(row.UnmetDependencies) === 0
    && ['Confirmed', 'InProduction'].includes(row.OrderStatus);
  return {
    id: row.Id,
    orderId: row.OrderId,
    orderNumber: row.OrderNumber,
    orderGarmentId: row.OrderGarmentId,
    garmentName: row.GarmentName,
    stageName: row.StageName,
    stageOrder: row.StageOrder,
    title: row.Title,
    quantity: row.Quantity,
    status: row.Status,
    priority: row.Priority,
    deadlineUtc: row.DeadlineUtc,
    isOverdue: statusOpen && row.DeadlineUtc !== null && row.DeadlineUtc !== undefined
      && new Date(row.DeadlineUtc) < now,
    assigneeMembershipId: row.AssignedMembershipId,
    assigneeName: row.AssigneeName,
    requiredSkill: row.RequiredSkill,
    estimatedMinutes: row.EstimatedMinutes,
    needsEstimate: row.EstimatedMinutes === null && statusOpen,
    unmetDependencies: Number(row.UnmetDependencies),
    isEligible: eligible,
    isConditional: row.IsConditional,
    blockedType: row.BlockedType,
    blockedReason: row.BlockedReason,
    version: Number(row.Version),
  };
}

const taskSummaryColumns = `
  t."Id", t."OrderId", o."OrderNumber", o."Status" AS "OrderStatus",
  t."OrderGarmentId", g."Name" AS "GarmentName", t."StageName", t."StageOrder",
  t."Title", t."Quantity", t."Status", t."Priority", t."DeadlineUtc",
  t."AssignedMembershipId", m."DisplayName" AS "AssigneeName", t."RequiredSkill",
  t."EstimatedMinutes", t."IsConditional", t."BlockedType", t."BlockedReason",
  t.xmin::text::bigint AS "Version",
  (SELECT count(*) FROM "TaskDependencies" d
    JOIN "ProductionTasks" prerequisite ON prerequisite."BusinessId" = d."BusinessId" AND prerequisite."Id" = d."DependsOnTaskId"
    WHERE d."BusinessId" = t."BusinessId" AND d."TaskId" = t."Id"
      AND prerequisite."Status" NOT IN ('Completed', 'Cancelled')) AS "UnmetDependencies"`;

async function loadTaskSummary(db, businessId, taskId, allowed) {
  const { rows } = await db.query(`
    SELECT ${taskSummaryColumns}
    FROM "ProductionTasks" t
    JOIN "Orders" o ON o."BusinessId" = t."BusinessId" AND o."Id" = t."OrderId"
    JOIN "OrderGarments" g ON g."BusinessId" = t."BusinessId" AND g."Id" = t."OrderGarmentId"
    LEFT JOIN "Memberships" m ON m."BusinessId" = t."BusinessId" AND m."Id" = t."AssignedMembershipId"
    WHERE t."BusinessId" = $1 AND t."Id" = $2
      AND ($3::uuid[] IS NULL OR o."LocationId" = ANY($3::uuid[]))`,
  [businessId, taskId, allowed]);
  if (!rows.length) throw AppError.notFound('task_not_found', 'Task not found.');
  return rows[0];
}

async function loadTaskDetail(db, businessId, taskId, allowed, includeCustomer = true) {
  const row = await loadTaskSummary(db, businessId, taskId, allowed);
  const { rows: taskRows } = await db.query(`
    SELECT t."Instructions", t."RequiredChecks", t."CompletedChecks", t."RequiredMaterialsJson",
      t."CompletionNotes", t."PlannedStartUtc", t."PlannedEndUtc", t."ActualStartUtc", t."CompletedAtUtc",
      g."Name" AS "GarmentName", g."Category", g."Quantity" AS "GarmentQuantity", g."Fabric", g."Colour",
      g."StyleInstructions", g."AlterationNotes", g."MeasurementSnapshotJson",
      ${includeCustomer ? 'c."FullName" AS "CustomerName"' : 'NULL::text AS "CustomerName"'}
    FROM "ProductionTasks" t
    JOIN "OrderGarments" g ON g."BusinessId" = t."BusinessId" AND g."Id" = t."OrderGarmentId"
    JOIN "Orders" o ON o."BusinessId" = t."BusinessId" AND o."Id" = t."OrderId"
    ${includeCustomer ? 'JOIN "Customers" c ON c."BusinessId" = o."BusinessId" AND c."Id" = o."CustomerId"' : ''}
    WHERE t."BusinessId" = $1 AND t."Id" = $2 AND ($3::uuid[] IS NULL OR o."LocationId" = ANY($3::uuid[]))`,
  [businessId, taskId, allowed]);
  if (!taskRows.length) throw AppError.notFound('task_not_found', 'Task not found.');
  const task = taskRows[0];
  const [dependencies, events] = await Promise.all([
    db.query(`
      SELECT prerequisite."Id", prerequisite."Title", prerequisite."Status"
      FROM "TaskDependencies" d JOIN "ProductionTasks" prerequisite
        ON prerequisite."BusinessId" = d."BusinessId" AND prerequisite."Id" = d."DependsOnTaskId"
      WHERE d."BusinessId" = $1 AND d."TaskId" = $2 ORDER BY prerequisite."StageOrder"`,
    [businessId, taskId]),
    db.query(`
      SELECT "Type", "FromStatus", "ToStatus", "ActorUserId", "Note", "CreatedAtUtc"
      FROM "TaskEvents" WHERE "BusinessId" = $1 AND "TaskId" = $2
      ORDER BY "CreatedAtUtc" DESC LIMIT 100`,
    [businessId, taskId]),
  ]);
  const requiredMaterials = typeof task.RequiredMaterialsJson === 'string'
    ? JSON.parse(task.RequiredMaterialsJson) : task.RequiredMaterialsJson;
  return {
    task: taskSummary(row),
    instructions: task.Instructions,
    requiredChecks: task.RequiredChecks ?? [],
    completedChecks: task.CompletedChecks ?? [],
    requiredMaterials: requiredMaterials ?? [],
    completionNotes: task.CompletionNotes,
    plannedStartUtc: task.PlannedStartUtc,
    plannedEndUtc: task.PlannedEndUtc,
    actualStartUtc: task.ActualStartUtc,
    completedAtUtc: task.CompletedAtUtc,
    dependsOn: dependencies.rows.map((dependency) => ({
      taskId: dependency.Id, title: dependency.Title, status: dependency.Status,
    })),
    garment: {
      name: task.GarmentName,
      category: task.Category,
      quantity: task.GarmentQuantity,
      fabric: task.Fabric,
      colour: task.Colour,
      styleInstructions: task.StyleInstructions,
      alterationNotes: task.AlterationNotes,
      measurementSnapshot: typeof task.MeasurementSnapshotJson === 'string'
        ? JSON.parse(task.MeasurementSnapshotJson) : task.MeasurementSnapshotJson,
    },
    customerName: task.CustomerName,
    events: events.rows.map((event) => ({
      type: event.Type, fromStatus: event.FromStatus, toStatus: event.ToStatus,
      actorUserId: event.ActorUserId, note: event.Note, atUtc: event.CreatedAtUtc,
    })),
  };
}

async function recordTaskEvent(db, businessId, taskId, type, fromStatus, toStatus, actorUserId, note = null, data = null) {
  await db.query(`
    INSERT INTO "TaskEvents" ("Id", "BusinessId", "TaskId", "Type", "FromStatus", "ToStatus",
      "ActorUserId", "Note", "DataJson", "CreatedAtUtc")
    VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9::jsonb, now())`,
  [randomUUID(), businessId, taskId, type, fromStatus, toStatus, actorUserId, clean(note),
    data === null ? null : JSON.stringify(data)]);
}

async function auditTask(db, req, action, taskId, details) {
  await db.query(`
    INSERT INTO "AuditLogs" ("Id", "Action", "ActorUserId", "BusinessId", "CreatedAtUtc",
      "DetailsJson", "EntityId", "EntityType")
    VALUES ($1, $2, $3, $4, now(), $5::jsonb, $6, 'ProductionTask')`,
  [randomUUID(), action, req.currentUser.userId, req.currentUser.businessId,
    JSON.stringify(details), taskId]);
}

async function syncOrderFromTasks(db, businessId, orderId) {
  const { rows: orderRows } = await db.query(`
    SELECT "Status" FROM "Orders" WHERE "BusinessId" = $1 AND "Id" = $2 FOR UPDATE`,
  [businessId, orderId]);
  if (!orderRows.length || !['Confirmed', 'InProduction', 'Ready'].includes(orderRows[0].Status)) return;
  const { rows } = await db.query(`
    SELECT "Status" FROM "ProductionTasks" WHERE "BusinessId" = $1 AND "OrderId" = $2 AND "Status" <> 'Cancelled'`,
  [businessId, orderId]);
  if (!rows.length) return;
  const allDone = rows.every((task) => task.Status === 'Completed');
  const started = rows.some((task) => ['InProgress', 'Paused', 'Completed'].includes(task.Status));
  if (allDone && orderRows[0].Status !== 'Ready') {
    await db.query(`
      UPDATE "Orders" SET "Status" = 'Ready', "ReadyAtUtc" = now(), "UpdatedAtUtc" = now()
      WHERE "BusinessId" = $1 AND "Id" = $2`,
    [businessId, orderId]);
  } else if (!allDone && orderRows[0].Status === 'Ready') {
    await db.query(`
      UPDATE "Orders" SET "Status" = 'InProduction', "ReadyAtUtc" = NULL, "UpdatedAtUtc" = now()
      WHERE "BusinessId" = $1 AND "Id" = $2`,
    [businessId, orderId]);
  } else if (!allDone && started && orderRows[0].Status === 'Confirmed') {
    await db.query(`
      UPDATE "Orders" SET "Status" = 'InProduction', "UpdatedAtUtc" = now()
      WHERE "BusinessId" = $1 AND "Id" = $2`,
    [businessId, orderId]);
  }
}

async function createProductionTask(db, req, orderId, payload, allowed) {
  const businessId = req.currentUser.businessId;
  const { rows: orderRows } = await db.query(`
    SELECT o."Id", o."Status", o."PromisedDate", o."Priority", o."LocationId", b."TimeZoneId"
    FROM "Orders" o JOIN "Businesses" b ON b."Id" = o."BusinessId"
    WHERE o."BusinessId" = $1 AND o."Id" = $2
      AND ($3::uuid[] IS NULL OR o."LocationId" = ANY($3::uuid[]))
    FOR UPDATE OF o`,
  [businessId, orderId, allowed]);
  if (!orderRows.length) throw AppError.notFound('order_not_found', 'Order not found.');
  const order = orderRows[0];
  if (!['Confirmed', 'InProduction', 'Ready'].includes(order.Status)) {
    throw AppError.conflict('order_not_active', 'Tasks can only be added to confirmed orders that are not yet delivered.');
  }

  const { rows: garments } = await db.query(`
    SELECT "Id", "Quantity" FROM "OrderGarments"
    WHERE "BusinessId" = $1 AND "OrderId" = $2 ORDER BY "LineNumber"`,
  [businessId, orderId]);
  const garmentId = payload.orderGarmentId ?? (garments.length === 1 ? garments[0].Id : null);
  if (!garmentId) throw AppError.badRequest('garment_required', 'Choose which garment this task is for.');
  const garment = garments.find((row) => row.Id === garmentId);
  if (!garment) throw AppError.badRequest('unknown_garment', 'That garment does not belong to this order.');

  const dependencyIds = [...new Set(payload.dependsOnTaskIds)];
  if (dependencyIds.length) {
    const { rows: dependencies } = await db.query(`
      SELECT "Id" FROM "ProductionTasks"
      WHERE "BusinessId" = $1 AND "OrderId" = $2 AND "Id" = ANY($3::uuid[])`,
    [businessId, orderId, dependencyIds]);
    if (dependencies.length !== dependencyIds.length) {
      throw AppError.badRequest('unknown_dependency', 'Every prerequisite must be a task of the same order.');
    }
  }
  const { rows: deadlineRows } = await db.query(`
    SELECT (($1::date + time '23:59') AT TIME ZONE $2) AS "DeadlineUtc"`,
  [order.PromisedDate, order.TimeZoneId]);
  const taskId = randomUUID();
  const priority = payload.priority ?? order.Priority;
  const deadline = payload.deadline ?? deadlineRows[0].DeadlineUtc;
  const requiredSkill = clean(payload.requiredSkill)?.toLowerCase() ?? null;
  const requiredMaterials = payload.requiredMaterials.map((material) => ({
    name: material.name.trim(), quantity: material.quantity, unit: material.unit.trim(),
  }));
  await db.query(`
    INSERT INTO "ProductionTasks" (
      "Id", "BusinessId", "OrderId", "OrderGarmentId", "StageName", "StageOrder", "Title", "Instructions",
      "Quantity", "RequiredSkill", "EstimatedMinutes", "DeadlineUtc", "Priority", "Status", "IsConditional",
      "RequiredChecks", "CompletedChecks", "RequiredMaterialsJson", "CreatedByUserId", "CreatedAtUtc"
    ) VALUES ($1, $2, $3, $4, $5, 1000, $6, $7, $8, $9, $10, $11, $12, 'Pending', FALSE,
      $13::text[], '{}'::text[], $14::jsonb, $15, now())`,
  [taskId, businessId, orderId, garmentId, clean(payload.stageName) ?? 'Additional work',
    payload.title, clean(payload.instructions), garment.Quantity, requiredSkill, payload.estimatedMinutes ?? null,
    deadline, priority, payload.completionChecks, JSON.stringify(requiredMaterials), req.currentUser.userId]);
  for (const dependencyId of dependencyIds) {
    await db.query(`
      INSERT INTO "TaskDependencies" ("TaskId", "DependsOnTaskId", "BusinessId")
      VALUES ($1, $2, $3)`,
    [taskId, dependencyId, businessId]);
  }
  await recordTaskEvent(db, businessId, taskId, 'created', null, 'Pending', req.currentUser.userId);
  await auditTask(db, req, 'task.created', taskId, { orderId, title: payload.title });
  await syncOrderFromTasks(db, businessId, orderId);
  return taskId;
}

async function updateProductionTask(db, req, taskId, payload, allowed) {
  const businessId = req.currentUser.businessId;
  const { rows } = await db.query(`
    SELECT t."OrderId", t."Status", t."Instructions", t."Priority", t."DeadlineUtc", t."EstimatedMinutes",
      t."RequiredSkill", t."RequiredMaterialsJson"
    FROM "ProductionTasks" t JOIN "Orders" o ON o."BusinessId" = t."BusinessId" AND o."Id" = t."OrderId"
    WHERE t."BusinessId" = $1 AND t."Id" = $2
      AND ($3::uuid[] IS NULL OR o."LocationId" = ANY($3::uuid[]))
    FOR UPDATE OF t`,
  [businessId, taskId, allowed]);
  if (!rows.length) throw AppError.notFound('task_not_found', 'Task not found.');
  const task = rows[0];
  if (['Completed', 'Cancelled'].includes(task.Status)) {
    throw AppError.conflict('task_closed', `This task is ${task.Status} and can no longer be changed.`);
  }

  const updates = [];
  const values = [businessId, taskId, payload.version, req.currentUser.userId];
  const set = (column, value) => {
    values.push(value);
    updates.push(`"${column}" = $${values.length}`);
  };
  const changed = [];
  if (payload.instructions !== undefined && payload.instructions !== null && payload.instructions !== task.Instructions) {
    set('Instructions', clean(payload.instructions));
    changed.push('instructions');
  }
  if (payload.priority !== undefined && payload.priority !== task.Priority) {
    set('Priority', payload.priority);
    changed.push('priority');
  }
  if (payload.deadline !== undefined && payload.deadline !== null
      && new Date(payload.deadline).getTime() !== new Date(task.DeadlineUtc).getTime()) {
    set('DeadlineUtc', payload.deadline);
    changed.push('deadline');
  }
  if (payload.estimatedMinutes !== undefined && payload.estimatedMinutes !== null
      && payload.estimatedMinutes !== task.EstimatedMinutes) {
    set('EstimatedMinutes', payload.estimatedMinutes);
    changed.push('estimate');
  }
  if (payload.requiredSkill !== undefined && payload.requiredSkill !== null) {
    const skill = clean(payload.requiredSkill)?.toLowerCase() ?? null;
    if (skill !== task.RequiredSkill) {
      set('RequiredSkill', skill);
      changed.push('skill');
    }
  }
  if (payload.requiredMaterials !== undefined) {
    const materials = payload.requiredMaterials.map((material) => ({
      name: material.name.trim(), quantity: material.quantity, unit: material.unit.trim(),
    }));
    const current = typeof task.RequiredMaterialsJson === 'string'
      ? JSON.parse(task.RequiredMaterialsJson) : task.RequiredMaterialsJson;
    if (JSON.stringify(materials) !== JSON.stringify(current)) {
      values.push(JSON.stringify(materials));
      updates.push(`"RequiredMaterialsJson" = $${values.length}::jsonb`);
      changed.push('materials');
    }
  }
  updates.push('"UpdatedByUserId" = $4', '"UpdatedAtUtc" = now()');
  const { rowCount } = await db.query(`
    UPDATE "ProductionTasks" SET ${updates.join(', ')}
    WHERE "BusinessId" = $1 AND "Id" = $2 AND xmin = $3::xid`,
  values);
  if (!rowCount) throw AppError.conflict('stale_version', 'This task was changed by someone else. Reload and try again.');
  await recordTaskEvent(db, businessId, taskId, 'updated', task.Status, task.Status,
    req.currentUser.userId, null, { changed });
  await auditTask(db, req, 'task.updated', taskId, { changed });
  await syncOrderFromTasks(db, businessId, task.OrderId);
  return task.OrderId;
}

async function assignProductionTask(db, req, taskId, payload, allowed) {
  const businessId = req.currentUser.businessId;
  const { rows: taskRows } = await db.query(`
    SELECT t."OrderId", t."Status", t."AssignedMembershipId", t."RequiredSkill",
      t."Title", o."LocationId"
    FROM "ProductionTasks" t
    JOIN "Orders" o ON o."BusinessId" = t."BusinessId" AND o."Id" = t."OrderId"
    WHERE t."BusinessId" = $1 AND t."Id" = $2
      AND ($3::uuid[] IS NULL OR o."LocationId" = ANY($3::uuid[]))
    FOR UPDATE OF t`,
  [businessId, taskId, allowed]);
  if (!taskRows.length) throw AppError.notFound('task_not_found', 'Task not found.');
  const task = taskRows[0];
  if (['Completed', 'Cancelled', 'InProgress'].includes(task.Status)) {
    throw AppError.conflict('task_not_assignable', `A ${task.Status} task cannot be reassigned. Pause it first if it is in progress.`);
  }
  const membershipId = payload.membershipId ?? null;
  if (membershipId === task.AssignedMembershipId) {
    return;
  }

  let member = null;
  const problems = [];
  if (membershipId) {
    const { rows: members } = await db.query(`
      SELECT m."Id", m."DisplayName"
      FROM "Memberships" m
      WHERE m."BusinessId" = $1 AND m."Id" = $2 AND m."Status" = 0
        AND ($3::uuid[] IS NULL OR EXISTS (
          SELECT 1 FROM "MembershipLocations" ml
          WHERE ml."BusinessId" = m."BusinessId" AND ml."MembershipId" = m."Id"
            AND ml."LocationId" = ANY($3::uuid[])
        ))`,
    [businessId, membershipId, allowed]);
    if (!members.length) {
      throw AppError.badRequest('unknown_worker', 'That person is not an active member in your permitted workshop locations.');
    }
    member = members[0];
    if (task.RequiredSkill) {
      const { rows: skills } = await db.query(`
        SELECT 1 FROM "WorkerSkills" WHERE "BusinessId" = $1 AND "MembershipId" = $2 AND "Skill" = $3`,
      [businessId, membershipId, task.RequiredSkill]);
      if (!skills.length) problems.push(`${member.DisplayName} does not have the '${task.RequiredSkill}' skill.`);
    }
    const { rows: locationRows } = await db.query(`
      SELECT 1 FROM "MembershipLocations"
      WHERE "BusinessId" = $1 AND "MembershipId" = $2 AND "LocationId" = $3`,
    [businessId, membershipId, task.LocationId]);
    if (!locationRows.length) problems.push(`${member.DisplayName} is not assigned to this order's workshop.`);
  }

  let overridden = false;
  if (problems.length) {
    if (!payload.override) {
      throw AppError.conflict('assignment_not_allowed',
        `${problems.join(' ')} An authorised manager can override this with a reason.`,
        { problems });
    }
    if (!req.currentUser.isOwner && !req.currentUser.permissions.includes('tasks.override')) {
      throw AppError.forbidden('override_not_permitted', 'You do not have permission to override workflow rules.');
    }
    if (!payload.reason || payload.reason.trim().length < 5) {
      throw AppError.badRequest('reason_required', 'Give a reason (at least 5 characters) for the override.');
    }
    overridden = true;
  }

  await db.query(`
    UPDATE "ProductionTasks" SET "AssignedMembershipId" = $1, "UpdatedByUserId" = $2, "UpdatedAtUtc" = now()
    WHERE "BusinessId" = $3 AND "Id" = $4`,
  [membershipId, req.currentUser.userId, businessId, taskId]);
  await recordTaskEvent(db, businessId, taskId, overridden ? 'override.assign' : membershipId ? 'assigned' : 'unassigned',
    task.Status, task.Status, req.currentUser.userId, payload.reason ?? null,
    { before: task.AssignedMembershipId, after: membershipId, problems });
  if (overridden) {
    await auditTask(db, req, 'task.override_assign', taskId, { reason: payload.reason, problems });
  }
  await syncOrderFromTasks(db, businessId, task.OrderId);
}

async function transitionProductionTask(db, req, taskId, action, payload, allowed) {
  const businessId = req.currentUser.businessId;
  const { rows } = await db.query(`
    SELECT t."OrderId", t."Status", t."StatusBeforeBlock", t."ActualStartUtc",
      t."BlockedType", t."AssignedMembershipId", o."Status" AS "OrderStatus"
    FROM "ProductionTasks" t
    JOIN "Orders" o ON o."BusinessId" = t."BusinessId" AND o."Id" = t."OrderId"
    WHERE t."BusinessId" = $1 AND t."Id" = $2
      AND ($3::uuid[] IS NULL OR o."LocationId" = ANY($3::uuid[]))
    FOR UPDATE OF t`,
  [businessId, taskId, allowed]);
  if (!rows.length) throw AppError.notFound('task_not_found', 'Task not found.');
  const task = rows[0];
  const actorId = req.currentUser.userId;

  if (action === 'unblock') {
    if (task.Status !== 'Blocked') throw AppError.conflict('invalid_transition', 'Only blocked tasks can be unblocked.');
    const back = task.StatusBeforeBlock === 'InProgress' || task.ActualStartUtc ? 'Paused' : 'Pending';
    await db.query(`
      UPDATE "ProductionTasks" SET "Status" = $1, "StatusBeforeBlock" = NULL, "BlockedType" = NULL,
        "BlockedReason" = NULL, "UpdatedByUserId" = $2, "UpdatedAtUtc" = now()
      WHERE "BusinessId" = $3 AND "Id" = $4`,
    [back, actorId, businessId, taskId]);
    await recordTaskEvent(db, businessId, taskId, 'unblocked', 'Blocked', back, actorId,
      payload.note ?? null, { issue: task.BlockedType });
  } else if (action === 'cancel') {
    if (['Completed', 'Cancelled'].includes(task.Status)) {
      throw AppError.conflict('task_closed', `This task is ${task.Status} and can no longer be changed.`);
    }
    await db.query(`
      UPDATE "ProductionTasks" SET "Status" = 'Cancelled', "CancelReason" = $1,
        "UpdatedByUserId" = $2, "UpdatedAtUtc" = now()
      WHERE "BusinessId" = $3 AND "Id" = $4`,
    [payload.reason, actorId, businessId, taskId]);
    await recordTaskEvent(db, businessId, taskId, 'cancelled', task.Status, 'Cancelled', actorId, payload.reason);
    await auditTask(db, req, 'task.cancelled', taskId, { reason: payload.reason });
  } else {
    if (['Completed', 'Cancelled'].includes(task.Status)) {
      throw AppError.conflict('task_closed', `This task is ${task.Status} and can no longer be changed.`);
    }
    if (!task.AssignedMembershipId) {
      throw AppError.conflict('assignee_required', 'Assign the task to someone first so the work is attributed correctly.');
    }
    if (!['Confirmed', 'InProduction', 'Ready'].includes(task.OrderStatus)) {
      throw AppError.conflict('order_not_active', 'This order is no longer in production.');
    }
    const nextStatus = payload.action === 'Start' ? 'InProgress' : 'Completed';
    const eventType = payload.action === 'Start' ? 'override.start' : 'override.complete';
    if (payload.action === 'Start') {
      await db.query(`
        UPDATE "ProductionTasks" SET "Status" = 'InProgress', "ActualStartUtc" = COALESCE("ActualStartUtc", now()),
          "StatusBeforeBlock" = NULL, "BlockedType" = NULL, "BlockedReason" = NULL,
          "UpdatedByUserId" = $1, "UpdatedAtUtc" = now()
        WHERE "BusinessId" = $2 AND "Id" = $3`,
      [actorId, businessId, taskId]);
    } else {
      await db.query(`
        UPDATE "ProductionTasks" SET "Status" = 'Completed', "CompletedAtUtc" = now(),
          "CompletionNotes" = $1, "UpdatedByUserId" = $2, "UpdatedAtUtc" = now()
        WHERE "BusinessId" = $3 AND "Id" = $4`,
      [`Completed by manager override: ${payload.reason}`, actorId, businessId, taskId]);
    }
    await recordTaskEvent(db, businessId, taskId, eventType, task.Status, nextStatus, actorId, payload.reason);
    await auditTask(db, req, `task.${eventType}`, taskId, { reason: payload.reason, previousStatus: task.Status });
  }
  await syncOrderFromTasks(db, businessId, task.OrderId);
}

async function workerTaskAction(db, req, taskId, action, payload = {}) {
  const businessId = req.currentUser.businessId;
  const { rows: memberships } = await db.query(`
    SELECT "Id" FROM "Memberships"
    WHERE "BusinessId" = $1 AND "UserId" = $2 AND "Status" = 0
    FOR UPDATE`,
  [businessId, req.currentUser.userId]);
  if (!memberships.length) throw AppError.forbidden('no_active_membership', 'Your account is not an active member of this business.');
  const membershipId = memberships[0].Id;
  const { rows } = await db.query(`
    SELECT t."Id", t."OrderId", t."Status", t."ActualStartUtc", o."Status" AS "OrderStatus"
    FROM "ProductionTasks" t
    JOIN "Orders" o ON o."BusinessId" = t."BusinessId" AND o."Id" = t."OrderId"
    WHERE t."BusinessId" = $1 AND t."Id" = $2 AND t."AssignedMembershipId" = $3
    FOR UPDATE OF t`,
  [businessId, taskId, membershipId]);
  if (!rows.length) throw AppError.notFound('task_not_found', 'Task not found.');
  const task = rows[0];

  let nextStatus;
  let eventType;
  let note = null;
  if (action === 'start' || action === 'resume') {
    const expectedStatus = action === 'start' ? ['Pending', 'Scheduled'] : ['Paused'];
    if (!expectedStatus.includes(task.Status)) {
      const message = action === 'start'
        ? `A ${task.Status} task cannot be started.`
        : 'Only a paused task can be resumed.';
      throw AppError.conflict('invalid_transition', message);
    }
    if (!['Confirmed', 'InProduction'].includes(task.OrderStatus)) {
      throw AppError.conflict('order_not_active', 'This order is not in production.');
    }
    const { rows: blockers } = await db.query(`
      SELECT prerequisite."Title"
      FROM "TaskDependencies" d JOIN "ProductionTasks" prerequisite
        ON prerequisite."BusinessId" = d."BusinessId" AND prerequisite."Id" = d."DependsOnTaskId"
      WHERE d."BusinessId" = $1 AND d."TaskId" = $2
        AND prerequisite."Status" NOT IN ('Completed', 'Cancelled')
      ORDER BY prerequisite."StageOrder"`,
    [businessId, taskId]);
    if (action === 'start' && blockers.length) {
      const titles = blockers.map((blocker) => blocker.Title);
      throw AppError.conflict('dependencies_incomplete', `Waiting for: ${titles.join(', ')}.`, { blockedBy: titles });
    }
    const { rows: activeTasks } = await db.query(`
      SELECT 1 FROM "ProductionTasks"
      WHERE "BusinessId" = $1 AND "AssignedMembershipId" = $2 AND "Status" = 'InProgress' AND "Id" <> $3
      LIMIT 1`,
    [businessId, membershipId, taskId]);
    if (activeTasks.length) throw AppError.conflict('task_in_progress', 'Pause or finish the task you are working on first.');
    nextStatus = 'InProgress';
    eventType = action === 'start' ? 'started' : 'resumed';
  } else {
    if (task.Status !== 'InProgress') {
      throw AppError.conflict('invalid_transition', 'Only a task in progress can be paused.');
    }
    nextStatus = 'Paused';
    eventType = 'paused';
    note = clean(payload.note);
  }

  await db.query(`
    UPDATE "ProductionTasks" SET "Status" = $1,
      "ActualStartUtc" = CASE WHEN $1 = 'InProgress' THEN COALESCE("ActualStartUtc", now()) ELSE "ActualStartUtc" END,
      "UpdatedByUserId" = $2, "UpdatedAtUtc" = now()
    WHERE "BusinessId" = $3 AND "Id" = $4`,
  [nextStatus, req.currentUser.userId, businessId, taskId]);
  await recordTaskEvent(db, businessId, taskId, eventType, task.Status, nextStatus,
    req.currentUser.userId, note);
  await syncOrderFromTasks(db, businessId, task.OrderId);
}

async function workerTaskCompletion(db, req, taskId, payload) {
  const businessId = req.currentUser.businessId;
  const { rows: membershipRows } = await db.query(`
    SELECT "Id" FROM "Memberships"
    WHERE "BusinessId" = $1 AND "UserId" = $2 AND "Status" = 0`,
  [businessId, req.currentUser.userId]);
  if (!membershipRows.length) throw AppError.forbidden('no_active_membership', 'Your account is not an active member of this business.');
  const { rows: taskRows } = await db.query(`
    SELECT t."Id", t."OrderId", t."OrderGarmentId", t."Status", t."RequiredChecks"
    FROM "ProductionTasks" t
    WHERE t."BusinessId" = $1 AND t."Id" = $2 AND t."AssignedMembershipId" = $3
    FOR UPDATE`,
  [businessId, taskId, membershipRows[0].Id]);
  if (!taskRows.length) throw AppError.notFound('task_not_found', 'Task not found.');
  const task = taskRows[0];
  if (task.Status !== 'InProgress') {
    throw AppError.conflict('invalid_transition', 'Start the task before completing it.');
  }

  const completedChecks = new Set(payload.completedChecks.map((check) => check.trim().toLowerCase()));
  const requiredChecks = task.RequiredChecks ?? [];
  const missing = requiredChecks.filter((check) => !completedChecks.has(check.toLowerCase()));
  if (missing.length) {
    throw AppError.badRequest('checks_incomplete', `Tick every checklist item first. Missing: ${missing.join('; ')}.`);
  }

  let activated = [];
  if (payload.alterationsRequired) {
    const { rows: conditionalDependents } = await db.query(`
      SELECT conditional."Id"
      FROM "TaskDependencies" d
      JOIN "ProductionTasks" conditional ON conditional."BusinessId" = d."BusinessId"
        AND conditional."Id" = d."TaskId" AND conditional."IsConditional"
      WHERE d."BusinessId" = $1 AND d."DependsOnTaskId" = $2`,
    [businessId, taskId]);
    if (!conditionalDependents.length) {
      throw AppError.badRequest('alterations_not_applicable', 'Alterations can only be requested from the step before them (e.g. the fitting).');
    }
    const { rows: conditionalTasks } = await db.query(`
      SELECT "Id" FROM "ProductionTasks"
      WHERE "BusinessId" = $1 AND "OrderGarmentId" = $2 AND "IsConditional"
        AND "Status" = 'Cancelled' AND "CancelReason" = 'Not required (conditional step)'
      FOR UPDATE`,
    [businessId, task.OrderGarmentId]);
    if (!conditionalTasks.length) {
      throw AppError.conflict('no_conditional_steps', 'This garment has no alteration step to activate (it was already requested, or its workflow has none).');
    }
    activated = conditionalTasks.map((row) => row.Id);
    await db.query(`
      UPDATE "ProductionTasks" SET "Status" = 'Pending', "CancelReason" = NULL,
        "UpdatedByUserId" = $1, "UpdatedAtUtc" = now()
      WHERE "BusinessId" = $2 AND "Id" = ANY($3::uuid[])`,
    [req.currentUser.userId, businessId, activated]);
    for (const activatedId of activated) {
      await recordTaskEvent(db, businessId, activatedId, 'alterations_requested',
        'Cancelled', 'Pending', req.currentUser.userId, payload.alterationNotes ?? payload.notes);
    }
  }

  const { rows: dependentRows } = await db.query(`
    SELECT DISTINCT t."Id", t."Status"
    FROM "TaskDependencies" d JOIN "ProductionTasks" t
      ON t."BusinessId" = d."BusinessId" AND t."Id" = d."TaskId"
    WHERE d."BusinessId" = $1 AND d."DependsOnTaskId" = $2
      AND t."Status" IN ('Pending', 'Scheduled')
      AND NOT (t."Id" = ANY($3::uuid[]))`,
  [businessId, taskId, activated]);
  const newlyEligible = [];
  for (const dependent of dependentRows) {
    const { rows: unmet } = await db.query(`
      SELECT 1 FROM "TaskDependencies" d
      JOIN "ProductionTasks" prerequisite ON prerequisite."BusinessId" = d."BusinessId"
        AND prerequisite."Id" = d."DependsOnTaskId"
      WHERE d."BusinessId" = $1 AND d."TaskId" = $2
        AND d."DependsOnTaskId" <> $3
        AND (prerequisite."Status" NOT IN ('Completed', 'Cancelled') OR d."DependsOnTaskId" = ANY($4::uuid[]))
      LIMIT 1`,
    [businessId, dependent.Id, taskId, activated]);
    if (!unmet.length) newlyEligible.push(dependent.Id);
  }
  newlyEligible.push(...activated);

  await db.query(`
    UPDATE "ProductionTasks" SET "Status" = 'Completed', "CompletedAtUtc" = now(),
      "CompletedChecks" = "RequiredChecks", "CompletionNotes" = $1,
      "UpdatedByUserId" = $2, "UpdatedAtUtc" = now()
    WHERE "BusinessId" = $3 AND "Id" = $4`,
  [clean(payload.notes), req.currentUser.userId, businessId, taskId]);
  if (payload.alterationsRequired) {
    const notes = clean(payload.alterationNotes) ?? clean(payload.notes);
    if (notes) {
      await db.query(`
        UPDATE "OrderGarments" SET "AlterationNotes" = left(
          CASE WHEN COALESCE("AlterationNotes", '') = '' THEN $1
            ELSE "AlterationNotes" || E'\\n' || $1 END, 2000),
          "UpdatedAtUtc" = now()
        WHERE "BusinessId" = $2 AND "Id" = $3`,
      [`[Alterations ${new Date().toISOString().slice(0, 10)}] ${notes}`, businessId, task.OrderGarmentId]);
    }
  }
  await recordTaskEvent(db, businessId, taskId, 'completed', 'InProgress', 'Completed',
    req.currentUser.userId, clean(payload.notes), { newlyEligible, alterationsRequested: payload.alterationsRequired });
  await syncOrderFromTasks(db, businessId, task.OrderId);
}

async function workerTaskIssue(db, req, taskId, payload) {
  const businessId = req.currentUser.businessId;
  const { rows: members } = await db.query(`
    SELECT "Id" FROM "Memberships" WHERE "BusinessId" = $1 AND "UserId" = $2 AND "Status" = 0`,
  [businessId, req.currentUser.userId]);
  if (!members.length) throw AppError.forbidden('no_active_membership', 'Your account is not an active member of this business.');
  const { rows } = await db.query(`
    SELECT "Id", "OrderId", "Status" FROM "ProductionTasks"
    WHERE "BusinessId" = $1 AND "Id" = $2 AND "AssignedMembershipId" = $3 FOR UPDATE`,
  [businessId, taskId, members[0].Id]);
  if (!rows.length) throw AppError.notFound('task_not_found', 'Task not found.');
  const task = rows[0];
  if (['Completed', 'Cancelled', 'Blocked'].includes(task.Status)) {
    throw AppError.conflict('invalid_transition', `A ${task.Status} task cannot be reported on.`);
  }
  await db.query(`
    UPDATE "ProductionTasks" SET "StatusBeforeBlock" = "Status", "Status" = 'Blocked',
      "BlockedType" = $1, "BlockedReason" = $2, "UpdatedByUserId" = $3, "UpdatedAtUtc" = now()
    WHERE "BusinessId" = $4 AND "Id" = $5`,
  [payload.type, payload.description, req.currentUser.userId, businessId, taskId]);
  await recordTaskEvent(db, businessId, taskId, 'issue_reported', task.Status, 'Blocked',
    req.currentUser.userId, payload.description, { type: payload.type });
  await syncOrderFromTasks(db, businessId, task.OrderId);
}

async function requestGarmentAlterations(db, req, garmentId, notes, allowed) {
  const businessId = req.currentUser.businessId;
  const { rows: garmentRows } = await db.query(`
    SELECT g."OrderId", o."Status", o."LocationId"
    FROM "OrderGarments" g JOIN "Orders" o ON o."BusinessId" = g."BusinessId" AND o."Id" = g."OrderId"
    WHERE g."BusinessId" = $1 AND g."Id" = $2
      AND ($3::uuid[] IS NULL OR o."LocationId" = ANY($3::uuid[]))
    FOR UPDATE OF g, o`,
  [businessId, garmentId, allowed]);
  if (!garmentRows.length) throw AppError.notFound('garment_not_found', 'Garment not found.');
  const garment = garmentRows[0];
  if (!['Confirmed', 'InProduction', 'Ready'].includes(garment.Status)) {
    throw AppError.conflict('order_not_active', 'Alterations can only be requested while the order is in production.');
  }
  const { rows: conditionalTasks } = await db.query(`
    SELECT "Id" FROM "ProductionTasks"
    WHERE "BusinessId" = $1 AND "OrderGarmentId" = $2 AND "IsConditional"
      AND "Status" = 'Cancelled' AND "CancelReason" = 'Not required (conditional step)'
    FOR UPDATE`,
  [businessId, garmentId]);
  if (!conditionalTasks.length) {
    throw AppError.conflict('no_conditional_steps', 'This garment has no alteration step to activate (it was already requested, or its workflow has none).');
  }
  const taskIds = conditionalTasks.map((task) => task.Id);
  await db.query(`
    UPDATE "ProductionTasks" SET "Status" = 'Pending', "CancelReason" = NULL,
      "UpdatedByUserId" = $1, "UpdatedAtUtc" = now()
    WHERE "BusinessId" = $2 AND "Id" = ANY($3::uuid[])`,
  [req.currentUser.userId, businessId, taskIds]);
  for (const taskId of taskIds) {
    await recordTaskEvent(db, businessId, taskId, 'alterations_requested',
      'Cancelled', 'Pending', req.currentUser.userId, notes);
  }
  await db.query(`
    UPDATE "OrderGarments" SET "AlterationNotes" = left(
      CASE WHEN COALESCE("AlterationNotes", '') = '' THEN $1
        ELSE "AlterationNotes" || E'\\n' || $1 END, 2000),
      "UpdatedAtUtc" = now()
    WHERE "BusinessId" = $2 AND "Id" = $3`,
  [`[Alterations ${new Date().toISOString().slice(0, 10)}] ${notes}`, businessId, garmentId]);
  await db.query(`
    INSERT INTO "AuditLogs" ("Id", "Action", "ActorUserId", "BusinessId", "CreatedAtUtc",
      "DetailsJson", "EntityId", "EntityType")
    VALUES ($1, 'garment.alterations_requested', $2, $3, now(), $4::jsonb, $5, 'OrderGarment')`,
  [randomUUID(), req.currentUser.userId, businessId, JSON.stringify({ notes }), garmentId]);
  await syncOrderFromTasks(db, businessId, garment.OrderId);
  return taskIds;
}

async function writeWorkflow(db, req, payload, dependencies, workflowId = randomUUID()) {
  const businessId = req.currentUser.businessId;
  await db.query(`SELECT "Id" FROM "Businesses" WHERE "Id" = $1 FOR UPDATE`, [businessId]);
  const { rows: existingRows } = await db.query(`
    SELECT "Id", "Name", "IsDefault", "IsActive" FROM "Workflows"
    WHERE "BusinessId" = $1 ORDER BY "Name" FOR UPDATE`,
  [businessId]);
  const existing = existingRows.find((row) => row.Id === workflowId);
  if (workflowId !== existing?.Id && existingRows.some((row) => row.Name === payload.name)) {
    throw AppError.conflict('workflow_exists', 'A workflow with this name already exists.');
  }
  if (existing && existingRows.some((row) => row.Id !== workflowId && row.Name === payload.name)) {
    throw AppError.conflict('workflow_exists', 'A workflow with this name already exists.');
  }
  const isDefault = payload.isDefault || (!existing && existingRows.length === 0);
  if (existing?.IsDefault && !payload.isDefault) {
    throw AppError.badRequest('default_required', 'Make another workflow the default instead of unsetting this one.');
  }
  if ((isDefault || existing?.IsDefault) && !payload.isActive) {
    throw AppError.badRequest('default_must_be_active', 'The default workflow must be active.');
  }
  const categoryKeys = new Set();
  const categories = payload.garmentCategories.map((category) => category.trim())
    .filter((category) => {
      const normalized = category.toLowerCase();
      if (!category || categoryKeys.has(normalized)) return false;
      categoryKeys.add(normalized);
      return true;
    });
  if (isDefault) {
    await db.query(`UPDATE "Workflows" SET "IsDefault" = FALSE, "UpdatedAtUtc" = now() WHERE "BusinessId" = $1 AND "IsDefault"`, [businessId]);
  }

  if (!existing) {
    await db.query(`
      INSERT INTO "Workflows" ("Id", "BusinessId", "Name", "Description", "IsDefault", "IsActive", "GarmentCategories", "CreatedAtUtc")
      VALUES ($1, $2, $3, $4, $5, $6, $7::text[], now())`,
    [workflowId, businessId, payload.name, clean(payload.description), isDefault, payload.isActive, categories]);
  } else {
    await db.query(`
      UPDATE "Workflows" SET "Name" = $1, "Description" = $2, "IsDefault" = $3, "IsActive" = $4,
        "GarmentCategories" = $5::text[], "UpdatedAtUtc" = now()
      WHERE "BusinessId" = $6 AND "Id" = $7`,
    [payload.name, clean(payload.description), isDefault, payload.isActive, categories, businessId, workflowId]);
  }

  const { rows: currentStages } = await db.query(`
    SELECT "Id", "Key" FROM "Stages" WHERE "BusinessId" = $1 AND "WorkflowId" = $2`,
  [businessId, workflowId]);
  const currentByKey = new Map(currentStages.map((stage) => [stage.Key, stage]));
  const incomingKeys = new Set(payload.stages.map((stage) => stage.key));
  const removed = currentStages.filter((stage) => !incomingKeys.has(stage.Key)).map((stage) => stage.Id);
  const existingStageIds = currentStages.map((stage) => stage.Id);
  const stageIds = new Map(payload.stages.map((stage) => [
    stage.key, currentByKey.get(stage.key)?.Id ?? randomUUID(),
  ]));
  if (existingStageIds.length) {
    await db.query(`
      DELETE FROM "StageDependencies" WHERE "BusinessId" = $1
        AND ("StageId" = ANY($2::uuid[]) OR "DependsOnStageId" = ANY($2::uuid[]))`,
    [businessId, existingStageIds]);
  }
  if (removed.length) {
    await db.query(`DELETE FROM "Stages" WHERE "BusinessId" = $1 AND "Id" = ANY($2::uuid[])`, [businessId, removed]);
  }

  for (let index = 0; index < payload.stages.length; index++) {
    const stage = payload.stages[index];
    const stageId = stageIds.get(stage.key);
    const values = [stageId, businessId, workflowId, stage.key, stage.name, index,
      clean(stage.instructions), clean(stage.requiredSkill)?.toLowerCase() ?? null, stage.defaultMinutes ?? null,
      stage.isConditional, stage.completionChecks];
    if (currentByKey.has(stage.key)) {
      await db.query(`
        UPDATE "Stages" SET "Name" = $1, "SortOrder" = $2, "Instructions" = $3, "RequiredSkill" = $4,
          "DefaultMinutes" = $5, "IsConditional" = $6, "CompletionChecks" = $7::text[], "UpdatedAtUtc" = now()
        WHERE "BusinessId" = $8 AND "WorkflowId" = $9 AND "Id" = $10`,
      [stage.name, index, values[6], values[7], values[8], stage.isConditional, stage.completionChecks,
        businessId, workflowId, stageId]);
    } else {
      await db.query(`
        INSERT INTO "Stages" ("Id", "BusinessId", "WorkflowId", "Key", "Name", "SortOrder", "Instructions",
          "RequiredSkill", "DefaultMinutes", "IsConditional", "CompletionChecks", "CreatedAtUtc")
        VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11::text[], now())`, values);
    }
  }
  for (const stage of payload.stages) {
    for (const prerequisite of dependencies.get(stage.key) ?? []) {
      await db.query(`
        INSERT INTO "StageDependencies" ("StageId", "DependsOnStageId", "BusinessId")
        VALUES ($1, $2, $3)`,
      [stageIds.get(stage.key), stageIds.get(prerequisite), businessId]);
    }
  }

  await auditWorkflow(db, req, existing ? 'workflow.updated' : 'workflow.created', workflowId, payload.name);
  return loadWorkflow(db, businessId, workflowId);
}

async function loadOrder(db, businessId, id, allowed) {
  const { rows } = await db.query(`
    SELECT o.*, xmin::text::bigint AS "Version", c."FullName" AS "CustomerName"
    FROM "Orders" o JOIN "Customers" c ON c."Id" = o."CustomerId" AND c."BusinessId" = o."BusinessId"
    WHERE o."BusinessId" = $1 AND o."Id" = $2 AND ($3::uuid[] IS NULL OR o."LocationId" = ANY($3::uuid[]))`,
  [businessId, id, allowed]);
  if (!rows.length) throw AppError.notFound('order_not_found', 'Order not found.');
  const order = rows[0];
  const [garmentResult, progressResult] = await Promise.all([
    db.query(`
      SELECT "Id", "LineNumber", "Name", "Category", "Quantity", "UnitPrice", "LineTotal", "Fabric", "Colour",
        "StyleInstructions", "AlterationNotes", "MeasurementSetId", "MeasurementRevision", "MeasurementSnapshotJson",
        "DesignId", "DesignNameSnapshot"
      FROM "OrderGarments" WHERE "BusinessId" = $1 AND "OrderId" = $2 ORDER BY "LineNumber"`,
    [businessId, id]),
    db.query(`
      SELECT "Status", "DeadlineUtc" FROM "ProductionTasks"
      WHERE "BusinessId" = $1 AND "OrderId" = $2 AND "Status" <> 'Cancelled'`, [businessId, id]),
  ]);
  const tasks = progressResult.rows;
  const completed = tasks.filter((task) => task.Status === 'Completed').length;
  const progress = tasks.length ? {
    totalTasks: tasks.length,
    completed,
    inProgress: tasks.filter((task) => task.Status === 'InProgress').length,
    blocked: tasks.filter((task) => task.Status === 'Blocked').length,
    overdue: tasks.filter((task) => !['Completed', 'Cancelled'].includes(task.Status) && task.DeadlineUtc && new Date(task.DeadlineUtc) < new Date()).length,
    percentComplete: Math.round(100 * completed / tasks.length),
  } : null;
  return {
    id: order.Id,
    orderNumber: order.OrderNumber,
    status: order.Status,
    customerId: order.CustomerId,
    customerName: order.CustomerName,
    locationId: order.LocationId,
    promisedDate: order.PromisedDate instanceof Date ? order.PromisedDate.toISOString().slice(0, 10) : String(order.PromisedDate).slice(0, 10),
    priority: order.Priority,
    deliveryMethod: order.DeliveryMethod,
    deliveryAddress: order.DeliveryAddress,
    deliveryInstructions: order.DeliveryInstructions,
    notes: order.Notes,
    currencyCode: order.CurrencyCode,
    subtotal: Number(order.Subtotal),
    discount: Number(order.Discount),
    total: Number(order.Total),
    version: Number(order.Version),
    createdAtUtc: order.CreatedAtUtc,
    quotedAtUtc: order.QuotedAtUtc,
    confirmedAtUtc: order.ConfirmedAtUtc,
    cancelledAtUtc: order.CancelledAtUtc,
    cancellationReason: order.CancellationReason,
    readyAtUtc: order.ReadyAtUtc,
    deliveredAtUtc: order.DeliveredAtUtc,
    progress,
    garments: garmentResult.rows.map((garment) => ({
      id: garment.Id,
      lineNumber: garment.LineNumber,
      name: garment.Name,
      category: garment.Category,
      quantity: garment.Quantity,
      unitPrice: Number(garment.UnitPrice),
      lineTotal: Number(garment.LineTotal),
      fabric: garment.Fabric,
      colour: garment.Colour,
      styleInstructions: garment.StyleInstructions,
      alterationNotes: garment.AlterationNotes,
      measurementSetId: garment.MeasurementSetId,
      measurementRevision: garment.MeasurementRevision,
      measurementSnapshot: typeof garment.MeasurementSnapshotJson === 'string'
        ? JSON.parse(garment.MeasurementSnapshotJson) : garment.MeasurementSnapshotJson,
      designId: garment.DesignId,
      designName: garment.DesignNameSnapshot,
    })),
  };
}

async function addGarment(db, req, orderId, garment, index, customerId, existingId = null) {
  if (money(garment.unitPrice) !== garment.unitPrice) {
    throw AppError.badRequest('invalid_price', `'${garment.name}': price can have at most 2 decimal places.`);
  }
  const lineTotal = money(garment.quantity * garment.unitPrice);
  let designName = null;
  let designPrice = null;
  if (garment.designId) {
    const { rows } = await db.query(`
      SELECT "Name", "BasePrice", "Status" FROM "Designs" WHERE "BusinessId" = $1 AND "Id" = $2`,
    [req.currentUser.businessId, garment.designId]);
    if (!rows.length) throw AppError.badRequest('unknown_design', 'That design was not found.');
    if (rows[0].Status !== 'Active') throw AppError.conflict('design_archived', `'${rows[0].Name}' is archived. Restore it before using it on an order.`);
    designName = rows[0].Name;
    designPrice = rows[0].BasePrice;
  }
  let measurementRevision = null;
  let measurementSnapshot = null;
  let category = clean(garment.category);
  if (garment.measurementSetId) {
    const { rows } = await db.query(`
      SELECT s."Id", s."Label", s."CurrentRevision", s."CustomerId", s."TemplateId",
        r."RevisionNumber", r."ValuesJson", r."Notes", r."CreatedAtUtc", t."Name" AS "TemplateName"
      FROM "MeasurementSets" s
      JOIN "MeasurementTemplates" t ON t."Id" = s."TemplateId" AND t."BusinessId" = s."BusinessId"
      JOIN "MeasurementRevisions" r ON r."SetId" = s."Id" AND r."BusinessId" = s."BusinessId"
        AND r."RevisionNumber" = COALESCE($4, s."CurrentRevision")
      WHERE s."BusinessId" = $1 AND s."Id" = $2 AND s."CustomerId" = $3`,
    [req.currentUser.businessId, garment.measurementSetId, customerId, garment.measurementRevision ?? null]);
    if (!rows.length) throw AppError.badRequest('unknown_measurement_set', 'That measurement profile or revision does not belong to this customer.');
    measurementRevision = rows[0].RevisionNumber;
    const values = typeof rows[0].ValuesJson === 'string' ? JSON.parse(rows[0].ValuesJson) : rows[0].ValuesJson;
    measurementSnapshot = {
      templateName: rows[0].TemplateName,
      profile: rows[0].Label,
      revision: rows[0].RevisionNumber,
      recordedAtUtc: rows[0].CreatedAtUtc,
      notes: rows[0].Notes,
      values,
    };
    category ??= rows[0].TemplateName;
  } else if (garment.measurementRevision) {
    throw AppError.badRequest('unknown_revision', 'A measurement revision requires a measurement profile.');
  }
  const values = [
    existingId ?? randomUUID(), req.currentUser.businessId, orderId, index, garment.name, category, garment.quantity,
    toMoney(garment.unitPrice), toMoney(lineTotal), clean(garment.fabric), clean(garment.colour),
    clean(garment.styleInstructions), clean(garment.alterationNotes), garment.designId ?? null,
    designName, designPrice, garment.measurementSetId ?? null, measurementRevision,
    measurementSnapshot ? JSON.stringify(measurementSnapshot) : null,
  ];
  if (existingId) {
    await db.query(`
      UPDATE "OrderGarments" SET "LineNumber" = $4, "Name" = $5, "Category" = $6, "Quantity" = $7, "UnitPrice" = $8,
        "LineTotal" = $9, "Fabric" = $10, "Colour" = $11, "StyleInstructions" = $12, "AlterationNotes" = $13,
        "DesignId" = $14, "DesignNameSnapshot" = $15, "DesignPriceSnapshot" = $16, "MeasurementSetId" = $17,
        "MeasurementRevision" = $18, "MeasurementSnapshotJson" = $19::jsonb, "UpdatedAtUtc" = now()
      WHERE "BusinessId" = $2 AND "OrderId" = $3 AND "Id" = $1`, values);
  } else {
    await db.query(`
      INSERT INTO "OrderGarments" ("Id", "BusinessId", "OrderId", "LineNumber", "Name", "Category", "Quantity",
        "UnitPrice", "LineTotal", "Fabric", "Colour", "StyleInstructions", "AlterationNotes", "DesignId",
        "DesignNameSnapshot", "DesignPriceSnapshot", "MeasurementSetId", "MeasurementRevision", "MeasurementSnapshotJson",
        "CreatedAtUtc")
      VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, $16, $17, $18, $19::jsonb, now())`,
    values);
  }
  return lineTotal;
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
  const requireWorkersManage = [requireBearerAuth(env, pool), requirePermission('workers.manage')];

  router.get('/orders', ...requireOrdersView, async (req, res) => {
    const page = Number.parseInt(req.query.page ?? '1', 10);
    const pageSize = Number.parseInt(req.query.pageSize ?? '25', 10);
    if (!Number.isSafeInteger(page) || page < 1 || !Number.isSafeInteger(pageSize) || pageSize < 1 || pageSize > 100) {
      throw AppError.badRequest('invalid_paging', 'Page must be positive and pageSize must be between 1 and 100.');
    }
    const allowed = await allowedLocations(pool, req.currentUser);
    const filters = ['o."BusinessId" = $1', '($2::uuid[] IS NULL OR o."LocationId" = ANY($2::uuid[]))'];
    const values = [req.currentUser.businessId, allowed];
    if (req.query.status) {
      if (!['Draft', 'Quoted', 'Confirmed', 'InProduction', 'Ready', 'Delivered', 'Cancelled'].includes(String(req.query.status))) {
        throw AppError.badRequest('invalid_status', 'The order status is invalid.');
      }
      values.push(String(req.query.status));
      filters.push(`o."Status" = $${values.length}`);
    }
    for (const [name, column] of [['customerId', 'CustomerId'], ['locationId', 'LocationId']]) {
      if (req.query[name]) {
        if (!isUuid(String(req.query[name]))) throw AppError.badRequest(`invalid_${name}`, `The ${name} is invalid.`);
        values.push(String(req.query[name]));
        filters.push(`o."${column}" = $${values.length}`);
      }
    }
    for (const [name, operator] of [['dueFrom', '>='], ['dueTo', '<=']]) {
      if (req.query[name]) {
        if (!/^\d{4}-\d{2}-\d{2}$/.test(String(req.query[name]))) throw AppError.badRequest('invalid_due_date', 'The due date is invalid.');
        values.push(String(req.query[name]));
        filters.push(`o."PromisedDate" ${operator} $${values.length}::date`);
      }
    }
    if (req.query.search) {
      const search = `%${String(req.query.search).trim().slice(0, 100).replace(/[\\%_]/g, '\\$&')}%`;
      values.push(search);
      filters.push(`(o."OrderNumber" ILIKE $${values.length} ESCAPE E'\\\\' OR c."FullName" ILIKE $${values.length} ESCAPE E'\\\\')`);
    }
    const where = filters.join(' AND ');
    const count = await pool.query(`
      SELECT count(*)::int AS "TotalCount" FROM "Orders" o
      JOIN "Customers" c ON c."BusinessId" = o."BusinessId" AND c."Id" = o."CustomerId"
      WHERE ${where}`, values);
    const { rows } = await pool.query(`
      SELECT o."Id", o."OrderNumber", o."Status", o."CustomerId", c."FullName" AS "CustomerName",
        o."PromisedDate", o."Priority", o."Total", o."CurrencyCode",
        (SELECT count(*)::int FROM "OrderGarments" g WHERE g."BusinessId" = o."BusinessId" AND g."OrderId" = o."Id") AS "GarmentCount"
      FROM "Orders" o JOIN "Customers" c ON c."BusinessId" = o."BusinessId" AND c."Id" = o."CustomerId"
      WHERE ${where} ORDER BY o."PromisedDate", o."CreatedAtUtc" DESC
      LIMIT $${values.length + 1} OFFSET $${values.length + 2}`,
    [...values, pageSize, (page - 1) * pageSize]);
    return res.status(200).json({
      items: rows.map((row) => ({
        id: row.Id, orderNumber: row.OrderNumber, status: row.Status, customerId: row.CustomerId,
        customerName: row.CustomerName, promisedDate: String(row.PromisedDate).slice(0, 10),
        priority: row.Priority, total: Number(row.Total), currencyCode: row.CurrencyCode, garmentCount: row.GarmentCount,
      })),
      page, pageSize, totalCount: Number(count.rows[0]?.TotalCount ?? 0),
    });
  });

  router.get('/orders/:id', ...requireOrdersView, async (req, res) => {
    if (!isUuid(req.params.id)) throw AppError.badRequest('invalid_order_id', 'The order ID is invalid.');
    return res.status(200).json(await loadOrder(pool, req.currentUser.businessId, req.params.id, await allowedLocations(pool, req.currentUser)));
  });

  router.get('/orders/:id/pricing', ...[requireBearerAuth(env, pool), requirePermission('pricing.view')], async (req, res) => {
    if (!isUuid(req.params.id)) throw AppError.badRequest('invalid_order_id', 'The order ID is invalid.');
    const allowed = await allowedLocations(pool, req.currentUser);
    const order = await loadOrder(pool, req.currentUser.businessId, req.params.id, allowed);
    const { rows } = await pool.query(`
      SELECT "Id", "LineNumber", "Name", "Quantity", "UnitPrice", "DesignPriceSnapshot",
        "PriceOverrideReason", "PricingSnapshotJson"
      FROM "OrderGarments" WHERE "BusinessId" = $1 AND "OrderId" = $2 ORDER BY "LineNumber"`,
    [req.currentUser.businessId, order.id]);
    return res.status(200).json({
      orderId: order.id, orderNumber: order.orderNumber, currencyCode: order.currencyCode,
      lines: rows.map((row) => ({
        lineId: row.Id, lineNumber: row.LineNumber, name: row.Name, quantity: row.Quantity,
        unitPrice: Number(row.UnitPrice), designPriceSnapshot: row.DesignPriceSnapshot === null ? null : Number(row.DesignPriceSnapshot),
        priceOverrideReason: row.PriceOverrideReason,
        calculation: typeof row.PricingSnapshotJson === 'string' ? JSON.parse(row.PricingSnapshotJson) : row.PricingSnapshotJson,
      })),
    });
  });

  router.post('/orders', ...requireOrdersManage, async (req, res) => {
    const payload = parse(createOrderSchema, req.body);
    const idempotencyKey = clean(req.get('idempotency-key'));
    if (idempotencyKey?.length > 100) throw AppError.badRequest('invalid_idempotency_key', 'Idempotency key is too long.');
    const businessId = req.currentUser.businessId;
    const allowed = await allowedLocations(pool, req.currentUser);
    try {
      const result = await transaction(pool, async (client) => {
        if (idempotencyKey) {
          const { rows } = await client.query(`
            SELECT "Id" FROM "Orders" WHERE "BusinessId" = $1 AND "IdempotencyKey" = $2 FOR UPDATE`,
          [businessId, idempotencyKey]);
          if (rows.length) return { order: await loadOrder(client, businessId, rows[0].Id, allowed), created: false };
        }
        await ensureLocation(client, businessId, payload.locationId, allowed);
        const { rows: customers } = await client.query(`
          SELECT "Id", "Status" FROM "Customers" WHERE "BusinessId" = $1 AND "Id" = $2`,
        [businessId, payload.customerId]);
        if (!customers.length) throw AppError.badRequest('unknown_customer', 'Customer not found.');
        if (customers[0].Status !== 'Active') throw AppError.conflict('customer_not_active', 'Orders can only be created for active customers.');
        const { rows: business } = await client.query(`
          SELECT "CurrencyCode", (now() AT TIME ZONE "TimeZoneId")::date::text AS "Today"
          FROM "Businesses" WHERE "Id" = $1`, [businessId]);
        if (payload.promisedDate < business[0].Today) {
          throw AppError.badRequest('promised_date_in_past', 'The promised date cannot be in the past.');
        }
        if (payload.deliveryMethod === 'Delivery' && !clean(payload.deliveryAddress)) {
          throw AppError.badRequest('delivery_address_required', 'A delivery address is required for delivery orders.');
        }
        const lines = payload.garments.map((garment) => money(garment.quantity * garment.unitPrice));
        const subtotal = money(lines.reduce((total, line) => total + line, 0));
        if (money(payload.discount) !== payload.discount) throw AppError.badRequest('invalid_discount', 'Discount can have at most 2 decimal places.');
        if (payload.discount > subtotal) throw AppError.badRequest('discount_exceeds_total', 'The discount cannot exceed the order subtotal.');
        const year = new Date().getUTCFullYear();
        const { rows: counter } = await client.query(`
          INSERT INTO "OrderCounters" ("BusinessId", "Year", "LastValue") VALUES ($1, $2, 1)
          ON CONFLICT ("BusinessId", "Year") DO UPDATE SET "LastValue" = "OrderCounters"."LastValue" + 1
          RETURNING "LastValue"`, [businessId, year]);
        const number = `ORD-${year}-${String(counter[0].LastValue).padStart(5, '0')}`;
        const orderId = randomUUID();
        await client.query(`
          INSERT INTO "Orders" ("Id", "BusinessId", "LocationId", "CustomerId", "OrderNumber", "Status", "Priority",
            "PromisedDate", "DeliveryMethod", "DeliveryAddress", "DeliveryInstructions", "Notes", "CurrencyCode",
            "Subtotal", "Discount", "Total", "IdempotencyKey", "CreatedByUserId", "CreatedAtUtc")
          VALUES ($1, $2, $3, $4, $5, 'Draft', $6, $7::date, $8, $9, $10, $11, $12, $13, $14, $15, $16, $17, now())`,
        [orderId, businessId, payload.locationId, payload.customerId, number, payload.priority, payload.promisedDate,
          payload.deliveryMethod, clean(payload.deliveryAddress), clean(payload.deliveryInstructions), clean(payload.notes),
          business[0].CurrencyCode, toMoney(subtotal), toMoney(payload.discount), toMoney(subtotal - payload.discount),
          idempotencyKey, req.currentUser.userId]);
        for (let index = 0; index < payload.garments.length; index += 1) {
          await addGarment(client, req, orderId, payload.garments[index], index + 1, payload.customerId);
        }
        await auditOrder(client, req, 'order.created', orderId, { orderNumber: number, total: subtotal - payload.discount, currencyCode: business[0].CurrencyCode });
        return { order: await loadOrder(client, businessId, orderId, allowed), created: true };
      });
      return res.status(result.created ? 201 : 200).json(result.order);
    } catch (error) {
      if (idempotencyKey && error?.code === '23505') {
        const { rows } = await pool.query(`SELECT "Id" FROM "Orders" WHERE "BusinessId" = $1 AND "IdempotencyKey" = $2`,
          [businessId, idempotencyKey]);
        if (rows.length) return res.status(200).json(await loadOrder(pool, businessId, rows[0].Id, allowed));
      }
      throw error;
    }
  });

  router.put('/orders/:id', ...requireOrdersManage, async (req, res) => {
    if (!isUuid(req.params.id)) throw AppError.badRequest('invalid_order_id', 'The order ID is invalid.');
    const payload = parse(updateOrderSchema, req.body);
    const businessId = req.currentUser.businessId;
    const allowed = await allowedLocations(pool, req.currentUser);
    const order = await transaction(pool, async (client) => {
      const { rows } = await client.query(`
        SELECT *, xmin::text::bigint AS "Version" FROM "Orders"
        WHERE "BusinessId" = $1 AND "Id" = $2 AND ($3::uuid[] IS NULL OR "LocationId" = ANY($3::uuid[])) FOR UPDATE`,
      [businessId, req.params.id, allowed]);
      if (!rows.length) throw AppError.notFound('order_not_found', 'Order not found.');
      const current = rows[0];
      if (Number(current.Version) !== payload.version) throw AppError.conflict('stale_version', 'This order was changed by someone else. Reload and try again.');
      if (!['Draft', 'Quoted'].includes(current.Status)) {
        throw AppError.conflict('order_locked', 'Only draft or quoted orders can be edited here. Confirmed orders need an amendment.');
      }
      await ensureLocation(client, businessId, payload.locationId, allowed);
      const { rows: business } = await client.query(`
        SELECT (now() AT TIME ZONE "TimeZoneId")::date::text AS "Today" FROM "Businesses" WHERE "Id" = $1`, [businessId]);
      if (payload.promisedDate !== String(current.PromisedDate).slice(0, 10) && payload.promisedDate < business[0].Today) {
        throw AppError.badRequest('promised_date_in_past', 'The promised date cannot be in the past.');
      }
      if (payload.deliveryMethod === 'Delivery' && !clean(payload.deliveryAddress)) {
        throw AppError.badRequest('delivery_address_required', 'A delivery address is required for delivery orders.');
      }
      const { rows: oldGarments } = await client.query(`
        SELECT "Id" FROM "OrderGarments" WHERE "BusinessId" = $1 AND "OrderId" = $2 FOR UPDATE`,
      [businessId, req.params.id]);
      const oldIds = new Set(oldGarments.map((garment) => garment.Id));
      const retained = new Set();
      let subtotal = 0;
      for (let index = 0; index < payload.garments.length; index += 1) {
        const garment = payload.garments[index];
        if (garment.id && !oldIds.has(garment.id)) throw AppError.badRequest('unknown_line', 'A garment line in the request does not belong to this order.');
        if (garment.id && retained.has(garment.id)) throw AppError.badRequest('duplicate_line', 'The same garment line appears twice.');
        if (garment.id) retained.add(garment.id);
        subtotal = money(subtotal + await addGarment(client, req, req.params.id, garment, index + 1, current.CustomerId, garment.id ?? null));
      }
      if (money(payload.discount) !== payload.discount) throw AppError.badRequest('invalid_discount', 'Discount can have at most 2 decimal places.');
      if (payload.discount > subtotal) throw AppError.badRequest('discount_exceeds_total', 'The discount cannot exceed the order subtotal.');
      const quoted = current.Status === 'Quoted';
      const result = await client.query(`
        UPDATE "Orders" SET "LocationId" = $1, "PromisedDate" = $2::date, "Priority" = $3, "DeliveryMethod" = $4,
          "DeliveryAddress" = $5, "DeliveryInstructions" = $6, "Notes" = $7, "Subtotal" = $8, "Discount" = $9,
          "Total" = $10, "Status" = CASE WHEN "Status" = 'Quoted' THEN 'Draft' ELSE "Status" END,
          "QuotedAtUtc" = CASE WHEN "Status" = 'Quoted' THEN NULL ELSE "QuotedAtUtc" END, "UpdatedAtUtc" = now()
        WHERE "BusinessId" = $11 AND "Id" = $12 AND xmin = $13::xid`,
      [payload.locationId, payload.promisedDate, payload.priority, payload.deliveryMethod, clean(payload.deliveryAddress),
        clean(payload.deliveryInstructions), clean(payload.notes), toMoney(subtotal), toMoney(payload.discount),
        toMoney(subtotal - payload.discount), businessId, req.params.id, payload.version]);
      if (!result.rowCount) throw AppError.conflict('stale_version', 'This order was changed by someone else. Reload and try again.');
      const removed = [...oldIds].filter((id) => !retained.has(id));
      if (removed.length) await client.query(`DELETE FROM "OrderGarments" WHERE "BusinessId" = $1 AND "Id" = ANY($2::uuid[])`, [businessId, removed]);
      await auditOrder(client, req, 'order.updated', req.params.id, { total: subtotal - payload.discount, requoteRequired: quoted });
      return loadOrder(client, businessId, req.params.id, allowed);
    });
    return res.status(200).json(order);
  });

  router.post('/orders/:id/quote', ...requireOrdersManage, async (req, res) => {
    if (!isUuid(req.params.id)) throw AppError.badRequest('invalid_order_id', 'The order ID is invalid.');
    const allowed = await allowedLocations(pool, req.currentUser);
    const order = await transaction(pool, async (client) => {
      const { rows } = await client.query(`
        SELECT "Status", "Total" FROM "Orders" WHERE "BusinessId" = $1 AND "Id" = $2
          AND ($3::uuid[] IS NULL OR "LocationId" = ANY($3::uuid[])) FOR UPDATE`,
      [req.currentUser.businessId, req.params.id, allowed]);
      if (!rows.length) throw AppError.notFound('order_not_found', 'Order not found.');
      if (rows[0].Status !== 'Draft') throw AppError.conflict('invalid_transition', `Only draft orders can be quoted (this one is ${rows[0].Status}).`);
      const garments = await client.query(`SELECT count(*)::int AS "Count" FROM "OrderGarments" WHERE "BusinessId" = $1 AND "OrderId" = $2`,
        [req.currentUser.businessId, req.params.id]);
      if (!garments.rows[0].Count || Number(rows[0].Total) <= 0) throw AppError.badRequest('nothing_to_quote', 'Add priced garments before issuing a quotation.');
      await client.query(`UPDATE "Orders" SET "Status" = 'Quoted', "QuotedAtUtc" = now(), "UpdatedAtUtc" = now() WHERE "BusinessId" = $1 AND "Id" = $2`,
        [req.currentUser.businessId, req.params.id]);
      await auditOrder(client, req, 'order.quoted', req.params.id, { total: Number(rows[0].Total) });
      return loadOrder(client, req.currentUser.businessId, req.params.id, allowed);
    });
    return res.status(200).json(order);
  });

  router.post('/orders/:id/confirm', ...requireOrdersConfirm, async (req, res) => {
    if (!isUuid(req.params.id)) throw AppError.badRequest('invalid_order_id', 'The order ID is invalid.');
    const allowed = await allowedLocations(pool, req.currentUser);
    const order = await transaction(pool, async (client) => {
      const { rows } = await client.query(`
        SELECT o."Status", o."PromisedDate", o."CustomerId", c."Status" AS "CustomerStatus",
          (now() AT TIME ZONE b."TimeZoneId")::date::text AS "Today"
        FROM "Orders" o
        JOIN "Customers" c ON c."BusinessId" = o."BusinessId" AND c."Id" = o."CustomerId"
        JOIN "Businesses" b ON b."Id" = o."BusinessId"
        WHERE o."BusinessId" = $1 AND o."Id" = $2 AND ($3::uuid[] IS NULL OR o."LocationId" = ANY($3::uuid[]))
        FOR UPDATE OF o`, [req.currentUser.businessId, req.params.id, allowed]);
      if (!rows.length) throw AppError.notFound('order_not_found', 'Order not found.');
      if (rows[0].Status !== 'Quoted') throw AppError.conflict('invalid_transition', `Only quoted orders can be confirmed (this one is ${rows[0].Status}).`);
      if (String(rows[0].PromisedDate).slice(0, 10) < rows[0].Today) {
        throw AppError.badRequest('promised_date_passed', 'The promised date has passed. Update the order before confirming.');
      }
      if (rows[0].CustomerStatus !== 'Active') throw AppError.conflict('customer_not_active', 'The customer profile is not active.');
      await client.query(`UPDATE "Orders" SET "Status" = 'Confirmed', "ConfirmedAtUtc" = now(), "UpdatedAtUtc" = now() WHERE "BusinessId" = $1 AND "Id" = $2`,
        [req.currentUser.businessId, req.params.id]);
      const plan = await generateProductionTasks(client, req.currentUser.businessId, req.params.id, req.currentUser.userId);
      await auditOrder(client, req, 'order.confirmed', req.params.id, {
        tasksCreated: plan.tasksCreated,
        garmentsWithoutWorkflow: plan.garmentsWithoutWorkflow,
      });
      return loadOrder(client, req.currentUser.businessId, req.params.id, allowed);
    });
    return res.status(200).json(order);
  });

  router.post('/orders/:id/cancel', ...requireOrdersManage, async (req, res) => {
    if (!isUuid(req.params.id)) throw AppError.badRequest('invalid_order_id', 'The order ID is invalid.');
    const payload = parse(z.object({ reason: z.string().trim().min(3).max(500) }), req.body);
    const allowed = await allowedLocations(pool, req.currentUser);
    const order = await transaction(pool, async (client) => {
      const { rows } = await client.query(`
        SELECT "Status" FROM "Orders" WHERE "BusinessId" = $1 AND "Id" = $2
          AND ($3::uuid[] IS NULL OR "LocationId" = ANY($3::uuid[])) FOR UPDATE`,
      [req.currentUser.businessId, req.params.id, allowed]);
      if (!rows.length) throw AppError.notFound('order_not_found', 'Order not found.');
      if (rows[0].Status === 'Cancelled') throw AppError.conflict('invalid_transition', 'This order is already cancelled.');
      if (rows[0].Status === 'Delivered') throw AppError.conflict('invalid_transition', 'A delivered order cannot be cancelled. Record a refund instead.');
      const invoices = await client.query(`SELECT 1 FROM "Invoices" WHERE "BusinessId" = $1 AND "OrderId" = $2 AND "Status" <> 'Void' LIMIT 1`,
        [req.currentUser.businessId, req.params.id]);
      if (invoices.rows.length) throw AppError.conflict('order_has_invoices', 'This order has invoices. Void them first, then cancel the order.');
      const tasks = await client.query(`
        UPDATE "ProductionTasks" SET "Status" = 'Cancelled', "CancelReason" = $1, "UpdatedAtUtc" = now(), "UpdatedByUserId" = $2
        WHERE "BusinessId" = $3 AND "OrderId" = $4 AND "Status" NOT IN ('Completed', 'Cancelled')
        RETURNING "Id", "Status"`, [`Order cancelled: ${payload.reason}`, req.currentUser.userId, req.currentUser.businessId, req.params.id]);
      for (const task of tasks.rows) {
        await client.query(`
          INSERT INTO "TaskEvents" ("Id", "BusinessId", "TaskId", "Type", "FromStatus", "ToStatus", "ActorUserId", "Note", "CreatedAtUtc")
          VALUES ($1, $2, $3, 'cancelled', NULL, 'Cancelled', $4, $5, now())`,
        [randomUUID(), req.currentUser.businessId, task.Id, req.currentUser.userId, `Order cancelled: ${payload.reason}`]);
      }
      await client.query(`
        UPDATE "Orders" SET "Status" = 'Cancelled', "CancelledAtUtc" = now(), "CancellationReason" = $1, "UpdatedAtUtc" = now()
        WHERE "BusinessId" = $2 AND "Id" = $3`, [payload.reason, req.currentUser.businessId, req.params.id]);
      await auditOrder(client, req, 'order.cancelled', req.params.id, { from: rows[0].Status, reason: payload.reason });
      return loadOrder(client, req.currentUser.businessId, req.params.id, allowed);
    });
    return res.status(200).json(order);
  });

  router.get('/fittings', ...requireOrdersView, async (req, res) => {
    const page = Number.parseInt(req.query.page ?? '1', 10);
    const pageSize = Number.parseInt(req.query.pageSize ?? '25', 10);
    if (!Number.isSafeInteger(page) || page < 1 || !Number.isSafeInteger(pageSize) || pageSize < 1 || pageSize > 100) {
      throw AppError.badRequest('invalid_paging', 'Page must be positive and pageSize must be between 1 and 100.');
    }
    const filters = { page, pageSize };
    for (const name of ['customerId', 'orderId']) {
      if (req.query[name] !== undefined) {
        if (!isUuid(String(req.query[name]))) throw AppError.badRequest(`invalid_${name}`, `The ${name} is invalid.`);
        filters[name] = String(req.query[name]);
      }
    }
    for (const [queryName, filterName] of [['from', 'fromUtc'], ['to', 'toUtc']]) {
      if (req.query[queryName] !== undefined) {
        const date = new Date(String(req.query[queryName]));
        if (!Number.isFinite(date.getTime())) throw AppError.badRequest(`invalid_${queryName}`, `${queryName} must be a valid date-time.`);
        filters[filterName] = date;
      }
    }
    if (filters.fromUtc && filters.toUtc && filters.toUtc <= filters.fromUtc) {
      throw AppError.badRequest('invalid_date_range', 'The to date must be later than the from date.');
    }
    return res.status(200).json(await listFittings(pool, req.currentUser, filters,
      await allowedLocations(pool, req.currentUser)));
  });

  router.post('/fittings', ...requireFittingsManage, async (req, res) => {
    const payload = parse(fittingSchema, req.body);
    return res.status(201).json(await createFitting(pool, transaction, req.currentUser, payload,
      await allowedLocations(pool, req.currentUser)));
  });

  router.post('/fittings/:id/status', ...requireFittingsManage, async (req, res) => {
    if (!isUuid(req.params.id)) throw AppError.badRequest('invalid_fitting_id', 'The fitting ID is invalid.');
    const payload = parse(fittingStatusSchema, req.body);
    return res.status(200).json(await setFittingStatus(pool, transaction, req.currentUser, req.params.id, payload,
      await allowedLocations(pool, req.currentUser)));
  });

  router.get('/workflows', ...requireTasksView, async (req, res) => {
    const includeInactive = req.query.includeInactive === 'true';
    if (req.query.includeInactive !== undefined && !['true', 'false'].includes(String(req.query.includeInactive))) {
      throw AppError.badRequest('invalid_include_inactive', 'includeInactive must be true or false.');
    }
    const { rows } = await pool.query(`
      SELECT "Id" FROM "Workflows" WHERE "BusinessId" = $1 AND ($2::boolean OR "IsActive") ORDER BY "Name"`,
    [req.currentUser.businessId, includeInactive]);
    const workflows = [];
    for (const row of rows) workflows.push(await loadWorkflow(pool, req.currentUser.businessId, row.Id));
    return res.status(200).json(workflows);
  });

  router.get('/workflows/:id', ...requireTasksView, async (req, res) => {
    if (!isUuid(req.params.id)) throw AppError.badRequest('invalid_workflow_id', 'The workflow ID is invalid.');
    return res.status(200).json(await loadWorkflow(pool, req.currentUser.businessId, req.params.id));
  });

  router.post('/workflows', ...requireWorkflowsManage, async (req, res) => {
    const payload = parse(workflowSchema, req.body);
    const dependencies = validateWorkflow(payload);
    const workflow = await transaction(pool, (client) => writeWorkflow(client, req, payload, dependencies));
    return res.status(201).json(workflow);
  });

  router.put('/workflows/:id', ...requireWorkflowsManage, async (req, res) => {
    if (!isUuid(req.params.id)) throw AppError.badRequest('invalid_workflow_id', 'The workflow ID is invalid.');
    const payload = parse(workflowSchema, req.body);
    const dependencies = validateWorkflow(payload);
    const workflow = await transaction(pool, (client) => writeWorkflow(client, req, payload, dependencies, req.params.id));
    return res.status(200).json(workflow);
  });

  router.get('/workers', ...requireTasksView, async (req, res) => {
    const businessId = req.currentUser.businessId;
    const allowed = await allowedLocations(pool, req.currentUser);
    const { rows: members } = await pool.query(`
      SELECT m."Id", m."DisplayName"
      FROM "Memberships" m
      WHERE m."BusinessId" = $1 AND m."Status" = 0
        AND ($2::uuid[] IS NULL OR EXISTS (
          SELECT 1 FROM "MembershipLocations" ml
          WHERE ml."BusinessId" = m."BusinessId" AND ml."MembershipId" = m."Id"
            AND ml."LocationId" = ANY($2::uuid[])
        ))
      ORDER BY m."DisplayName"`,
    [businessId, allowed]);
    const memberIds = members.map((member) => member.Id);
    const [skills, openTasks] = memberIds.length ? await Promise.all([
      pool.query(`SELECT "MembershipId", "Skill" FROM "WorkerSkills"
        WHERE "BusinessId" = $1 AND "MembershipId" = ANY($2::uuid[]) ORDER BY "Skill"`,
      [businessId, memberIds]),
      pool.query(`
        SELECT t."AssignedMembershipId" AS "MembershipId", count(*)::int AS "OpenTasks"
        FROM "ProductionTasks" t
        JOIN "Orders" o ON o."BusinessId" = t."BusinessId" AND o."Id" = t."OrderId"
        WHERE t."BusinessId" = $1 AND t."AssignedMembershipId" = ANY($2::uuid[])
          AND t."Status" NOT IN ('Completed', 'Cancelled')
          AND ($3::uuid[] IS NULL OR o."LocationId" = ANY($3::uuid[]))
        GROUP BY t."AssignedMembershipId"`,
      [businessId, memberIds, allowed]),
    ]) : [{ rows: [] }, { rows: [] }];
    const skillsByMember = new Map();
    for (const row of skills.rows) {
      const list = skillsByMember.get(row.MembershipId) ?? [];
      list.push(row.Skill);
      skillsByMember.set(row.MembershipId, list);
    }
    const taskCounts = new Map(openTasks.rows.map((row) => [row.MembershipId, Number(row.OpenTasks)]));
    return res.status(200).json(members.map((member) => ({
      membershipId: member.Id,
      displayName: member.DisplayName,
      isActive: true,
      skills: skillsByMember.get(member.Id) ?? [],
      openTasks: taskCounts.get(member.Id) ?? 0,
    })));
  });

  router.put('/workers/:membershipId/skills', ...requireWorkersManage, async (req, res) => {
    if (!isUuid(req.params.membershipId)) throw AppError.badRequest('invalid_membership_id', 'The membership ID is invalid.');
    const { skills: inputSkills } = parse(setWorkerSkillsSchema, req.body);
    const skills = [...new Set(inputSkills.map((skill) => skill.trim().toLowerCase()))];
    const allowed = await allowedLocations(pool, req.currentUser);
    const worker = await transaction(pool, async (client) => {
      const { rows: members } = await client.query(`
        SELECT m."Id", m."DisplayName" FROM "Memberships" m
        WHERE m."BusinessId" = $1 AND m."Id" = $2 AND m."Status" = 0
          AND ($3::uuid[] IS NULL OR EXISTS (
            SELECT 1 FROM "MembershipLocations" ml
            WHERE ml."BusinessId" = m."BusinessId" AND ml."MembershipId" = m."Id"
              AND ml."LocationId" = ANY($3::uuid[])
          ))`,
      [req.currentUser.businessId, req.params.membershipId, allowed]);
      if (!members.length) throw AppError.notFound('staff_not_found', 'Active staff member was not found in your permitted locations.');
      await client.query(`DELETE FROM "WorkerSkills" WHERE "BusinessId" = $1 AND "MembershipId" = $2`,
        [req.currentUser.businessId, req.params.membershipId]);
      for (const skill of skills) {
        await client.query(`
          INSERT INTO "WorkerSkills" ("MembershipId", "Skill", "BusinessId") VALUES ($1, $2, $3)`,
        [req.params.membershipId, skill, req.currentUser.businessId]);
      }
      await client.query(`
        INSERT INTO "AuditLogs" ("Id", "Action", "ActorUserId", "BusinessId", "CreatedAtUtc",
          "DetailsJson", "EntityId", "EntityType")
        VALUES ($1, 'worker.skills_changed', $2, $3, now(), $4::jsonb, $5, 'WorkerSkill')`,
      [randomUUID(), req.currentUser.userId, req.currentUser.businessId, JSON.stringify({ skills }), req.params.membershipId]);
      return members[0];
    });
    const { rows: taskRows } = await pool.query(`
      SELECT count(*)::int AS "OpenTasks"
      FROM "ProductionTasks" t
      JOIN "Orders" o ON o."BusinessId" = t."BusinessId" AND o."Id" = t."OrderId"
      WHERE t."BusinessId" = $1 AND t."AssignedMembershipId" = $2
        AND t."Status" NOT IN ('Completed', 'Cancelled')
        AND ($3::uuid[] IS NULL OR o."LocationId" = ANY($3::uuid[]))`,
    [req.currentUser.businessId, worker.Id, allowed]);
    return res.status(200).json({
      membershipId: worker.Id,
      displayName: worker.DisplayName,
      isActive: true,
      skills,
      openTasks: Number(taskRows[0].OpenTasks),
    });
  });

  router.get('/workers/:membershipId/schedule/availability', ...requireTasksView, async (req, res) => {
    if (!isUuid(req.params.membershipId)) throw AppError.badRequest('invalid_membership_id', 'The membership ID is invalid.');
    const allowed = await allowedLocations(pool, req.currentUser);
    return res.status(200).json(await getWorkerAvailability(pool, req.currentUser, req.params.membershipId, allowed));
  });

  router.put('/workers/:membershipId/schedule/availability', ...requireWorkersManage, async (req, res) => {
    if (!isUuid(req.params.membershipId)) throw AppError.badRequest('invalid_membership_id', 'The membership ID is invalid.');
    const { days } = parse(setWorkerAvailabilitySchema, req.body);
    const allowed = await allowedLocations(pool, req.currentUser);
    return res.status(200).json(await setWorkerAvailability(pool, transaction, req.currentUser, req.params.membershipId, days, allowed));
  });

  router.get('/workers/:membershipId/schedule/leave', ...requireTasksView, async (req, res) => {
    if (!isUuid(req.params.membershipId)) throw AppError.badRequest('invalid_membership_id', 'The membership ID is invalid.');
    if (typeof req.query.fromUtc !== 'string' || typeof req.query.toUtc !== 'string') {
      throw AppError.badRequest('invalid_calendar_range', 'Provide fromUtc and toUtc as UTC date-times.');
    }
    const allowed = await allowedLocations(pool, req.currentUser);
    return res.status(200).json(await listWorkerLeave(pool, req.currentUser, req.params.membershipId,
      req.query.fromUtc, req.query.toUtc, allowed));
  });

  router.post('/workers/:membershipId/schedule/leave', ...requireWorkersManage, async (req, res) => {
    if (!isUuid(req.params.membershipId)) throw AppError.badRequest('invalid_membership_id', 'The membership ID is invalid.');
    const payload = parse(workerLeaveSchema, req.body);
    const allowed = await allowedLocations(pool, req.currentUser);
    return res.status(201).json(await addWorkerLeave(pool, transaction, req.currentUser, req.params.membershipId, payload, allowed));
  });

  router.delete('/workers/:membershipId/schedule/leave/:leaveId', ...requireWorkersManage, async (req, res) => {
    if (!isUuid(req.params.membershipId)) throw AppError.badRequest('invalid_membership_id', 'The membership ID is invalid.');
    if (!isUuid(req.params.leaveId)) throw AppError.badRequest('invalid_leave_id', 'The leave ID is invalid.');
    const allowed = await allowedLocations(pool, req.currentUser);
    await cancelWorkerLeave(pool, transaction, req.currentUser, req.params.membershipId, req.params.leaveId, allowed);
    return res.status(204).end();
  });

  router.get('/workers/:membershipId/schedule/leave-requests/pending', ...requireWorkersManage, async (req, res) => {
    if (!isUuid(req.params.membershipId)) throw AppError.badRequest('invalid_membership_id', 'The membership ID is invalid.');
    return res.status(200).json(await listPendingWorkerLeave(pool, req.currentUser, await allowedLocations(pool, req.currentUser)));
  });

  router.post('/workers/:membershipId/schedule/leave-requests/:leaveId/review', ...requireWorkersManage, async (req, res) => {
    if (!isUuid(req.params.membershipId)) throw AppError.badRequest('invalid_membership_id', 'The membership ID is invalid.');
    if (!isUuid(req.params.leaveId)) throw AppError.badRequest('invalid_leave_id', 'The leave ID is invalid.');
    const payload = parse(workerLeaveReviewSchema, req.body);
    return res.status(200).json(await reviewWorkerLeave(pool, transaction, req.currentUser, req.params.leaveId,
      payload, await allowedLocations(pool, req.currentUser)));
  });

  router.get('/workers/:membershipId/schedule/calendar', ...requireTasksView, async (req, res) => {
    if (!isUuid(req.params.membershipId)) throw AppError.badRequest('invalid_membership_id', 'The membership ID is invalid.');
    if (typeof req.query.fromUtc !== 'string' || typeof req.query.toUtc !== 'string') {
      throw AppError.badRequest('invalid_calendar_range', 'Provide fromUtc and toUtc as UTC date-times.');
    }
    return res.status(200).json(await getWorkerCalendar(pool, req.currentUser, req.params.membershipId,
      req.query.fromUtc, req.query.toUtc, await allowedLocations(pool, req.currentUser)));
  });

  router.post('/my/leave-requests', ...requireTasksWork, async (req, res) => {
    const payload = parse(workerLeaveSchema, req.body);
    return res.status(201).json(await requestMyLeave(pool, transaction, req.currentUser, payload));
  });

  router.get('/tasks', ...requireTasksView, async (req, res) => {
    const page = Number.parseInt(req.query.page ?? '1', 10);
    const pageSize = Number.parseInt(req.query.pageSize ?? '25', 10);
    if (!Number.isSafeInteger(page) || page < 1 || !Number.isSafeInteger(pageSize) || pageSize < 1 || pageSize > 100) {
      throw AppError.badRequest('invalid_paging', 'Page must be positive and pageSize must be between 1 and 100.');
    }
    const allowed = await allowedLocations(pool, req.currentUser);
    const values = [req.currentUser.businessId, allowed];
    const filters = ['t."BusinessId" = $1', '($2::uuid[] IS NULL OR o."LocationId" = ANY($2::uuid[]))'];
    const statuses = ['Pending', 'Scheduled', 'InProgress', 'Paused', 'Blocked', 'Completed', 'Cancelled'];
    const priorities = ['Low', 'Normal', 'High', 'Urgent'];
    for (const [field, column, allowedValues] of [
      ['status', 't."Status"', statuses], ['priority', 't."Priority"', priorities],
    ]) {
      if (req.query[field] !== undefined) {
        if (!allowedValues.includes(String(req.query[field]))) throw AppError.badRequest(`invalid_${field}`, `The task ${field} is invalid.`);
        values.push(String(req.query[field]));
        filters.push(`${column} = $${values.length}`);
      }
    }
    for (const [field, column] of [
      ['orderId', 't."OrderId"'], ['garmentId', 't."OrderGarmentId"'], ['assigneeId', 't."AssignedMembershipId"'],
    ]) {
      if (req.query[field] !== undefined) {
        if (!isUuid(String(req.query[field]))) throw AppError.badRequest(`invalid_${field}`, `The ${field} is invalid.`);
        values.push(String(req.query[field]));
        filters.push(`${column} = $${values.length}`);
      }
    }
    for (const field of ['unassigned', 'overdue', 'eligible']) {
      if (req.query[field] !== undefined) {
        if (!['true', 'false'].includes(String(req.query[field]))) throw AppError.badRequest(`invalid_${field}`, `${field} must be true or false.`);
        const enabled = String(req.query[field]) === 'true';
        if (field === 'unassigned' && enabled) filters.push('t."AssignedMembershipId" IS NULL AND t."Status" NOT IN (\'Completed\', \'Cancelled\')');
        if (field === 'overdue' && enabled) filters.push('t."Status" NOT IN (\'Completed\', \'Cancelled\') AND t."DeadlineUtc" < now()');
        if (field === 'eligible' && enabled) filters.push(`
          t."Status" IN ('Pending', 'Scheduled') AND o."Status" IN ('Confirmed', 'InProduction')
          AND NOT EXISTS (
            SELECT 1 FROM "TaskDependencies" d JOIN "ProductionTasks" prerequisite
              ON prerequisite."BusinessId" = d."BusinessId" AND prerequisite."Id" = d."DependsOnTaskId"
            WHERE d."BusinessId" = t."BusinessId" AND d."TaskId" = t."Id"
              AND prerequisite."Status" NOT IN ('Completed', 'Cancelled')
          )`);
      }
    }
    if (req.query.stage !== undefined) {
      values.push(`%${String(req.query.stage).trim().slice(0, 80).replace(/[\\%_]/g, '\\$&')}%`);
      filters.push(`t."StageName" ILIKE $${values.length} ESCAPE E'\\\\'`);
    }
    if (req.query.dueBefore !== undefined) {
      const date = new Date(String(req.query.dueBefore));
      if (Number.isNaN(date.getTime())) throw AppError.badRequest('invalid_due_before', 'dueBefore must be a valid date-time.');
      values.push(date.toISOString());
      filters.push(`t."DeadlineUtc" IS NOT NULL AND t."DeadlineUtc" < $${values.length}::timestamptz`);
    }
    const where = filters.join(' AND ');
    const count = await pool.query(`
      SELECT count(*)::int AS "TotalCount"
      FROM "ProductionTasks" t JOIN "Orders" o ON o."BusinessId" = t."BusinessId" AND o."Id" = t."OrderId"
      WHERE ${where}`, values);
    const { rows } = await pool.query(`
      SELECT ${taskSummaryColumns}
      FROM "ProductionTasks" t
      JOIN "Orders" o ON o."BusinessId" = t."BusinessId" AND o."Id" = t."OrderId"
      JOIN "OrderGarments" g ON g."BusinessId" = t."BusinessId" AND g."Id" = t."OrderGarmentId"
      LEFT JOIN "Memberships" m ON m."BusinessId" = t."BusinessId" AND m."Id" = t."AssignedMembershipId"
      WHERE ${where}
      ORDER BY CASE t."Priority" WHEN 'Urgent' THEN 0 WHEN 'High' THEN 1 WHEN 'Normal' THEN 2 ELSE 3 END,
        t."DeadlineUtc" IS NULL, t."DeadlineUtc", t."StageOrder"
      LIMIT $${values.length + 1} OFFSET $${values.length + 2}`,
    [...values, pageSize, (page - 1) * pageSize]);
    const now = new Date();
    return res.status(200).json({
      items: rows.map((row) => taskSummary(row, now)),
      page,
      pageSize,
      totalCount: Number(count.rows[0]?.TotalCount ?? 0),
    });
  });

  router.post('/tasks', ...requireTasksManage, async (req, res) => {
    const payload = parse(addTaskSchema, req.body);
    const allowed = await allowedLocations(pool, req.currentUser);
    const taskId = await transaction(pool, (client) =>
      createProductionTask(client, req, payload.orderId, payload, allowed));
    return res.status(201).json(await loadTaskDetail(pool, req.currentUser.businessId, taskId, allowed));
  });

  router.post('/tasks/schedule/preview', ...requireTasksView, async (req, res) => {
    const payload = parse(schedulePreviewSchema, req.body);
    const allowed = await allowedLocations(pool, req.currentUser);
    return res.status(200).json(await previewShopSchedule(pool, req.currentUser, payload, allowed));
  });

  router.post('/tasks/schedule/apply', ...requireTasksManage, async (req, res) => {
    const payload = parse(scheduleApplySchema, req.body);
    const allowed = await allowedLocations(pool, req.currentUser);
    return res.status(200).json(await applyShopSchedule(pool, req.currentUser, payload, allowed, transaction));
  });

  router.get('/tasks/:id', ...requireTasksView, async (req, res) => {
    if (!isUuid(req.params.id)) throw AppError.badRequest('invalid_task_id', 'The task ID is invalid.');
    return res.status(200).json(await loadTaskDetail(pool, req.currentUser.businessId, req.params.id,
      await allowedLocations(pool, req.currentUser)));
  });

  router.put('/tasks/:id', ...requireTasksManage, async (req, res) => {
    if (!isUuid(req.params.id)) throw AppError.badRequest('invalid_task_id', 'The task ID is invalid.');
    const payload = parse(updateTaskSchema, req.body);
    const allowed = await allowedLocations(pool, req.currentUser);
    await transaction(pool, (client) =>
      updateProductionTask(client, req, req.params.id, payload, allowed));
    return res.status(200).json(await loadTaskDetail(pool, req.currentUser.businessId, req.params.id, allowed));
  });

  router.post('/tasks/:id/assign', ...requireTasksAssign, async (req, res) => {
    if (!isUuid(req.params.id)) throw AppError.badRequest('invalid_task_id', 'The task ID is invalid.');
    const payload = parse(assignTaskSchema, req.body);
    const allowed = await allowedLocations(pool, req.currentUser);
    await transaction(pool, (client) =>
      assignProductionTask(client, req, req.params.id, payload, allowed));
    return res.status(200).json(await loadTaskDetail(pool, req.currentUser.businessId, req.params.id, allowed));
  });

  router.post('/tasks/:id/unblock', ...requireTasksManage, async (req, res) => {
    if (!isUuid(req.params.id)) throw AppError.badRequest('invalid_task_id', 'The task ID is invalid.');
    const payload = parse(taskNoteSchema, req.body);
    const allowed = await allowedLocations(pool, req.currentUser);
    await transaction(pool, (client) =>
      transitionProductionTask(client, req, req.params.id, 'unblock', payload, allowed));
    return res.status(200).json(await loadTaskDetail(pool, req.currentUser.businessId, req.params.id, allowed));
  });

  router.post('/tasks/:id/cancel', ...requireTasksManage, async (req, res) => {
    if (!isUuid(req.params.id)) throw AppError.badRequest('invalid_task_id', 'The task ID is invalid.');
    const payload = parse(cancelTaskSchema, req.body);
    const allowed = await allowedLocations(pool, req.currentUser);
    await transaction(pool, (client) =>
      transitionProductionTask(client, req, req.params.id, 'cancel', payload, allowed));
    return res.status(200).json(await loadTaskDetail(pool, req.currentUser.businessId, req.params.id, allowed));
  });

  router.post('/tasks/:id/override', ...requireTasksOverride, async (req, res) => {
    if (!isUuid(req.params.id)) throw AppError.badRequest('invalid_task_id', 'The task ID is invalid.');
    const payload = parse(overrideTaskSchema, req.body);
    const allowed = await allowedLocations(pool, req.currentUser);
    await transaction(pool, (client) =>
      transitionProductionTask(client, req, req.params.id, 'override', payload, allowed));
    return res.status(200).json(await loadTaskDetail(pool, req.currentUser.businessId, req.params.id, allowed));
  });

  router.post('/orders/:orderId/tasks', ...requireTasksManage, async (req, res) => {
    if (!isUuid(req.params.orderId)) throw AppError.badRequest('invalid_order_id', 'The order ID is invalid.');
    const payload = parse(createTaskSchema, req.body);
    const allowed = await allowedLocations(pool, req.currentUser);
    const taskId = await transaction(pool, (client) =>
      createProductionTask(client, req, req.params.orderId, payload, allowed));
    return res.status(201).json(await loadTaskDetail(pool, req.currentUser.businessId, taskId, allowed));
  });

  router.post('/orders/:orderId/generate-tasks', ...requireTasksManage, async (req, res) => {
    if (!isUuid(req.params.orderId)) throw AppError.badRequest('invalid_order_id', 'The order ID is invalid.');
    const payload = parse(generateTasksSchema, req.body);
    const allowed = await allowedLocations(pool, req.currentUser);
    const result = await transaction(pool, async (client) => {
      const { rows } = await client.query(`
        SELECT "Id" FROM "Orders" WHERE "BusinessId" = $1 AND "Id" = $2
          AND ($3::uuid[] IS NULL OR "LocationId" = ANY($3::uuid[])) FOR UPDATE`,
      [req.currentUser.businessId, req.params.orderId, allowed]);
      if (!rows.length) throw AppError.notFound('order_not_found', 'Order not found.');
      const generated = await generateProductionTasks(client, req.currentUser.businessId, req.params.orderId,
        req.currentUser.userId, payload.workflowOverrides);
      await auditOrder(client, req, 'order.tasks_generated', req.params.orderId, generated);
      return generated;
    });
    return res.status(200).json(result);
  });

  router.post('/orders/:orderId/deliver', ...requireOrdersManage, async (req, res) => {
    if (!isUuid(req.params.orderId)) throw AppError.badRequest('invalid_order_id', 'The order ID is invalid.');
    const allowed = await allowedLocations(pool, req.currentUser);
    const order = await transaction(pool, async (client) => {
      const { rows } = await client.query(`
        SELECT "Status" FROM "Orders" WHERE "BusinessId" = $1 AND "Id" = $2
          AND ($3::uuid[] IS NULL OR "LocationId" = ANY($3::uuid[])) FOR UPDATE`,
      [req.currentUser.businessId, req.params.orderId, allowed]);
      if (!rows.length) throw AppError.notFound('order_not_found', 'Order not found.');
      if (rows[0].Status !== 'Ready') {
        throw AppError.conflict('invalid_transition', `Only orders that are Ready can be delivered (this one is ${rows[0].Status}). Finish the production tasks first.`);
      }
      const { rows: openTasks } = await client.query(`
        SELECT count(*)::int AS "Count" FROM "ProductionTasks"
        WHERE "BusinessId" = $1 AND "OrderId" = $2 AND "Status" NOT IN ('Completed', 'Cancelled')`,
      [req.currentUser.businessId, req.params.orderId]);
      if (Number(openTasks[0].Count) > 0) {
        throw AppError.conflict('tasks_incomplete', `${openTasks[0].Count} production task(s) are still open.`);
      }
      await client.query(`
        UPDATE "Orders" SET "Status" = 'Delivered', "DeliveredAtUtc" = now(), "UpdatedAtUtc" = now()
        WHERE "BusinessId" = $1 AND "Id" = $2`, [req.currentUser.businessId, req.params.orderId]);
      await auditOrder(client, req, 'order.delivered', req.params.orderId);
      return loadOrder(client, req.currentUser.businessId, req.params.orderId, allowed);
    });
    return res.status(200).json(order);
  });

  router.post('/order-garments/:garmentId/request-alterations', ...requireTasksManage, async (req, res) => {
    if (!isUuid(req.params.garmentId)) throw AppError.badRequest('invalid_garment_id', 'The garment ID is invalid.');
    const payload = parse(requestAlterationsSchema, req.body);
    const allowed = await allowedLocations(pool, req.currentUser);
    const taskIds = await transaction(pool, (client) =>
      requestGarmentAlterations(client, req, req.params.garmentId, payload.notes, allowed));
    const details = [];
    for (const taskId of taskIds) {
      details.push(await loadTaskDetail(pool, req.currentUser.businessId, taskId, allowed));
    }
    return res.status(200).json(details);
  });

  router.get('/my/tasks', ...requireTasksWork, async (req, res) => {
    const page = Number.parseInt(req.query.page ?? '1', 10);
    const pageSize = Number.parseInt(req.query.pageSize ?? '25', 10);
    if (!Number.isSafeInteger(page) || page < 1 || !Number.isSafeInteger(pageSize) || pageSize < 1 || pageSize > 100) {
      throw AppError.badRequest('invalid_paging', 'Page must be positive and pageSize must be between 1 and 100.');
    }
    const show = String(req.query.show ?? 'open').toLowerCase();
    if (!['open', 'all', 'completed'].includes(show)) throw AppError.badRequest('invalid_show', 'show must be open, all, or completed.');
    const { rows: membershipRows } = await pool.query(`
      SELECT "Id" FROM "Memberships" WHERE "BusinessId" = $1 AND "UserId" = $2 AND "Status" = 0`,
    [req.currentUser.businessId, req.currentUser.userId]);
    if (!membershipRows.length) throw AppError.forbidden('no_active_membership', 'Your account is not an active member of this business.');
    const membershipId = membershipRows[0].Id;
    const filters = ['t."BusinessId" = $1', 't."AssignedMembershipId" = $2'];
    if (show === 'completed') filters.push(`t."Status" = 'Completed'`);
    if (show === 'open') filters.push(`t."Status" NOT IN ('Completed', 'Cancelled')`);
    const values = [req.currentUser.businessId, membershipId];
    const where = filters.join(' AND ');
    const count = await pool.query(`SELECT count(*)::int AS "TotalCount" FROM "ProductionTasks" t WHERE ${where}`, values);
    const { rows } = await pool.query(`
      SELECT ${taskSummaryColumns}
      FROM "ProductionTasks" t
      JOIN "Orders" o ON o."BusinessId" = t."BusinessId" AND o."Id" = t."OrderId"
      JOIN "OrderGarments" g ON g."BusinessId" = t."BusinessId" AND g."Id" = t."OrderGarmentId"
      LEFT JOIN "Memberships" m ON m."BusinessId" = t."BusinessId" AND m."Id" = t."AssignedMembershipId"
      WHERE ${where}
      ORDER BY CASE t."Priority" WHEN 'Urgent' THEN 0 WHEN 'High' THEN 1 WHEN 'Normal' THEN 2 ELSE 3 END,
        t."DeadlineUtc" IS NULL, t."DeadlineUtc", t."StageOrder"
      LIMIT $3 OFFSET $4`,
    [...values, pageSize, (page - 1) * pageSize]);
    const now = new Date();
    return res.status(200).json({
      items: rows.map((row) => taskSummary(row, now)),
      page,
      pageSize,
      totalCount: Number(count.rows[0]?.TotalCount ?? 0),
    });
  });

  router.get('/my/tasks/:id', ...requireTasksWork, async (req, res) => {
    if (!isUuid(req.params.id)) throw AppError.badRequest('invalid_task_id', 'The task ID is invalid.');
    const { rows: membershipRows } = await pool.query(`
      SELECT "Id" FROM "Memberships" WHERE "BusinessId" = $1 AND "UserId" = $2 AND "Status" = 0`,
    [req.currentUser.businessId, req.currentUser.userId]);
    if (!membershipRows.length) throw AppError.forbidden('no_active_membership', 'Your account is not an active member of this business.');
    const { rows } = await pool.query(`
      SELECT 1 FROM "ProductionTasks"
      WHERE "BusinessId" = $1 AND "Id" = $2 AND "AssignedMembershipId" = $3`,
    [req.currentUser.businessId, req.params.id, membershipRows[0].Id]);
    if (!rows.length) throw AppError.notFound('task_not_found', 'Task not found.');
    return res.status(200).json(await loadTaskDetail(pool, req.currentUser.businessId,
      req.params.id, null, false));
  });

  router.post('/my/tasks/:id/start', ...requireTasksWork, async (req, res) => {
    if (!isUuid(req.params.id)) throw AppError.badRequest('invalid_task_id', 'The task ID is invalid.');
    await transaction(pool, (client) => workerTaskAction(client, req, req.params.id, 'start'));
    return res.status(200).json(await loadTaskDetail(pool, req.currentUser.businessId, req.params.id, null, false));
  });

  router.post('/my/tasks/:id/pause', ...requireTasksWork, async (req, res) => {
    if (!isUuid(req.params.id)) throw AppError.badRequest('invalid_task_id', 'The task ID is invalid.');
    const payload = parse(taskNoteSchema, req.body);
    await transaction(pool, (client) => workerTaskAction(client, req, req.params.id, 'pause', payload));
    return res.status(200).json(await loadTaskDetail(pool, req.currentUser.businessId, req.params.id, null, false));
  });

  router.post('/my/tasks/:id/resume', ...requireTasksWork, async (req, res) => {
    if (!isUuid(req.params.id)) throw AppError.badRequest('invalid_task_id', 'The task ID is invalid.');
    await transaction(pool, (client) => workerTaskAction(client, req, req.params.id, 'resume'));
    return res.status(200).json(await loadTaskDetail(pool, req.currentUser.businessId, req.params.id, null, false));
  });

  router.post('/my/tasks/:id/complete', ...requireTasksWork, async (req, res) => {
    if (!isUuid(req.params.id)) throw AppError.badRequest('invalid_task_id', 'The task ID is invalid.');
    const payload = parse(completeTaskSchema, req.body);
    await transaction(pool, (client) => workerTaskCompletion(client, req, req.params.id, payload));
    return res.status(200).json(await loadTaskDetail(pool, req.currentUser.businessId, req.params.id, null, false));
  });

  router.post('/my/tasks/:id/issues', ...requireTasksWork, async (req, res) => {
    if (!isUuid(req.params.id)) throw AppError.badRequest('invalid_task_id', 'The task ID is invalid.');
    const payload = parse(reportTaskIssueSchema, req.body);
    await transaction(pool, (client) => workerTaskIssue(client, req, req.params.id, payload));
    return res.status(200).json(await loadTaskDetail(pool, req.currentUser.businessId, req.params.id, null, false));
  });

  return router;
}
