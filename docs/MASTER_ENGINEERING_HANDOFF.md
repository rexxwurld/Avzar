# Avzar — Master Engineering Handoff

Repository: `https://github.com/rexxwurld/Avzar.git` (primary branch historically `main`). The supplied archive does not contain `.git`, so its exact branch/HEAD/worktree cannot be verified from this handoff. Always inspect the actual checkout before pushing or merging. Preserve user work, never weaken tests, never expose secrets, use forward-only migrations, and do not deploy or run real email/payment flows without authorization.

## CI run #243 (commit `1409b30b6a424dac763b0a5b1fa9c9dd57a3b7da`, "Update CustomerPortalController.cs") — all three jobs succeeded (latest evidence)

- **backend (2m13s):** Release build 0 warnings / 0 errors; **704 passed / 0 failed / 0 skipped**; EF model-drift gate: "Committed migrations match the model"; `backend-test-results` artifact uploaded (TRX counters agree: 704/704).
- **web (48s):** `npm ci` + `npm audit` 0 vulnerabilities; `tsc --noEmit` clean; **Vitest 162 passed in 9 files** (links, proxy, orders, team, production, customers, problem, nav, business-schedule); `next build` compiled successfully on Next.js 15.5.28. The pasted route table was truncated after `/orders/new`, so `/tasks`, `/tasks/schedule`, `/workers/schedule`, `/storefront`, `/settings` routes were NOT seen in the output. Still-open warning: `@types/node@22.10.7` below Vite 8.3.4's peer range (needs lockfile regeneration with registry access).
- **migrations (1m22s):** job succeeded (clean PostgreSQL service container, licence-key guard passed), but step output was collapsed: applied migration count and schema counts NOT seen.
- Not covered by any of this: Playwright/browser E2E, Docker Compose smoke, real email/S3/ClamAV/Redis/DNS/Paystack/OpenAI/SignalR/MAUI. No web tests exist yet for storefront, finance, inventory, workers-schedule or portal helpers.
- Whether #243's commit equals the working copy in the latest ZIP (which includes a workflow-default edit and doc edits) is unknown (no `.git` in the archive).

## Latest increment (not yet run in CI): cross-business HTTP isolation tests
- Added `backend/tests/Tailoring.IntegrationTests/CrossTenantApiTests.cs` (5 tests): the owner of business B probes business A's customer, order, invoice, payment, receipt and task ids (reads, PDF/job-sheet routes, lists, writes: void/pay/reverse/reject/cancel/generate-tasks), expects 404 identical to a missing id, confirms A's data is unchanged, refuses a foreign `customerId` on order/invoice create, and shows a `businessId` query/header does not change tenancy. Control reads prove the routes are valid for the real owner.
- **Status: Written, NOT compiled or run** (no .NET SDK here). Expected backend total after CI: 709 if all five pass (704 + 5). If a probe returns a non-404 the finding is a real isolation gap (or a wrong route/status assumption): investigate, do not weaken the assertion.
- Remaining isolation gaps to cover next: expenses, storefront/media/designs/collections, inventory and customer-portal endpoints, and worker availability/leave endpoints over HTTP.

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

## Remaining work and constraints

1. Verify the order UI and production dashboard together in CI: Vitest, TypeScript, Next production build, and route output including `/orders/new`, `/orders/[id]`, `/orders/[id]/edit`, `/tasks`.
2. Review/fix the Node types peer warning by upgrading `@types/node` to a compatible Node 22 version (`>=22.12.0`) when package dependencies are next changed; update the lockfile and rerun CI.
3. Confirm the #230 migrations job and whether the staff/customer web slices are integrated in `main` from GitHub, because the archive has no Git metadata.
4. Verify the new worker calendar and storefront editing controls in browser E2E; continue media-picker/template-switch UI and worker leave request workflows after the backend and migration checks pass.
5. Remaining infrastructure gaps to verify against code/tests: Redis caching/queues and SignalR backplane, background-job recovery, CSV import/export beyond customers, email-suppression admin UI/webhook rate limiting, real-provider email, real S3/ClamAV/Redis/DNS/payment/push/AI integrations, mobile builds and Data Protection persistence on Render.
6. Once dependency access and a .NET SDK/Docker environment are available, run Docker Compose/full-stack smoke; the dashboard and initial storefront editor now exist but remain partial.

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
- Next action: run CI for this complete increment, fix every compiler/test/migration-drift finding, then run PostgreSQL migration and end-to-end smoke checks. Do not merge/deploy until those pass.

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
