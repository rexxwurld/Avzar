# Avzar Mobile + Node.js Migration Inventory

**Created:** 2026-10-17  
**Scope:** Static inspection of the supplied `Avzar-main.zip` archive.  
**Verification caveat:** The archive does not contain `.git` metadata. This report cannot establish the live branch/commit or whether the files exactly match GitHub. The inspection environment has Node.js 22 but no .NET SDK or Docker; no production service or database was contacted.

**Update 2026-10-17 (later):** the MAUI project (`mobile/Avzar.Mobile`) is no longer in the archive; Expo remains as reference only. Backend CI #329 is green (741/741). The PWA already covers customers, measurements, orders, fittings, tasks, scheduling, finance, expenses, inventory and staff (see `docs/FEATURE_INVENTORY.md`). Statements below about MAUI being retained, or about dependency installs timing out, are historical.

## Owner decision supersedes the original mobile direction (2026-10-17)

The initial release is a **vanilla HTML/CSS/JavaScript PWA**, not React Native or Expo. Node.js + Express will become the primary backend gradually, while the existing ASP.NET API and PostgreSQL data remain authoritative until parity and staging checks pass. iPhone users will use Safari/Add to Home Screen; a dedicated Android app is a possible later phase, not a launch dependency. The experimental Expo and MAUI folders remain for reference and must not be deleted as part of the initial PWA work.

## Executive decision

Do not perform a wholesale rewrite in one pass. The current codebase already contains meaningful business rules and APIs. The safe target is a staged migration: preserve the ASP.NET API as the production source of truth while the Expo app is developed; implement a Node.js compatibility layer by domain; test it against an isolated PostgreSQL database; only then plan a controlled production cutover. Never let both APIs independently write the same production tables without an explicit ownership/consistency strategy.

## Repository inventory

| Area | Evidence found | Status / implication |
|---|---|---|
| ASP.NET Core API | `backend/src/Tailoring.Api`, controllers, security, exception handling | Existing primary API. Replacing it is a real migration, not a new empty backend. |
| Application/domain layers | `Tailoring.Application`, `Tailoring.Domain` | Business rules and contracts should be mapped before translating. |
| PostgreSQL / EF Core | `Tailoring.Infrastructure/Persistence/Migrations`; model snapshot; DbContext | Preserve data and schema. Add forward-only migrations; never regenerate or reset production schema. |
| Integration tests | `backend/tests/Tailoring.IntegrationTests` | Existing regression suite is a source of truth for behavior. CI #329 (commit `7e9f6aa`, 2026-10-17) passed 741/741 tests. |
| Web app | `web/`, Next.js App Router | Includes signed-in management routes and public storefront rendering. Preserve during transition; storefront publishing still needs a web runtime unless a deliberate alternative is built. |
| MAUI app | Removed from the repository (was `mobile/Avzar.Mobile`) | Historical only. Its behavior is described in the next section for reference |
| Deployment/config | `.env.example`, `docker-compose.yml`, `.github/workflows/` | Existing configuration is ASP.NET-oriented. New Node deployment must not replace the live API before parity checks. |
| Expo app | An experimental implementation is present under `mobile/avzar-expo/`; it is not the selected initial product direction because the owner does not know React Native. Keep it as reference until its useful behavior is inventoried; the initial product is a vanilla-JavaScript PWA. |
| Node API | Present under `backend-node/` | JavaScript Express foundation and PWA host. It serves `pwa/` and provides a restricted proxy to the existing API; Node database access and business-domain routes are not migrated. The pwa-node CI job (`npm ci`, audit, tests) was green in #289 and #318 |

## Existing domain/API coverage observed

Route attributes and source files confirm the following existing API areas. This indicates code presence, not that every external integration is live or every route has end-to-end verification.

- Authentication/account: cookie auth, mobile bearer tokens, token refresh/revocation, account security, recovery endpoints, staff invitations, business registration, ownership transfer.
- Tenant/security: business membership, permission attributes, tenant-aware persistence, audit records, CSRF protection for cookie writes, mobile JWT configuration.
- Customers: list/detail/create/update/status, duplicate detection/merge, communications, fittings.
- Measurements: templates, measurement sets, revision history and comparison.
- Orders: list/detail/create/update, pricing, quote/confirm/cancel lifecycle, fitting operations, frozen measurement snapshots in the domain workflow.
- Production: workflows, task list/detail, worker actions, assignments, skills, schedules, availability and leave-related endpoints.
- Finance: invoices, payments, receipts, expenses, credit notes and reports.
- Other areas: inventory, designs/collections, media/documents, AI endpoints, subscriptions/plans/entitlements, email delivery/suppression, storefront APIs and public Next.js storefront rendering.
- Realtime: SignalR business event hub and MAUI client.

## Mobile starter assessment (historical: the MAUI project has since been removed)

`mobile/Avzar.Mobile/AvzarApi.cs` targets `POST /api/auth/token`, `/api/auth/token/refresh`, and `/api/auth/token/revoke`. The app stores access/refresh tokens with platform secure storage. `TasksPage.cs` calls `/api/my/tasks`, caches assigned tasks, and queues selected worker actions when offline. `BusinessEventsClient.cs` connects to the SignalR hub and refreshes task data after task events.

The MAUI README explicitly says that automatic retry/re-authentication on 401, conflict resolution, offline customer/measurement/order workflows, push notifications, sync telemetry, and device testing remain outstanding. Do not delete the MAUI folder until Expo replaces required functionality and the replacement has been verified.

## Key migration risks

1. **Authentication compatibility:** Existing mobile auth is a bearer-token flow, but tokens are minted by ASP.NET-specific code. The Node implementation must preserve the intended claims, expiry, refresh rotation/revocation, multi-business selection, two-factor behavior, and session semantics—or define a deliberate staged migration. Do not assume existing ASP.NET tokens can automatically be validated by Node.
2. **Tenant isolation:** Business scope is derived from authenticated identity and enforced in application/database layers. Equivalent checks and cross-tenant regression tests are mandatory in Node.
3. **Database semantics:** EF Core entity mappings, concurrency tokens, composite foreign keys, partial indexes, constraints, and migration history may not translate directly into a Prisma schema. Inspect the actual migrations and schema before selecting ORM mappings.
4. **Money and state transitions:** Order totals, payment verification, invoice lifecycle, idempotency, and subscription entitlements must retain their server-side invariants.
5. **Background work:** Email outbox, reminders, scheduling, and notifications need durable processing. BullMQ/Redis is not a drop-in replacement without retry, deduplication, recovery, and monitoring behavior.
6. **Storefronts:** A mobile-only management app does not remove the need for a public web renderer for customer-facing storefronts.
7. **External integrations:** README/PROGRESS identify several integrations that are source-implemented but not fully verified against live services. Do not mistake source presence for operational readiness.
8. **Build verification:** backend CI #329 built and tested the .NET code (741/741). The earlier web error in `web/src/app/help/[slug]/page.tsx` was fixed; the web job has been green since. This archive still has no `.git` metadata.

## Recommended implementation sequence

### Pass 0 — Inventory

- [x] Inspect the supplied archive and repository layout.
- [x] Identify existing API, database migrations, mobile starter, web management routes and public storefront renderer.
- [x] Record verification constraints and migration risks.

### Pass 1 — PWA-first JavaScript foundation (current)

- [x] Add a vanilla HTML/CSS/JavaScript PWA shell, manifest, and static-only service worker under `pwa/`.
- [x] Add sign-in, business-selection and two-factor/recovery-code UI states against the existing cookie + CSRF API contract.
- [x] Convert the parallel Express service to JavaScript and add a constrained same-origin proxy to the existing API.
- [x] Keep the ASP.NET API and PostgreSQL schema authoritative; preserve Next.js storefront, MAUI and Expo code during transition.
- [x] Dependencies install and Express tests run in CI (pwa-node job green in #318).
- [ ] Staging cookie/API checks for the deployed PWA (blocked until the API is recreated on Render).
- [x] Add PWA customer list/detail/create/edit and measurement profile creation, revision recording, and revision-history screens against the existing ASP.NET API.
- [ ] Verify these workflows against staging; add offline-safe operations (no business mutations are queued yet). The measurement comparison UI exists.

### Pass 2 — Establish a stable baseline

- [ ] Confirm the archive matches the intended GitHub commit and working tree.
- [x] Confirm the supplied ZIP already contains the fix for the previously reported `await`-inside-`.find()` TypeScript error in `web/src/app/help/[slug]/page.tsx`; rerun GitHub CI to verify the uploaded branch contains this version.
- [ ] Capture an API route/DTO/permission compatibility matrix (a domain-level ledger exists in `docs/NODE_API_COMPATIBILITY_MATRIX.md`; per-endpoint DTO and permission detail is still open).
- [ ] Establish a reproducible isolated PostgreSQL test environment in CI.
- [ ] Record a schema/constraint/index baseline before introducing a second backend.

### Deferred — dedicated Android application (not initial scope)

- [ ] Consider a dedicated Android application only after the PWA is launched and real users demonstrate a need. Do not make Expo/React Native a requirement for the initial release.

### Pass 3 — Node API compatibility foundation

- [x] Add a separate JavaScript + Express service without changing production routing.
- [ ] Add strict environment validation, health/readiness checks, request validation, structured errors/logging, and test harness.
- [ ] Build a read-only database compatibility spike against an isolated copy of the PostgreSQL schema.
- [ ] Implement and test auth/tenant context before customer endpoints.
- [ ] Migrate domain-by-domain and run the old/new contract tests against equivalent fixtures.

### Pass 5 — Domain parity and cutover

- [ ] Migrate customers and measurements, then orders and production, then finance/subscriptions, then remaining integrations.
- [ ] Do not run dual writers against the same production tables without a formal consistency design.
- [ ] Perform staging migration and reconciliation, security tests, rollback rehearsal and smoke tests.
- [ ] Cut over only after explicit evidence of parity and a tested rollback path.
- [ ] Retire the old API/MAUI project only after approval and verified replacements.

## Initial acceptance criteria

- Existing production data is untouched.
- A feature/route compatibility matrix exists before porting endpoints.
- PWA authentication uses the existing cookie + CSRF contract, and passwords/session tokens are not stored in localStorage.
- Node API tenant tests prove one business cannot read or mutate another business's records before business routes are migrated.
- No production cutover happens merely because the new service health endpoint returns 200.
- Public storefronts remain functional and hosted while the management interface becomes mobile-first.
- A dedicated Android app remains optional and deferred until the PWA is launched and evaluated.

## Evidence sources within the archive

- `README.md` — architecture, feature status, deployment and verification limitations.
- `PROGRESS.md` — historical pass notes and known unverified work.
- `mobile/Avzar.Mobile/README.md` — existing mobile scope and limitations.
- `mobile/Avzar.Mobile/AvzarApi.cs`, `TasksPage.cs`, `OfflineSyncQueue.cs`, `BusinessEventsClient.cs` — concrete mobile implementation.
- `backend/src/Tailoring.Api/Controllers/` — current endpoint surface.
- `backend/src/Tailoring.Application/Customers/Customers.cs`, `Measurements/Measurements.cs`, `Orders/Orders.cs` — current API DTOs and contracts.
- `backend/src/Tailoring.Infrastructure/Persistence/Migrations/` — EF migration history.
- `.github/workflows/ci.yml`, `.github/workflows/mobile-build-1.yml` — existing verification pipelines.
