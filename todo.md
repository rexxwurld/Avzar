# Avzar: C# to Node.js migration todo

Target: everything runs from `backend-node/` (Express, pg, Zod, Argon2id, ESM, Vitest). `backend/` (C#) is a temporary reference and is removed only in slice 12.

Source of truth for routes: `backend-node/migration/endpoint-inventory.json` (245 endpoints, all `todo` at the start). Mark each entry `done` only when its Node route and tests exist and pass.

Rules for every slice:
- [ ] Read the C# controller, services and entities for the slice before writing code (the groupings below come from controller names, not from reading the source yet).
- [ ] Keep HTTP method, path, request/response shape, status codes and validation behaviour.
- [ ] Parameterized SQL only; explicit transactions for atomic changes.
- [ ] Business and location isolation enforced server-side; never trust client-supplied IDs, roles or entitlements.
- [ ] Tests: success, validation failure, unauthenticated, forbidden, cross-business, cross-location.
- [ ] No schema change unless unavoidable; if needed, a new forward-only SQL migration (never edit `0001_baseline.sql`).
- [ ] Update inventory status; run `npm ci && npm test` and record the real result.

## Slice 0: Foundation (no endpoints)
- [x] Add dependencies: pg, argon2, pino, cookie-parser or equivalent, express-rate-limit, helmet, vitest, supertest; commit lockfile
- [x] Env validation for all vars the C# app read (DB, JWT, cookie, CORS, storage, email, AI, billing, Redis)
- [x] pg pool, `withTransaction` helper, graceful shutdown
- [x] Error/validation middleware, request IDs, log redaction
- [x] Security headers, CORS allowlist, rate-limit presets (`auth` and others from the inventory)
- [x] Migration runner wired to `migrations/` with status tracking (exists, verify against a real PostgreSQL)
- [x] Test harness against a real PostgreSQL (CI service container, not Docker smoke tests)
- [x] Health: `/health/live`, `/health/ready` checking the DB instead of the C# API
- [x] Keep PWA static hosting; stop depending on the proxy for readiness

## Slice 1: Auth and accounts (26 endpoints)
AuthController 7, MobileAuthController 3, AccountController 11, AccountRecoveryController 3, OwnershipController 2
- [x] Register, login, logout, refresh, cookie sessions, CSRF double-submit
- [x] Mobile bearer-token flow; no long-lived tokens in localStorage
- [x] Argon2id hashing; check compatibility with existing password hashes in the DB (PBKDF2/Identity format?) and add verify-and-rehash if needed
- [x] Email confirmation, password reset, account management, ownership transfer (partial implementation: forgot/reset and password-change/account security routes are in Node and tested; verification flows and broader account actions remain pending)
- [x] Session revocation and lockout rules

## Slice 2: Tenancy, staff and access (17 endpoints)
StaffController 6, RolesController 4, LocationsController 2, WorkersController 2, AuditController 1, BrandingController 2
- [x] Permission model and role/permission checks as middleware
- [x] Business and location scoping helper used by all later slices
- [x] Staff invites and role assignment, locations, workers
- [x] Audit log writes (shared service used by every later slice) and read endpoint
- [x] Branding

## Current verified progress
- [x] Node foundation and health checks are active and tested.
- [x] Mobile auth/token/session flows are implemented and passing in the Node backend tests.
- [x] Account security and password-change flows are implemented and passing in the Node backend tests.
- [x] Business settings, branding, and schedule routes are implemented with permission gating and passing Node tests.
- [x] Staff, roles, locations, and audit access routes are implemented with permission gating and passing Node tests.
- [x] Customers, customer communications, measurement templates, and measurement set routes are implemented with permission gating and passing Node tests.
- [x] Catalog, pricing, and inventory routes are implemented with permission gating and passing Node tests.
- [x] Orders, workflows, fittings, and task routes are implemented with permission gating and passing Node tests.
- [x] Finance routes for invoices, payments, and expenses are implemented with permission gating and passing Node tests.
- [ ] Full endpoint parity with the legacy C# backend is not yet complete.
- [ ] The remaining slices above are still pending implementation and verification.

## Slice 3: Customers and measurements (21 endpoints)
CustomersController 10, CustomerImportController 3, SearchController 1, MeasurementTemplatesController 4, MeasurementSetsController 3
- [x] CRUD, search, duplicate detection
- [x] Customer import (CSV) with validation and limits
- [x] Measurement templates, sets and revisions
- [x] Tests for duplicate detection and revision history

## Slice 4: Catalog, pricing and inventory (33 endpoints)
DesignsController 8, DesignCategoriesController 4, CollectionsController 7, PricingController 3, InventoryController 11
- [x] Designs, categories, collections
- [x] Pricing rules
- [x] Inventory items, stock movements, concurrency-safe adjustments

## Slice 5: Orders (20 endpoints)
OrdersController 8, FittingsController 3, ReceiptsController 1, WorkflowsController 4, OrderProductionController 4
- [x] Orders and items, quotation as order in Quoted status, status transitions
- [x] Idempotency keys
- [x] Fittings, receipts, workflows
- [x] Order production start and task generation hook (feeds slice 6)

## Slice 6: Tasks and scheduling (29 endpoints)
TasksController 9, MyTasksController 7, WorkerScheduleController 8, BusinessScheduleController 4, MyLeaveRequestsController 1
- [x] Task CRUD, assignment, "my tasks" for workers
- [x] Daily work schedule per worker and overall shop schedule
- [x] Leave requests
- [x] Task generation tests (idempotent, transactional)

## Slice 7: Finance (27 endpoints)
InvoicesController 10, PaymentsController 6, ExpensesController 10, ReportsController 1
- [x] Invoices (a payment applies to one invoice), payments, expenses
- [x] Reports
- [x] Money arithmetic without floats; transaction and uniqueness tests

## Slice 8: Media, documents and PDF (19 endpoints)
MediaController 6, PublicMediaController 1, DocumentsController 5, PdfController 7
- [ ] Upload type, size and content sniffing; storage abstraction (local and S3-compatible, matching the C# options)
- [ ] Image processing (ImageSharp replacement, e.g. sharp) for variants
- [ ] Documents
- [ ] PDF generation (QuestPDF replacement; pick a Node PDF library and match output fields)

## Slice 9: Storefront, public site and portal (42 endpoints)
StorefrontsController 23, PublicSiteController 11, InquiriesController 5, CustomerPortalController 3
- [ ] Storefront JSON document per draft/version with the same validation rules (port the Domain/Storefront validators)
- [ ] Publish, versions, rollback
- [ ] Public rendering endpoints used by `web/`, sitemap and robots data
- [ ] Inquiries, customer portal
- [ ] Custom domains stay off in default plans

## Slice 10: Plans, billing and AI (9 endpoints)
BillingController 2, SubscriptionBillingController 2, PlatformPlanAdminController 2, AiController 3
- [ ] Plans Free/Starter/Professional/Business (NGN, DB-editable) and server-side entitlement checks
- [ ] Subscription billing provider integration and webhooks (needs credentials to verify)
- [ ] AI provider abstraction and usage limits

## Slice 11: Email, webhooks, background jobs, real-time (2 endpoints plus non-HTTP)
EmailSuppressionAdminController 1, EmailWebhooksController 1
- [ ] Inspect `Infrastructure/Coordination`, hosted services and any SignalR hubs first, then list them here
- [ ] Email sender, templates, suppression list, provider webhook signature verification
- [ ] Job queue with retries, idempotency, failure handling (Redis-backed only if the C# code used Redis)
- [ ] Recurring jobs, notifications, real-time events (WebSocket or Socket.IO) with tests

## Slice 12: Frontend compatibility and cutover
- [ ] Diff API usage in `web/`, `pwa/`, `mobile/avzar-expo/` against the Node routes; fix only what breaks
- [ ] Remove the `/backend` proxy and `API_URL` from `backend-node/`
- [ ] Port or retire each C# test (`backend/tests`) with a Node equivalent
- [ ] CI: remove .NET jobs; add Node install, lint/format if configured, tests, production startup check
- [ ] Update `.env.example`, `docker-compose.yml` and deployment files; remove ASP.NET and EF variables
- [ ] Search the repo for stale references to the C# API and .NET
- [ ] Confirm every inventory entry is `done`, then delete `backend/`
- [ ] No Docker Compose smoke test until the dashboard and editor frontend work is done

## Known blockers
- This sandbox has no npm network access and no .NET runtime, so dependency install and tests must run on your side or in CI.
- Provider integrations (email, billing, AI, storage) need real credentials to verify.
