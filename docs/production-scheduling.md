# Production scheduling — implementation status

## Current implementation

`Tailoring.Domain.Production.ProductionSchedulePlanner` is a deterministic scheduler for assigned, estimated production tasks. It uses business-local opening hours, closure dates, prerequisite dependencies, current reservations, worker-specific recurring availability, approved leave intervals, and per-worker daily workload capacity. Tasks that lack assignments/estimates, have unresolved dependencies, or cannot fit within the planning horizon are reported as unscheduled; the planner never invents estimates or silently assigns a worker.

`POST /api/tasks/schedule/preview` is tenant- and location-scoped and returns the proposed slots plus PostgreSQL task row versions without writing schedule times. `POST /api/tasks/schedule/apply` recalculates the plan, verifies the exact slot set and task versions, rejects partial/stale plans, persists the planned times in a transaction, and writes append-only task events. Existing active tasks at other locations are included as anonymous busy-time reservations, preventing a restricted manager from creating a cross-location worker overlap without revealing hidden task details.

## Worker calendars and leave

- `PUT /api/workers/{membershipId}/schedule/availability` stores exactly one entry for each weekday. Available days require a valid local-time window and a daily capacity between 1 and 1,440 minutes. Closed days must have null hours and zero capacity.
- If a worker has no saved availability, the scheduler falls back to the business opening hours for backward compatibility. Once a worker calendar is saved, missing or closed weekdays are unavailable.
- `POST /api/workers/{membershipId}/schedule/leave` adds an approved UTC absence. Overlapping approved leave is rejected; `DELETE .../leave/{leaveId}` cancels leave without deleting history.
- `GET /api/workers/{membershipId}/schedule/calendar` returns that worker's planned tasks in a bounded UTC date range, filtered by the caller's allowed locations.
- The `/workers/schedule` page provides a manager UI for individual availability, daily capacity, leave, and assigned-task calendars. Management controls are hidden for users without `workers.manage`; backend permission attributes remain authoritative.

The scheduler treats approved leave as unavailable time and does not count leave minutes as productive workload capacity. Capacity counts scheduled/reserved task minutes within each worker's local calendar day. A task is not split across shifts or dates; it must fit as one slot within the day's overlapping shop/worker window.

## Notifications and retries

Applying a schedule queues assignment emails for assigned workers with valid email addresses and queues a reminder for 24 hours before a future task start. The reminder uses the existing durable email outbox, dispatcher leases, retry backoff, max-attempt dead-letter behavior, suppression checks, and provider delivery-status webhooks. A filtered unique index on `EmailOutbox.DeduplicationKey` prevents duplicate scheduling notifications for the same task/worker/start. Pending notifications for a superseded start time are marked failed and their message bodies are purged; already-sent mail cannot be recalled. `NotificationsQueued` indicates whether at least one new email was queued, not whether a provider accepted or delivered it. Real delivery still depends on configuring a non-log provider and running the dispatcher.

## Storefront editor

The editor supports draft save with revision checks, page title/slug/visibility and SEO fields, navigation visibility, custom-page add/remove where plan entitlements permit, section add/reorder/remove/visibility/variant/content/style editing, theme configuration, a media picker for existing assets, template-switch preview/apply, custom-domain DNS verification controls, preview links, publish/unpublish, version history and rollback. The server remains authoritative for strict document validation, entitlements, and versioning. Content and style JSON controls are functional editors but are not a polished drag-and-drop canvas; uploads/deletion remain in the Media library and browser-level E2E remains follow-up work.

## Integration coverage added

- `ProductionDomainTests` covers worker availability windows, approved leave, daily capacity, unavailable weekdays, dependencies, reservations, closed dates and invalid input.
- `Phase3Tests` now includes a PostgreSQL-backed schedule preview/apply case covering read-only preview, approved leave blocking a slot, task persistence, assignment/reminder outbox rows, and stale-preview rejection, plus worker availability/leave service validation.
- Existing `Phase3Tests` cover order confirmation generating production tasks and task progression/dependency/worker restrictions.
- Existing `StorefrontTests` cover draft save, preview links, publish/version history and rollback paths.
- Existing `FinanceTests` cover backend invoice/payment/expense/credit-note workflows.

## Verification limits — do not skip

As of CI #329 (commit `7e9f6aa`, "Create MediaIsolationApiTests.cs", 2026-10-17), the backend compiles and the PostgreSQL integration suite passes (741/741), including the schedule preview/apply, availability, leave and capacity tests, and the EF model-drift gate is clean. Still unverified: DST transition behavior, live delivery of assignment/reminder emails through a real provider, and the Next production build/Vitest results for the scheduling screens beyond the last green web job (#318).

## Required release checks

1. Run backend Release build and all integration tests against PostgreSQL/Testcontainers.
2. Apply all pending migrations to a clean disposable PostgreSQL database; verify model snapshot drift and rollback paths.
3. Run web typecheck, Vitest, and Next production build; add browser E2E for editor save/preview/publish/rollback and schedule/leave management.
4. Exercise tenant and location isolation, permission/entitlement denial, concurrent schedule apply, outbox uniqueness/retries, and superseded reminder cancellation.
5. Verify real email provider delivery and delivery webhooks in staging. A log transport is not delivery evidence.
6. Do not mark production readiness until Docker/full-stack smoke, backup/restore, observability, and live external integrations have been verified.
