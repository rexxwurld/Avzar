# Avzar — Master Engineering Handoff

Repository: `https://github.com/rexxwurld/Avzar.git` (primary branch historically `main`). The supplied archive does not contain `.git`, so its exact branch/HEAD/worktree cannot be verified from this handoff. Always inspect the actual checkout before pushing or merging. Preserve user work, never weaken tests, never expose secrets, use forward-only migrations, and do not deploy or run real email/payment flows without authorization.

## Direction (owner, 2026-10-17): mobile-first PWA, JavaScript-first stack
Primary product = installable PWA (`pwa/`: HTML/CSS/vanilla JS) served by Node/Express (`backend-node/`); PostgreSQL kept. The ASP.NET API (`backend/`) stays authoritative; Node only serves the PWA and proxies safe `/backend/api/*` routes and reports `/health/ready` from the upstream API (no business endpoint is migrated to Node yet). `web/` (Next.js) stays for the public storefront renderer and its existing management screens; `mobile/avzar-expo` (Expo) is reference only. The MAUI project (`mobile/Avzar.Mobile`) has been removed from the repository.

## Current status (2026-10-17) — supersedes the dated sections below

- **Latest CI evidence:** CI #329 (commit `7e9f6aa`, "Create MediaIsolationApiTests.cs", 2026-10-17): backend Release build 0 warnings / 0 errors, **741/741 tests passed** (TRX confirms 741 passed, 0 failed, 0 skipped), committed migrations match the EF model. The `web`, `pwa-node`, `migrations` and Expo jobs were last seen green in #318; their #329 results were not reviewed. The run, commit and test-count entries below (#230 to #289) are history.
- **Media isolation:** `MediaIsolationApiTests` (6 HTTP tests) now covers media read/list/open, delete, client `BusinessId` on upload and as a header, a no-permission member and anonymous access. Item 1 of "Remaining work" below is done for media; expenses, storefront, designs, collections, inventory, portal links and worker availability/leave still need their HTTP probes confirmed.
- **Data Protection keys** are stored in PostgreSQL (`PersistKeysToDbContext`, migration `AddDataProtectionKeys`). Surviving a Render restart is still unverified.
- **Repository changes:** the MAUI project is gone; the one-off workflows (`fix-lockfile.yml`, `cleanup-docs.yml`, `generate-lockfile.yml`) are gone; the root `package-lock.json` is gone; Playwright is now in `web/package.json` (config and `e2e/` exist; the staging secrets are not configured, so the e2e is skipped).
- **PWA coverage** (`pwa/app.js`, not run): sign-in with business selection and 2FA/recovery, home overview, customers and measurements (create, revise, history, compare), orders (list, detail, create/edit, quote/confirm/cancel/deliver, task generation), fittings, task board and detail, shop schedule, worker schedules and leave, finance (invoices, payments, credit notes, receipts), expenses, inventory, staff list/invite, customer-portal links, business settings and account security. Missing: storefront editor, AI studio, billing/plans, platform admin, ownership transfer and a reports page. Offline writes are not queued.
- **Deployments:** the owner deleted the `avzar-api` Render service (2026-10-17) and will recreate it (`docs/DEPLOYMENT.md`). The state of `avzar-web` and the database was not stated. The Node/PWA service is not deployed.
- Live guides: `STATUS.md` (current state), `TODO.md` (what is left). This file and `PROGRESS.md` are history.

## CI run #289 (commit `e860e1714e2a4906689f27328cfe75ba54ad8b82`, "Update Avzar from final engineering handoff") — backend and migrations FAILED (build), pwa-node and web succeeded
- **backend / migrations:** `dotnet build -c Release` failed: `OverdueTaskReminderWorker.cs(20,5): CS0246 'IConfiguration' not found` (missing `using Microsoft.Extensions.Configuration;`). Because the build failed, **no .NET tests ran** and no migration check ran for this commit. **Fix written, not yet run in CI:** the using was added. Build errors are reported per project, so more errors in the test project could appear after this one is fixed.
- **Still-unfixed from #284:** `Phase3Tests` schedule mail test (expected 2, got 3 in #284 because task assignment now queues a `task.assignment` mail). Fix written (count by template; 1 `task.assignment`, 2 `schedule.*`), not yet run.
- **pwa-node:** succeeded: `npm ci` (86 packages), `npm audit` 0 vulnerabilities, syntax checks, `node --test`; the log showed tests 1-7 passing (readiness tests included), the final summary count was cut off.
- **web:** succeeded: typecheck clean, Vitest passing (count cut off in the paste), Next 15.5.28 build compiled 14 static pages; warnings: `@types/node` peer, multiple-lockfile workspace root.
- **New warning, all builds:** transitive `MessagePack 2.5.187` advisories (NU1902 moderate, NU1903 high) in `Tailoring.Api` and the test project; source not yet identified.
- **Mobile build workflow (commit `ed00525`, run #5):** iOS and Android MAUI builds failed at restore with `NU1015`: `Microsoft.AspNetCore.SignalR.Client` had no version because `mobile/` is outside `backend/` and does not use the central package file. **Fix written, not yet run:** `Version="10.0.0"` (same as `backend/Directory.Packages.props`) and an explicit `Microsoft.Maui.Controls 10.0.110` reference (MA002). Later MAUI build errors are possible.
- **Expo mobile checks (workflow "Mobile + Node migration foundation", commit `e860e17`):** failed after ~25s with the later steps skipped, which points to the `npm install` step in `mobile/avzar-expo`; the error text was not in the paste and the npm registry is unreachable here, so the cause is unconfirmed. No lockfile is committed there. Needs the expanded install log.
- Not covered: browser/device PWA tests, Docker Compose smoke (owner decision), real email/S3/ClamAV/Redis/DNS/Paystack/OpenAI/SignalR.

## CI run #254 (commit `8f18d45bfe57b41513fccf239ca3e8426545c7b8`, "Add files via upload") — all three jobs succeeded (latest evidence)

- **backend (2m13s):** TRX artifact: **710 passed / 0 failed / 0 skipped / 0 errors** (704 earlier + 6 new `CrossTenantApiTests`, all six passed). The job's step output was collapsed, so the drift-gate line was not seen for this run; the migrations job's Release build showed 0 warnings / 0 errors.
- **web (38s):** `npm ci` ok, 0 vulnerabilities; the later steps (typecheck, Vitest, build) were collapsed, so counts for #254 were NOT seen (162 Vitest tests in 9 files were seen in #243). `@types/node@22.10.7` peer warning still present.
- **migrations (1m06s):** clean PostgreSQL 17.11; applied from `InitialSchema` (visible excerpt lists the first five; the rest of the log was collapsed); schema counts: **73 tables, 38 check constraints, 125 foreign keys, 9 partial indexes**; the "financial/audit FKs that cascade (expect none)" check printed no rows. The `__EFMigrationsHistory does not exist` error line is the expected first-run probe, not a failure. The repo has 13 migrations; do not claim all 13 were seen applying in this log.
- **Isolation:** the six HTTP cross-business tests (customer, order, invoice, payment, receipt, task; reads, PDFs, lists, writes, foreign `customerId` on create, `businessId` query/header ignored) passed, so no gap was found for those resources. Not yet covered over HTTP: expenses, storefront/media/designs/collections, inventory, portal links, worker availability/leave.
- Not covered: Playwright/browser E2E, Docker Compose smoke (owner decision: not run), real email/S3/ClamAV/Redis/DNS/Paystack/OpenAI/SignalR/MAUI. No web tests yet for storefront, finance, inventory, worker schedule, reports, billing, AI studio or portal.

## Previous CI run #243 (commit `1409b30`) — all three jobs succeeded
- backend 704/704, 0 warnings/errors, model-drift gate clean; web 162 Vitest tests (9 files), typecheck clean, Next 15.5.28 build compiled (route table truncated after `/orders/new`), 0 audit vulnerabilities; migrations succeeded (counts not seen).

## Previous CI run #238 (commit `40306ac`) — all three jobs succeeded

backend, web and migrations all reported "succeeded" on the run page (backend 2m13s, web 50s, migrations 1m32s). The pasted logs had collapsed steps, so exact test counts, the Vitest/build summaries and the migration schema counts were NOT seen; do not quote numbers for #238. Runs #231–#237 failed on, in order: missing `using` directives, `decimal.IsFinite`, a strict-index TS error, a syntax error from a manual edit, a missing `Microsoft.AspNetCore.Authorization` using in `CustomerPortalController`, and relationship blocks placed before their principal entity in `AppDbContextModelSnapshot.cs` and three Designer files (`AddInventory`, `AddCustomerPortalGrants`, `AddSubscriptionPayments`). All were fixed; the Designer/snapshot fix only reordered model descriptions, no migration SQL changed.
Open items: `@types/node` 22.10.7 is below Vite 8's optional peer range (warning only; needs a lockfile regeneration with registry access). Resolved in the current archive by static inspection (not compiled): `CustomerPortalController` no longer declares the unused `tenant` parameter (CS9113), and the generate-migration workflow default name was changed from the already-existing `AddLastTotpStep` to `AddNextChange`.

## Latest supplied verification

CI run #230, commit `c349faf9023a9a0c1c6d5e7ddb5950c76016953c`:
- Backend: Release build passed with 0 warnings/0 errors; 693/693 tests passed; EF model-drift gate reported committed migrations match the model.
- Web: job succeeded; `npm ci` and `npm audit` completed, reporting 0 vulnerabilities. The supplied excerpt does not show the Vitest count or typecheck/build summaries. `npm ci` warned that `@types/node@22.10.7` is below Vite 8.3.4's declared `>=22.12.0` peer range.
- Migration job: output was not supplied for #230. Historical #207 successfully applied eight migrations to clean PostgreSQL 17.11; do not attribute that clean-database result to #230.
- Local verification in this session: no .NET SDK or Docker. `npm ci --no-audit --no-fund` timed out; `npm test -- --run` failed because `vitest` is not installed, and `npm run typecheck` emitted missing-module/type errors because dependencies were absent. TypeScript syntax transpilation passed for 19 changed/relevant TS/TSX files, and strict compilation plus eight standalone assertions passed for the production helper module. These checks do not replace Vitest, the full typecheck or a production build.

## Current web implementation in the supplied archive

- Auth: login, register, accept invitation.
- Staff: team, roles and locations.
- Customers: list/create/detail/edit and communication/merge features.
- Orders: searchable/filterable list, read-only detail, create/edit form, and quote/confirm/cancel actions.
- Production: partial staff UI at `/tasks` and `/tasks/[id]`, shop schedule preview/apply at `/tasks/schedule`, and worker calendars at `/workers/schedule`. Managers get task filters and actions; workers get assigned task actions. Scheduling source now includes worker availability, approved leave, daily capacity, calendar endpoints, and queued/deduplicated assignment/reminder emails. Backend compilation, PostgreSQL integration and delivery verification remain outstanding.
- Public storefront renderer and token preview exist. `/storefront` supports draft editing, custom pages under entitlements, page slug/SEO/navigation controls, section content/style/variant/visibility and ordering, theme JSON, preview, publish/unpublish and rollback. It is not yet a polished drag-and-drop visual builder; media upload/delete remain in the Media library and browser E2E remains open.
- Order creation sends an idempotency key through the same-origin proxy allow-list; totals are server-calculated. Edit uses optimistic concurrency and only supports Draft/Quoted. Quote/confirm/cancel transitions are enforced by the API. The UI does not expose costs/margins.
- **Also already built (found by static inspection of the archive; compiled/typechecked by CI #243 where the build output showed it, not browser-tested):** dashboard with 30-day report tiles and "my assigned work" (`/dashboard`); date-range reports page with daily trend (`/reports`, `reports.view`); inventory UI (`/inventory`, `InventoryManager`); AI studio UI (`/ai-studio`); subscription billing UI (`/settings/billing`, `BillingManager`) and platform plan admin (`/platform-admin/plans`); customer portal (`/customer-portal` + `CustomerPortalClient`, staff "send link" button on customer detail); customer CSV import/export tools (`CustomerCsvTools`). Expenses have a summary on `/finance` only (API CRUD exists, no expense UI). Backend extras present: Redis fixed-window limiter/counter/health check, Redis distributed cache wired for reports (memory fallback), SignalR hub `BusinessEventsHub` (`AddSignalR()` with no Redis backplane and no web/mobile client consuming it), DB-backed email outbox with leases (`FOR UPDATE SKIP LOCKED`), retry backoff and lease expiry (this is the existing job-recovery mechanism for email), `OrphanCleanupWorker`, ownership transfer with 2FA check and post-transfer notification emails, MAUI starter project under `mobile/`.
- Web test coverage: 9 Vitest files (links, proxy, orders, team, production, customers, problem, nav, business-schedule). None for storefront, finance, inventory, worker schedule, reports, billing, AI studio or the portal.

## Remaining work and constraints

Decision (project owner): **no Docker Compose / full-stack smoke test will be run.** Consequence: Compose startup, real email/S3/ClamAV/Redis/DNS/Paystack/OpenAI/SignalR behavior and storefront publish-to-render stay **Not verified**, and Avzar must not be described as production-ready. Rely on CI, integration tests (Testcontainers/PostgreSQL) and unit tests instead.

Done / no longer open: order UI + production dashboard now pass CI #243 (Vitest, typecheck, build; only the route table was truncated); migrations job succeeded in #243 (counts not seen); Redis report cache, email retry/lease recovery, ownership-transfer notification emails and customer CSV exist in code.

Open, in suggested order:
1. [`CrossTenantApiTests.cs` passed in #254: 710/710.] Extend HTTP cross-business tests to expenses, storefront/media/designs/collections, inventory, portal links, worker availability/leave.
2. Fix `@types/node` (>=22.12.0) + regenerate lockfile (needs registry access).
3. Web unit tests for the untested areas listed above (extract pure helpers where logic is inline, e.g. `reports/page.tsx` and `dashboard/page.tsx` hold their logic inline).
4. Finance UI gaps: expense and category CRUD, invoice draft editing, order invoice history.
5. Storefront editor: drag-and-drop canvas, media upload picker, polished template-switch flow, narrow preview CSP for the editor iframe, Playwright publish/rollback tests (Playwright is not yet in `web/package.json`; needs a running stack, so cannot be verified without the smoke environment).
6. Worker leave-request workflow (UI for requesting; approval flow); scheduling notification delivery depends on a real email provider (unverified).
7. Backend gaps still real: CSV import/export beyond customers; Redis queues (none; email uses a DB outbox) and SignalR Redis backplane + a web/mobile consumer; ownership-transfer email *verification* step (only notifications exist); email-suppression admin UI and webhook rate limiting; Data Protection key persistence on Render.
8. Billing: Paystack checkout/webhook code exists; a verified end-to-end payment, signature and idempotency check has not been done. MAUI: starter only; real build, sync and conflict resolution are not done.

## Database and security rules

- Keep tenant isolation, server-side permissions, CSRF, secure cookies, financial idempotency/concurrency, audit records and upload validation.
- Never trust client-supplied `BusinessId`; do not expose pricing costs/margins without `pricing.view`.
- Never rewrite/delete `InitialSchema` or historical migrations. Add forward migrations only and verify migration SQL/model snapshot.
- Public storefront CSP uses `frame-ancestors 'none'`; editor preview needs a narrowly scoped route policy, not a global weakening. Nonce CSP makes public pages dynamically rendered and affects CDN caching.

## Git and verification state

- Archive inspected: `/mnt/data/Avzar-main (2).zip`; no `.git` directory was included.
- Current session working copy for the latest increment: `/mnt/data/avzar-recovered/work/Avzar-main`. The archive has no `.git` metadata, so branch/commit/worktree status cannot be established. Changes have not been committed, pushed, merged or deployed.
- No production database, customer email or payment action was performed.
- Latest working-copy increment adds `/tasks` and `/tasks/[id]` manager/worker views and actions, plus query helper tests. `npm ci --no-audit --no-fund` timed out; Vitest and the full Next build remain blocked by unavailable dependencies. Changes remain uncommitted/unpushed.
- Next action: run CI after each increment and fix every compiler/test/migration-drift finding. No Docker smoke test is planned (owner decision); do not merge/deploy until CI passes, and do not claim production readiness.

## Latest working-copy increment: storefront editor slice
- Added `/storefront` plus `StorefrontManager`: create/select storefronts, edit draft page titles/visibility, section visibility/variant/content, add preset sections, reorder/remove sections, and theme JSON; save with the expected draft revision; private preview; publish/unpublish; version history/rollback; contact/settings updates. Displays server-returned plan issues and contrast warnings. Storefront navigation is enabled for `storefront.view` users.
- This is not yet the complete visual builder: section props/theme use JSON editors; no polished drag/drop canvas, media picker, template-switch preview flow, custom pages/domain UI, or browser e2e tests yet.
- Local verification: npm CI install timed out; no local Vitest/full typecheck/Next build. CI #230 predates this change. Run full web CI on the combined ZIP before treating the slice as verified. No backend or migration changed.
- Latest ZIP from this work session should include this increment; no commit, push, merge, or deployment was performed. The supplied archive still has no `.git` metadata.

## Latest working-copy increment: business schedule settings
- Added `/settings` for users with `business.settings`: weekly opening hours for all seven weekdays and named closure/holiday dates. It uses the existing `/api/business/schedule` endpoints and displays the configured business time zone. Navigation is enabled for authorized users.
- This configures business hours only; it does not implement the missing production scheduler or notifications/reminders.
- Local npm dependency installation fails while resolving the npm registry (`EAI_AGAIN`). Syntax transpilation and a focused TypeScript check with temporary dependency stubs passed; full Vitest, the actual project typecheck and Next production build remain unverified. No backend or migration changed.

## Latest working-copy increment: finance UI
- `/finance` now shows invoice search/filtering, pending payment claims and an expense summary based on the viewer's permissions. `/finance/invoices/new` creates idempotent free-form draft invoices. `/finance/invoices/[id]` exposes invoice lifecycle/credit-note actions, payment recording and verify/reject/reverse actions, and document links. Order details can create Full/Deposit/Balance draft invoices for eligible orders.
- The finance UI is still partial: expense/category CRUD, invoice draft editing, order invoice history and e2e coverage are outstanding. PDF downloads depend on the server's QuestPDF licence configuration.
- Verification: syntax transpilation passed for 98 TypeScript files; focused typecheck with temporary dependency stubs passed; six standalone schedule-helper assertions passed. npm registry DNS (`EAI_AGAIN`) prevents `npm ci`, so real Vitest, full typecheck and Next build have not run. CI #230 predates these changes.


## Historical checkpoint: first-pass scheduling domain algorithm (superseded by latest increment below)
- Added `backend/src/Tailoring.Domain/Production/ProductionSchedulePlanner.cs` and three cases in `backend/tests/Tailoring.IntegrationTests/ProductionDomainTests.cs`. It schedules assigned, estimated tasks within business hours, skips closure dates, respects included prerequisites and existing worker reservations, and reports unschedulable tasks rather than inventing values.
- Added `docs/production-scheduling.md`; updated `PROGRESS.md` and `docs/architecture.md`. This is pure domain logic only: no API, persistence, individual leave/availability, notifications or schedule UI yet.
- No migration or external integration changed. No .NET SDK is installed, so compilation and tests were not run. This increment is **not verified** and must be checked in CI before integration.
- Added `POST /api/tasks/schedule/preview`, protected by `tasks.view` and the `scheduling.shop` entitlement. The service reads tenant-scoped open tasks, dependencies, weekly hours, holidays and in-progress reservations, applies location scope, and returns scheduled/unscheduled results without writing to task records.
- Shop scheduling now has preview and apply endpoints. Apply is transaction-based, checks task row versions and exact preview slots, rejects stale or partial plans, and records task events. Worker availability/leave, durable notifications, mobile calendar, and HTTP authorization/tenant-isolation tests remain outstanding. Backend compilation/tests are blocked because the .NET SDK is unavailable; do not treat this code as verified.


### Historical scheduling UI checkpoint (superseded by latest increment below)

The shop schedule screen is available at `/tasks/schedule` and appears in navigation for `tasks.view`. It submits to the preview API, displays slots and unscheduled reasons, and offers an Apply action only when every candidate has a slot. Apply sends both the row-version map and exact proposed slots; the backend recalculates and rejects stale plans before persisting. Worker notifications remain unavailable. Build/test verification is still outstanding because dependencies and SDKs are unavailable in this environment.

### Historical shop schedule apply checkpoint (2026-10-16; superseded by latest increment below)
- Added `POST /api/tasks/schedule/apply`, gated by `tasks.manage` and `scheduling.shop` entitlement.
- Preview responses now include task row versions. Apply recalculates server-side, rejects stale candidate sets/versions and partial schedules, then saves planned times transactionally with `schedule_applied` task events.
- The web schedule screen now offers Apply only when every candidate is schedulable and submits the preview's version map; stale plans must be regenerated.
- Notifications remain unimplemented and the API reports `notificationsQueued: false`. No worker leave/capacity model or mobile calendar yet.
- Verification remains incomplete: .NET SDK and npm dependencies are unavailable in this environment; no backend compile/integration tests or full web build/tests were run for this increment.


## Historical scheduling cross-location conflict fix (2026-10-16; retained as audit trail)
- The schedule preview now treats existing `Scheduled`, `InProgress`, and `Paused` tasks outside the current candidate set as busy-time reservations. This closes a cross-location overlap hole when a location-restricted manager plans work for a worker who is also scheduled at another location. The restricted preview uses only the other task's worker/time range; it does not return hidden task identifiers or details.
- The project archive still has no Git metadata. This change has not been committed, pushed, merged, deployed, compiled or run against PostgreSQL. The environment lacks the .NET SDK and Docker; `npm ci --no-audit --no-fund` timed out and the web dependencies are unavailable. Run the full CI pipeline before merge.
- Remaining scheduling gaps are still open: worker availability/leave and capacity rules, durable assignment/schedule notifications, schedule publication/versioning, calendar/day views, HTTP authorization and location-scope regression tests, and mobile calendar support.
- Security follow-up in the same increment: `ShopScheduleService` now requires `ILocationScope` instead of an optional nullable dependency, preventing accidental tenant-wide scheduling if the location-scope registration is absent. This is source-level only and requires CI verification of DI and authorization/location-scope tests.


## Latest increment — worker scheduling, reminders, storefront editing (2026-10-16)
- Source now includes persisted worker weekly availability/daily capacity and approved leave, location-filtered worker calendars, a `/workers/schedule` UI, and planner integration for availability/leave/capacity. No saved worker calendar means the scheduler falls back to shop hours; saved calendar days not marked available are unavailable. Leave is UTC and is treated as unavailable time.
- Schedule apply queues deduplicated assignment email and future 24-hour reminder rows in the existing durable outbox. Pending notifications for old slot times are cancelled when a new schedule supersedes them. Provider acceptance/delivery still depends on configuring a real email provider; `notificationsQueued` is not delivery confirmation.
- Storefront editor now exposes custom page add/remove under entitlements, slug and SEO edits, navigation visibility, section style JSON, existing-media selection for supported image sections/logo, template-switch preview/apply, and custom-domain DNS verification controls. It remains a structured-data editor, not a polished drag-and-drop canvas; uploads/deletion remain in the Media library.
- New forward migrations: `20261016230000_AddEmailDeduplication`, `20261016233000_AddWorkerAvailabilityAndLeave`. Never rewrite historical migrations.
- Added planner tests and PostgreSQL integration cases for schedule preview/apply, leave, availability, capacity, notification queue rows, and stale preview rejection. Existing order-to-production, storefront and finance integration tests are still the relevant end-to-end suites.
- **Release blocker:** this workspace has no .NET SDK/Docker and cannot reach the npm registry. No backend compile, PostgreSQL integration test run, migration application/model drift check, Vitest, web typecheck or Next production build was possible. Run all CI jobs and apply migrations to disposable PostgreSQL before merge/deployment; do not treat source changes as verified. No push, merge or deployment was performed.
