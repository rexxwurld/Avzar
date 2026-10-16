# Avzar roadmap work log — pass 3 (2026-10-16)

## Implemented in this pass (not yet runtime-verified)

- Added `ExpenseManager` to the finance page for listing/searching expenses, recording expenses, editing expenses with the current optimistic concurrency `version`, voiding with a reason, and creating/updating categories through existing API routes. The component uses the same-origin API client, which attaches CSRF tokens to unsafe requests.
- Added order invoice history to the order detail page using the existing invoice list endpoint filtered by `orderId`. The history is permission-gated by `invoices.view` and links to invoice details.

## Verification status

- Source-level route and DTO contracts were checked against the existing controller and application request/response types.
- `npm install --package-lock-only --save-dev @types/node@^22.12.0 --ignore-scripts --no-audit --no-fund` timed out. `package.json` and `package-lock.json` were not changed by that failed attempt; `@types/node` upgrade remains open.
- No `node_modules`, .NET SDK, or Docker are available in this working environment, so TypeScript tests/build and backend compilation were not run. Do not treat this pass as tested or production-ready.

## Still open from the handoff

- Cross-business HTTP test expansion and web unit tests for all requested modules; extract dashboard/reports inline logic.
- Worker leave request/approval workflow (current API supports manager-created approved leave and cancellation only).
- Storefront drag/drop canvas, media picker, template switching polish, editor-only iframe policy, Playwright installation and tests.
- CSV import/export for further entities, Redis queues/backplane and a real SignalR client, ownership-transfer email verification.
- Real Paystack test-mode flow/signature/idempotency verification, real email delivery, concurrency checks, customer portal verification, MAUI build/sync/conflict resolution.
- Docker Compose and real S3/ClamAV/Redis/DNS/storefront render smoke tests.

## Pass 4 — worker leave request and approval workflow

- Added `POST /api/my/leave-requests` for an authenticated business member to submit leave against their own active membership. Requests start as `Pending`; submitted intervals do not affect scheduling until approved.
- Added manager-only pending request listing and approve/reject endpoints under the worker schedule API. Approval rechecks overlap with approved leave; review actor/time/note are persisted and actions are audited.
- Extended worker schedule UI with a request form and a manager pending-requests table.
- Added migration `20261017120000_AddWorkerLeaveReview` for review metadata and updated the EF model snapshot/target model.
- This pass has not been compiled or run against PostgreSQL because the environment does not have the .NET SDK or Docker. Treat API/UI and migration as implemented but unverified until CI passes.
