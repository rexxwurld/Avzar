# AVZAR: Master Engineering Checklist

Single source of truth for unfinished work. `STATUS.md` holds the short current state and points here.

**Baseline:** CI #320, 17 October 2026. Backend Release build 0 warnings / 0 errors, **735/735 tests**, committed migrations match the EF model (#319). web, pwa-node, migrations and Expo mobile checks green (#318). MessagePack advisories cleared (#320).

## Rules

- Source code and the latest CI results take precedence over documentation.
- Do not modify `InitialSchema`; forward-only migrations.
- Never trust a client-supplied `BusinessId`; enforce tenant isolation on the server.
- Do not weaken or delete tests to make CI pass.
- Run CI after every meaningful increment and record the run.
- A feature is not complete because files or interfaces exist.
- Docker smoke testing is excluded by the owner's decision.
- Mark an item DONE only after implementation and its verification have passed.
- A green CI run does not prove production email, payments, S3, ClamAV, DNS, push or other external integrations work.

## Status legend

`DONE` = done and verified, evidence recorded · `UNVERIFIED` = code exists, required test or real-environment check has not passed · `NOT IMPL` = functionality (or the task) does not exist yet · `IN PROGRESS` · `BLOCKED` = needs credentials, a staging environment, an external service or a decision · `OOS` = out of scope by owner decision

Statuses were assigned from CI results, the repo docs and keyword searches of the code. A trailing `?` means "appears in code, behaviour not confirmed". Re-check an item before relying on it.

---

## 0. Required before first real users

Everything else can follow. These cannot.

- [ ] `BLOCKED` Persistent Data Protection keys on Render, verified across restarts and instances (section 12)
- [ ] `BLOCKED` Real email provider connected; verification, reset and notification emails tested (section 7)
- [ ] `BLOCKED` Paystack test-mode checkout, webhook signature, replay and idempotency tested (section 7)
- [ ] `NOT IMPL` HTTP cross-business isolation tests for media upload, read and delete (section 2)
- [ ] `BLOCKED` Playwright storefront edit/publish/rollback running against staging (section 8)
- [ ] `UNVERIFIED` All migrations applied to the real deployed database, including `AddExpenseLocation` (section 3)
- [ ] `BLOCKED` Production S3 storage and `App__PublicBaseUrl` configured (section 12)
- [ ] `UNVERIFIED` Email verification, password reset and account deletion verified end to end (section 2)

---

## 1. Build, CI and dependency maintenance

- [ ] `NOT IMPL` Delete the obsolete `.github/workflows/fix-lockfile.yml` (`contents: write`), and `cleanup-docs.yml` after it has run.
- [ ] `NOT IMPL` Review `.github/workflows/generate-lockfile.yml`; it deletes the lockfile and uses `npm@latest`. Replace with a reproducible, reviewed lockfile workflow (or delete it).
- [ ] `NOT IMPL` Resolve the multiple-lockfile situation (root `package-lock.json`, `web/`, `backend-node/`). Decide which manifests and lockfiles are authoritative; remove the root one if unused or set the Next.js workspace root.
- [x] `DONE` MessagePack `2.5.187` advisories NU1902/NU1903: transitive pin to 2.5.303 in `backend/Directory.Packages.props`; build shows 0 warnings in run #320. Re-check if a new advisory appears; stay on 2.x.
- [ ] `NOT IMPL` Resolve Expo dependency vulnerabilities in `mobile/avzar-expo` (28 reported: 10 moderate, 18 high); commit a lockfile and run an audit. (The Expo install failure itself is fixed.)
- [ ] `UNVERIFIED` Confirm the MAUI project builds after the `NU1015` fix (no run seen since #5).
- [ ] `NOT IMPL` Configure CI to detect dependency vulnerabilities and lockfile drift without introducing unreviewed dependency changes (web already runs `npm audit --audit-level=high`; Expo and root do not).
- [ ] `IN PROGRESS` Update `PROGRESS.md` and `docs/MASTER_ENGINEERING_HANDOFF.md` with verified evidence from #318 to #320 (`STATUS.md` and a handoff rewrite are prepared).
- [ ] `IN PROGRESS` Run the full applicable CI suite after each change and record commit and run. (#320 recorded in `STATUS.md`.)
- [x] `DONE` `@types/node` / lockfile mismatch: fixed, `npm ci` passes in #318.
- [x] `DONE` #289 compile error and `Phase3Tests` schedule-mail count: fixed, backend 735/735.

## 2. Tenant isolation, authorization and security

- [ ] `NOT IMPL` HTTP-level cross-business isolation tests for media upload, read and delete endpoints. (`CrossTenantApiTests.cs` already has 22 tests; media is the listed gap.)
- [ ] `NOT IMPL` Verify one business cannot access, replace or delete another business's media even when it knows the resource id.
- [ ] `IN PROGRESS` Test authorization at the API boundary, not only service level. (Endpoint-surface test and 22 HTTP cross-tenant tests exist; per-endpoint behavioural coverage is partial.)
- [ ] `UNVERIFIED` Review existing Manager roles and permissions; add permissions required by newer modules without granting unintended access. (Existing businesses' Manager roles lack the newer permissions until edited; Owner unaffected.)
- [ ] `UNVERIFIED` Verify `BusinessId` and other tenant-scoped ownership fields always come from trusted server-side identity. (Six HTTP tests covering `businessId` query/header being ignored passed in #254.)
- [ ] `UNVERIFIED` Verify account export and deletion end to end, including associated data and external resources. `?`
- [ ] `UNVERIFIED` Verify email verification and password-reset flows: expiry, invalid tokens, replay, abuse protection.
- [ ] `UNVERIFIED` Verify mobile JWT authentication and rotating refresh tokens: expiry, revocation, replay. `?`
- [ ] `UNVERIFIED` Verify platform-admin plan editing is restricted to authorized admins and enforces entitlement changes. (A `/platform-admin/plans` page exists.) `?`
- [ ] `UNVERIFIED` Review CSRF protection, secure cookies, authentication boundaries and API error handling.
- [ ] `UNVERIFIED` Test file upload validation, size/type limits, malware rejection and unauthorized download attempts. (Image re-encoding and a document sniffer exist.)

## 3. Database, migrations and data integrity

- [ ] `UNVERIFIED` Verify all pending migrations against the deployed database, including `AddExpenseLocation` and later ones. (Clean-DB application and model-drift gate pass in CI; the real database has not been checked.)
- [x] `DONE` Migrations apply on a clean PostgreSQL database without modifying `InitialSchema` (migrations job green; 73 tables seen in #254).
- [ ] `UNVERIFIED` Verify migrations on an existing database populated with data.
- [ ] `UNVERIFIED` Test concurrent inventory updates; stock must not be oversold or corrupted. (PostgreSQL regression tests exist for stock accounting; concurrency not confirmed.)
- [ ] `UNVERIFIED` Test customer-portal operations under PostgreSQL: authorization, concurrent updates.
- [ ] `NOT IMPL` Test MAUI sync and conflict handling against PostgreSQL-backed API workflows.
- [ ] `UNVERIFIED` Verify the leave-request approval flow against PostgreSQL: authorization, state transitions, concurrent approvals.
- [ ] `BLOCKED` Verify Data Protection keys persist across Render restarts and are shared across instances (needs the deployment).
- [ ] `UNVERIFIED` Confirm tenant-scoped queries and constraints prevent cross-business leakage. (Query filters, composite FKs and isolation tests exist.)
- [ ] `UNVERIFIED` Verify transactions and idempotency for workflows that create multiple related records.

## 4. Orders, customers, inventory and finance

- [ ] `NOT IMPL` Amendments to confirmed orders: validation, audit history, downstream task/schedule updates. `?`
- [ ] `NOT IMPL` Tax on order totals: rounding, persistence, invoice display, business settings. (The calculator shows tax; orders are tax-free until invoicing.)
- [ ] `NOT IMPL` Allocate one customer payment across multiple invoices, with correct balances and audit history.
- [ ] `NOT IMPL` Customer payment-proof submission and review/approval workflow.
- [ ] `NOT IMPL` Printable quotations (Quoted is only an order status today).
- [ ] `NOT IMPL` Per-design cost recipes connected to pricing/cost calculations.
- [ ] `UNVERIFIED` Verify the `DesignId` link from design to order across create, update, retrieval and reporting.
- [ ] `UNVERIFIED` Verify order invoice history in UI and API.
- [ ] `UNVERIFIED` Verify expense/category CRUD UI and its authorization.
- [ ] `UNVERIFIED` Verify expense location scoping: a location-limited user cannot view another location's expense. (HTTP tests for expenses were added; confirm they ran in CI.)
- [ ] `NOT IMPL` CSV import for designs, collections and expenses. (Export exists for all three; import exists for customers and inventory.)
- [ ] `UNVERIFIED` Verify existing customer and inventory CSV imports: validation, duplicates, invalid rows, partial-failure reporting.
- [ ] `UNVERIFIED` Verify existing CSV exports respect tenant and role permissions.
- [ ] `NOT IMPL` Billing-period-anchored quota windows; fix the UTC calendar-month boundary in `UsagePeriod`.
- [ ] `UNVERIFIED` Verify collection limits and other plan entitlements are enforced by the backend and reflected in the UI. (No plan limit on collections exists yet.)

## 5. Scheduling, jobs and notifications

- [ ] `UNVERIFIED` Verify daily scheduling for individual workers and the shop schedule.
- [ ] `UNVERIFIED` Verify the planner, preview/apply, version checks, transactions and audit events against current source and the deployed database. (Integration tests exist and pass in CI; deployed DB not checked.)
- [ ] `UNVERIFIED` Verify order confirmation generates the expected production tasks, and that changes/cancellations update dependent work.
- [ ] `UNVERIFIED` Test worker availability, business hours, holidays, overdue tasks and conflicting assignments.
- [ ] `NOT IMPL` Redis-backed job producers and handlers for workflows needing background processing (only queue primitives exist).
- [ ] `NOT IMPL` Test Redis worker retries, dead-letter handling, abandoned-message recovery, failure recovery and idempotency (only enqueue tests exist).
- [ ] `NOT IMPL` Test worker restart and recovery without losing or duplicating jobs.
- [ ] `NOT IMPL` Verify background handlers produce the expected database changes and notifications.
- [ ] `UNVERIFIED` Notification delivery for task assignments, daily schedules, due dates, order changes and business events. (Notifications are queued in the email outbox; delivery needs a real provider, see section 7.)

## 6. Real-time communication and SignalR

- [ ] `UNVERIFIED` Connect the SignalR event infrastructure to real web/mobile consumers. (A MAUI client exists; no web consumer found.)
- [ ] `NOT IMPL` Verify screens subscribe to and display real events.
- [ ] `UNVERIFIED` Test authentication and tenant isolation for hub connections and subscriptions. (An HTTP negotiate regression test exists.)
- [ ] `NOT IMPL` Test reconnects, drops, stale subscriptions and recovery.
- [ ] `BLOCKED` Test multi-instance delivery in the deployed configuration (Redis backplane is wired; needs a Redis deployment).
- [ ] `NOT IMPL` Confirm real business workflows emit events to the correct users.

## 7. Email and external integrations

- [ ] `BLOCKED` Connect and test a real email delivery provider (SMTP and Resend transports exist).
- [ ] `BLOCKED` Verify verification, reset, order-update and notification emails end to end.
- [ ] `UNVERIFIED` Delivered/bounced webhook handling: signature verification, duplicate events. (Webhook code exists.) `?`
- [ ] `BLOCKED` Paystack test-mode checkout and webhook signature validation.
- [ ] `BLOCKED` Paystack replay protection, idempotency, status transitions, reconciliation.
- [ ] `BLOCKED` S3-compatible storage: uploads, downloads, deletion, failure handling (`S3ObjectStorage` exists).
- [ ] `BLOCKED` ClamAV scanning: rejected files and scanner outages (`IVirusScanner` + ClamAV exist).
- [ ] `BLOCKED` Redis configuration, connection failures and recovery in the deployment.
- [ ] `OOS` for now: custom-domain DNS setup. The entitlement is intentionally disabled in every seeded plan until the policy and implementation are ready. (DNS TXT verification code exists.)
- [ ] `BLOCKED` OpenAI provider with real credentials, usage limits, error handling, cost controls.
- [ ] `NOT IMPL` Real push-notification delivery, only if push is in release scope.
- [ ] `IN PROGRESS` Keep secrets in deployment/CI secret stores; never commit credentials. (`SIXLABORS_LICENSE_KEY` is in CI secrets.)

## 8. Storefront builder, publishing and PWA

- [ ] `BLOCKED` Configure Playwright secrets/vars (`PLAYWRIGHT_BASE_URL`, `PLAYWRIGHT_EMAIL`, `PLAYWRIGHT_PASSWORD`, `PLAYWRIGHT_ALLOW_MUTATIONS=true`) so edit/publish/rollback e2e runs against staging or localhost. It is silently skipped now.
- [ ] `BLOCKED` Browser tests: edit, preview, publish, render, roll back a storefront.
- [ ] `UNVERIFIED` Template switching preserves intended content and settings.
- [ ] `UNVERIFIED` Storefront customization supports different layouts/templates, content, images and custom colors.
- [ ] `UNVERIFIED` Test iframe/CSP behaviour; required functionality works without weakening security.
- [ ] `UNVERIFIED` Test upload rejection for invalid, oversized or malicious media.
- [ ] `UNVERIFIED` Published storefronts render correctly on mobile and desktop.
- [ ] `UNVERIFIED` PWA installation, updates, offline behaviour and recovery on reconnect. (Update prompt and static-shell caching exist.)
- [ ] `NOT IMPL` Polished drag-and-drop storefront canvas, if it remains in the intended release. (Section reordering exists; the editor is largely structured/JSON data.)
- [ ] `UNVERIFIED` Storefront entitlements for Free, Starter, Professional and Business plans.
- [ ] `UNVERIFIED` Custom-domain restrictions match the intentionally disabled entitlement.
- [ ] `NOT IMPL` Self-host required fonts (currently Google Fonts, allowed by CSP) or verify production font loading.

## 9. Web application and user experience

- [ ] `NOT IMPL` Component-level web tests for the AI Studio (helper functions are tested; components are not).
- [ ] `NOT IMPL` Component-level web tests for the customer portal.
- [ ] `IN PROGRESS` Expand web test coverage for storefront, finance, inventory, scheduling and billing. (18 test files now, up from 9.)
- [ ] `NOT IMPL` Pricing page UI and the subscription/onboarding journey (marked out of the PWA follow-up scope).
- [ ] `UNVERIFIED` Expense/category management and invoice-history screens against the real API.
- [ ] `UNVERIFIED` Permission-dependent navigation and actions for each relevant role.
- [ ] `UNVERIFIED` Loading, empty, validation, error and success states across key workflows.
- [ ] `UNVERIFIED` Confirm the Node.js service only serves the PWA and proxy responsibilities; business logic stays in ASP.NET Core unless an explicit architecture decision changes this. (Documented as such; Node `/health/ready` reports from the upstream API.)

## 10. AI assistant and Fashion Studio

- [ ] `BLOCKED` Verify AI Studio end to end with a real provider: prompts, outputs, error handling, usage limits.
- [ ] `BLOCKED` Verify AI assistant/builder workflows against real tenant data with tenant isolation.
- [ ] `BLOCKED` Verify AI image generation and media storage, cost controls, failed-generation handling.
- [ ] `NOT IMPL` Component-level tests for AI Studio and assistant workflows.
- [ ] `UNVERIFIED` Design-to-order workflow preserves `DesignId` links.
- [ ] `UNVERIFIED` AI-related entitlements, rate limits and usage tracking.
- [ ] `NOT IMPL` Decide whether progress photos and per-design cost recipes are required for the intended release; implement any in scope. (No progress-photo code found.)

## 11. Mobile and offline support

- [ ] `UNVERIFIED` Verify the MAUI app builds with current source and dependencies.
- [ ] `UNVERIFIED` Test mobile authentication, token refresh and logout.
- [ ] `NOT IMPL` Test offline access, queued changes, sync and conflict resolution.
- [ ] `UNVERIFIED` Test worker task updates and notifications on mobile.
- [ ] `UNVERIFIED` Verify mobile consumers receive and handle SignalR events (MAUI `BusinessEventsClient` exists).
- [ ] `NOT IMPL` Verify media upload/download on mobile, including poor connectivity and failed transfers.
- [ ] `IN PROGRESS` Expo package: dependency install, doctor and Android export pass; lockfile and audit still open (see section 1).
- [ ] `NOT IMPL` Decide and document the supported production mobile path (MAUI vs Expo vs PWA) and each one's responsibilities. The owner has declared PWA-first.

## 12. Production deployment and operations

- [ ] `BLOCKED` Persistent Data Protection keys; verify they survive restarts and work across instances.
- [x] `DONE` `SIXLABORS_LICENSE_KEY` configured as a CI secret (backend job passes). Still confirm the license terms fit your revenue tier.
- [ ] `BLOCKED` Configure the public HTTPS origin in `App__PublicBaseUrl` (ownership-transfer links depend on it).
- [ ] `BLOCKED` Production S3/CDN storage; verify access policies, media delivery and cleanup.
- [ ] `UNVERIFIED` Orphaned-media cleanup (`OrphanFileCleaner` / `OrphanCleanupWorker` exist); verify failed/deleted records leave no unmanaged files.
- [ ] `NOT IMPL` Verify production secrets, environment variables, migrations and health checks.
- [ ] `NOT IMPL` Verify logs and diagnostics for jobs, email, payments, storage and AI provider failures.
- [ ] `NOT IMPL` Verify recovery procedures for database, Redis, storage and provider outages.
- [ ] `BLOCKED` Run staging tests for real integrations before enabling them in production.
- [ ] `NOT IMPL` Document deployment, rollback, secret rotation and incident recovery. (`docs/RUNBOOK.md` and `docs/DEPLOYMENT.md` exist; coverage of rollback and incident recovery not confirmed.)

## 13. Tests and release acceptance

- [x] `DONE` Full backend suite run: 735/735 (#320).
- [ ] `IN PROGRESS` Full web suite and coverage report for critical workflows (job green; counts not seen in recent logs).
- [x] `DONE` Mobile checks: Expo Doctor 21/21 and Android export (mobile CI).
- [ ] `IN PROGRESS` Migration validation on clean and existing database states (clean done; existing not).
- [ ] `IN PROGRESS` Authorization and cross-tenant isolation tests (22 HTTP tests; media missing).
- [ ] `BLOCKED` Playwright storefront and core workflow tests against staging or local.
- [ ] `BLOCKED` Payment, email, storage, malware scanning, Redis and AI integrations in supported test environments.
- [ ] `BLOCKED` Verify production configuration and restart/recovery behaviour.
- [ ] `IN PROGRESS` Update `PROGRESS.md`, the handoff and this checklist only with evidence from source, test output or deployed verification.
- [ ] `IN PROGRESS` Record latest commit, CI run, test counts, failed checks and blockers. (Run #320: 735/735.)

---

## Completion policy

Do not mark the project complete from a green CI run alone. Next action: work section 0 first, then the remaining sections in dependency order, running CI after every increment and updating the status tags with evidence.
