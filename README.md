# Avzar

**Avzar is a multi-tenant SaaS platform for fashion designers, tailoring businesses, garment workshops and fashion entrepreneurs.** One workspace per business covers customers and measurements, designs, orders and quotations, invoices and verified payments, workshop production, and a public online storefront.

> **Status: active development, not production-ready.** The backend and storefront renderer are substantially built. The staff dashboard is **partially implemented** (auth, staff/roles/locations, customers and initial order workflows). The storefront editor and production dashboard are **partial**; automated scheduling is still incomplete (a pure first-pass domain algorithm exists but is not integrated), as are notifications, AI, billing, platform admin and the mobile app. Each claim below is tagged with its evidence level. See [Verification and known limitations](#verification-and-known-limitations).

## Who it is for and what it solves

Small fashion businesses typically run on WhatsApp threads, paper measurement books and spreadsheets. That makes measurements easy to lose, prices inconsistent, payments hard to reconcile and production hard to track. Avzar's goal is one system where:

- a customer's measurements are versioned and frozen onto each order;
- prices are calculated server-side from materials, labour and margin rules;
- an invoice is only paid once staff verify the payment evidence;
- a confirmed order generates production tasks that workers act on;
- the business publishes its own storefront from a validated template.

## Module status

| Module | State | Evidence |
|---|---|---|
| Auth, businesses, memberships, roles, permissions, invitations, audit writes | Implemented | Backend suite green in CI |
| Subscription plans and entitlements (Free / Starter / Professional / Business) | Implemented; no payment provider | Backend suite green in CI |
| Customers, measurements, designs, collections, media | Implemented | Backend suite green in CI |
| Orders, quotations (`Quoted` status), pricing calculator | Implemented | Backend suite green in CI |
| Invoices, payments (staff-verified), receipts, credit notes, expenses | Implemented | Backend suite green in CI |
| Workflows, task generation, assignment, worker actions, alterations | Implemented | Backend suite green in CI |
| Storefront builder API (8 templates, drafts, publish, rollback, presets, DNS-verified domains) and public Next.js renderer | Implemented; **not smoke-tested end to end** | Backend tests and web build in CI |
| Account security: email verification, password reset, TOTP 2FA, JWT + rotating refresh tokens, sessions, export/delete | Implemented | Tests exist; no real email provider exercised |
| Email outbox (SMTP / Resend), S3 storage, ClamAV, Redis rate limits, QuestPDF documents, customer CSV, global search | Implemented | Fakes only; no real service exercised |
| Staff dashboard | **Partially implemented** | Auth, staff/roles/locations, customer workflows, order create/edit/lifecycle and production task list/detail/actions; latest UI changes await CI verification |
| Finance UI | **Partial** | Invoice list/create/detail and lifecycle/payment actions, order-to-invoice flow, pending-payment view, and expense summary; expense CRUD and invoice draft editing remain open |
| Business schedule settings | **Initial UI** | Weekly opening hours and closure dates at `/settings`; this is not the production task scheduler or notification system |
| Storefront editor | **Partial** | `/storefront` supports draft content/theme, custom pages, slug/SEO/navigation, section content/style, existing-media picker, template-switch preview, custom-domain DNS verification, preview, publish and rollback; still lacks a polished drag-and-drop canvas and browser E2E |
| Customer portal | Not built | |
| Shop and individual-worker scheduling, availability, leave, daily capacity and email reminders | Implemented in source; backend compilation, PostgreSQL integration, migration drift and live provider delivery still require verification | |
| Inventory and stock reservations | Not built | |
| Reports | Not built | |
| AI design tools and assistant builder | Not built | |
| Paystack/Flutterwave billing, platform admin | Not built | |
| .NET MAUI mobile and offline sync | Not built | |

Per-item detail lives in [PROGRESS.md](PROGRESS.md).

## Architecture

```mermaid
flowchart LR
  Browser[Browser] --> Web[Next.js 15<br/>public renderer]
  Web -->|/api proxy| Api[ASP.NET Core API<br/>.NET 10]
  Mobile[MAUI app<br/>planned] -.->|JWT| Api
  Api --> App[Application<br/>contracts, DTOs]
  App --> Dom[Domain<br/>entities, rules]
  Api --> Infra[Infrastructure<br/>EF Core, services]
  Infra --> PG[(PostgreSQL)]
  Infra -.->|optional| Redis[(Redis)]
  Infra -.->|optional| S3[(S3-compatible storage)]
  Infra -.->|optional| Mail[SMTP / Resend]
  Infra -.->|optional| AV[ClamAV]
```

Layers: `Tailoring.Domain` (entities, permissions) → `Tailoring.Application` (service contracts, DTOs, `AppException`) → `Tailoring.Infrastructure` (EF Core, implementations) → `Tailoring.Api` (thin controllers, auth, wiring). Details: [docs/architecture.md](docs/architecture.md).

### Principles

- **Multi-tenancy:** tenant entities carry `BusinessId`, with EF global query filters, a `SaveChanges` write guard and composite foreign keys so PostgreSQL itself rejects cross-business links. The business comes from the signed session, never from the request.
- **Security:** permission checks on every protected endpoint; CSRF header on cookie-authenticated writes (bearer requests exempt); strict storefront layout validation with no user HTML/CSS/JS; images re-encoded; public storefront shows only published, public data.
- **Money:** decimal arithmetic, server-computed totals, idempotency keys, advisory locks, optimistic concurrency. Pending payments count for nothing until verified; financial and audit rows are never deleted.
- **Entitlements:** one catalogue of gates, enforced server-side and failing closed. Downgrades never delete data.

## Repository structure

```
backend/src/Tailoring.{Domain,Application,Infrastructure,Api}
backend/tests/Tailoring.IntegrationTests   PostgreSQL via Testcontainers
web/                                       Next.js 15 public storefront renderer
docs/                                      architecture, runbook, static review
scripts/bootstrap-backend.sh               solution + first migration
.github/workflows/                         ci.yml, generate-migration.yml, generate-lockfile.yml
```

## Local development

Prerequisites: .NET SDK 10.0.100+ (`backend/global.json`), Node.js 22, Docker, `dotnet tool install -g dotnet-ef`, and a Six Labors license (ImageSharp 4.x; see RUNBOOK section 8).

```bash
cp .env.example .env                       # set a local password
docker compose up -d postgres
./scripts/bootstrap-backend.sh             # restore and build (migrations are already committed)
cd backend
ConnectionStrings__Default="Host=localhost;Database=tailoring;Username=tailoring;Password=<password>" \
  ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Tailoring.Api
```

Development auto-migrates and seeds plans. Production must apply migrations explicitly (or set `Database__MigrateOnStartup=true`) and set `Billing__SeedPlansOnStartup=true` once. **Eight migrations are committed** under `backend/src/Tailoring.Infrastructure/Persistence/Migrations` and applied to a clean PostgreSQL by CI; add forward migrations only (see the RUNBOOK) and never regenerate `InitialSchema`. Full steps: [docs/RUNBOOK.md](docs/RUNBOOK.md).

### Tests

```bash
cd backend && dotnet test -c Release       # needs Docker
cd web && npm ci && npm run typecheck && npm test && npm run build
```

## Configuration

Set through environment variables or a secret manager; never commit values.

| Variable | Purpose |
|---|---|
| `ConnectionStrings__Default` | PostgreSQL connection |
| `SIXLABORS_LICENSE_KEY` | Build-time ImageSharp license (CI secret) |
| `Pdf__QuestPdfLicense` | Explicit QuestPDF license choice |
| `Auth__JwtSigningKey` | Enables bearer/mobile auth (32+ chars) |
| `Auth__RequireVerifiedEmail` | Optional email-verification gate (default off) |
| `Email__Provider` and provider settings | `log` (default), SMTP or Resend |
| `Storage__Provider`, `Storage__S3__*` | Local disk (default) or S3-compatible |
| `Redis__ConnectionString` | Optional shared rate limits |
| `Billing__SeedPlansOnStartup` | Seed plans once in production |
| `BASE_DOMAIN`, `API_PUBLIC_URL`, `WEB_BASE_URL` | Public hosting |

Persistent ASP.NET Core Data Protection keys are required in production (preview links). Other settings are listed in the RUNBOOK.

## Verification and known limitations

- **CI run #238** (commit `40306ac`): backend, web and migrations jobs succeeded per the run page; logs were collapsed, so counts were not seen. See `PROGRESS.md`.
- **CI run #230** (2026-10-16, commit `c349faf9023a9a0c1c6d5e7ddb5950c76016953c`): the supplied backend log shows a Release build with 0 warnings/0 errors, **693/693 tests passed** (0 failed, 0 skipped), and the EF model-drift gate reports that committed migrations match the model. The web job succeeded and `npm audit` reported 0 vulnerabilities; the supplied web excerpt does not show the test count or typecheck/build summaries. The #230 migration job output was not supplied. Historical run #207 applied eight migrations to clean PostgreSQL 17.11 (67 tables, 33 check constraints, 113 foreign keys, 8 partial indexes, no cascading foreign keys on financial/audit tables). Ownership-transfer notification emails and location-scoping behavior are covered by the passing backend suite.
- **Dependency security:** `web/package.json` pins Next.js 15.5.28, React ^19.2.8 and Vitest 4.1.11 with a `postcss` override; `web/package-lock.json` is committed, CI and the Docker build use `npm ci`, and CI fails on any high or critical `npm audit` finding in the whole tree. Check the Next.js release notes (Next.js ships monthly security releases) before any public deployment.
- **Not verified:** `docker compose` startup; full-stack smoke test; storefront publish-to-render; email, S3, ClamAV, Redis, DNS and every other external integration; mobile. The staff dashboard is partial: the order create/edit/action UI is present and a partial production task list/detail/actions UI is now added for manager and worker views in the working copy. These latest order/production UI changes have not yet been verified by a fresh web build or CI. The `/storefront` editor now exists as a first slice but is not yet the polished drag-and-drop builder.
- CI actions are on `checkout@v6`, `setup-node@v6`, `setup-dotnet@v5`, `upload-artifact@v6`; run #207 used them without error.
- Known design issues: the public site's `frame-ancestors 'none'` will block the editor's preview iframe; nonce CSP makes public pages dynamic (not CDN-cacheable).

## Roadmap

Planned, in rough priority order (none implemented unless marked above):

1. Verified baseline: migrations applied to a clean PostgreSQL (done in CI), authorization tests via `WebApplicationFactory` (done), web dependencies audited clean (done); a compose smoke test is not planned (owner decision), so compose startup and external integrations stay unverified.
2. Remaining Step A gaps: Redis caching/queues/SignalR backplane, more CSV import/export, background-job recovery, and email extras. HTTP-level location-scope tests for invoices/payments/tasks have been added and are included in the 693-test backend suite. Customers stay unscoped by design. (Audit-read, TOTP replay protection, join-with-existing-account, ownership transfer with a one-owner database constraint, PDF branding, location scoping of orders, finance, tasks and expenses, ownership-transfer notification emails, the Resend delivery webhook and the email suppression list are implemented and covered by the passing suite.)
3. Staff dashboard: verify order and read-only production UI, then verify task detail/actions and implement schedule planning plus finance workflows; build the visual storefront editor (with Playwright tests).
4. Scheduling, inventory, notifications, reports.
5. Customer portal, billing, platform admin, AI tools, mobile.

## Documentation

[PROGRESS.md](PROGRESS.md) · [docs/RUNBOOK.md](docs/RUNBOOK.md) · [docs/architecture.md](docs/architecture.md) · [docs/STATIC_REVIEW.md](docs/STATIC_REVIEW.md)

### Storefront management (current web slice)
The signed-in `/storefront` page supports storefront creation from allowed templates, draft editing for page title/slug/SEO/visibility and navigation, custom page add/remove under plan entitlements, section content/style/variant/visibility and ordering, theme settings, private preview, publish/unpublish, version history/rollback, and contact settings. This is still a structured-data editor, not a polished drag-and-drop visual builder; browser E2E and media-picker work remain open. See `docs/MASTER_ENGINEERING_HANDOFF.md` for verification status.

### Business schedule settings
Authorized staff can manage weekly opening hours and closure dates at `/settings`. These settings feed `/tasks/schedule`. The `/workers/schedule` page manages individual worker availability, daily capacity, approved leave and task calendars. The backend queues deduplicated assignment emails and eligible 24-hour reminders through the durable email outbox. Live delivery, full builds and PostgreSQL migration/integration verification remain outstanding; mobile calendar parity is not implemented.

### Finance UI (initial slice)
`/finance` shows invoices, pending payment claims, and expense summaries for the current user's permissions. Free-form draft invoices can be created at `/finance/invoices/new`; invoice detail supports issue, void, credit notes, recording payments, and permission-gated verification/reversal. Eligible order details can create Full/Deposit/Balance draft invoices. Expense CRUD and invoice draft editing are still outstanding, and this UI needs a dependency-backed CI run.

### Production scheduling status

The backend includes `POST /api/tasks/schedule/preview` and `POST /api/tasks/schedule/apply`, plus worker-specific availability, approved leave, daily capacity and a location-filtered worker calendar. Preview is gated by `tasks.view`; apply requires `tasks.manage`; both require `scheduling.shop`. Apply recalculates the plan, checks task row versions and exact slots, rejects stale/partial plans, persists schedule events transactionally and queues deduplicated assignment/reminder emails. The latest changes have not been compiled or tested against PostgreSQL because the .NET SDK and Docker are unavailable in the working environment. See `docs/production-scheduling.md`.


### Production schedule preview

Users with task-view access can open `/tasks/schedule` to request a proposed shop schedule. The preview is entitlement-gated by the API and does not persist planned times. See [`docs/production-scheduling.md`](docs/production-scheduling.md) for limitations and verification status.

### Shop schedule apply increment (2026-10-16)
- Added `POST /api/tasks/schedule/apply`, gated by `tasks.manage` and `scheduling.shop` entitlement.
- Preview responses now include task row versions. Apply recalculates server-side, rejects stale candidate sets/versions and partial schedules, then saves planned times transactionally with `schedule_applied` task events.
- The web schedule screen now offers Apply only when every candidate is schedulable and submits the preview's version map; stale plans must be regenerated.
- Superseded by the latest increment recorded in `PROGRESS.md` and `docs/production-scheduling.md`: worker availability/leave/capacity and email outbox reminders are now implemented in source, but verification is still blocked.
- Verification remains incomplete: .NET SDK and npm dependencies are unavailable in this environment; no backend compile/integration tests or full web build/tests were run for this increment.

## Roadmap increment: inventory, reports, customer portal, AI, billing and mobile

This source increment adds the following API surfaces:

- `GET/POST /api/inventory`, `POST /api/inventory/{id}/{receive|adjust|reserve|release|consume}`, `GET /api/inventory/{id}/movements`, CSV preview/commit import and export, plus atomic `POST /api/inventory/tasks/{taskId}/reserve` and `/consume` batches. Customer CSV import/export now has a web UI with preview, duplicate handling and confirmation.
- `GET /api/reports/summary?from=YYYY-MM-DD&to=YYYY-MM-DD` with tenant and location-scoped totals.
- `POST /api/customer-portal/customers/{customerId}/link`, `DELETE .../links`, and public `GET /api/customer-portal/access/{token}`.
- `POST /api/ai/fashion-design` and `POST /api/ai/assistant-builder` (OpenAI provider configuration required; plan entitlements gate each feature).
- `POST /api/billing/subscription/checkout` and anonymous signature-verified `POST /api/billing/subscription/webhook` (Paystack; NGN plans only).
- `GET/PUT /api/platform-admin/plans` for allowlisted platform-admin emails configured under `PlatformAdmin:Emails`.
- Authenticated SignalR hub at `/hubs/business-events` (business-scoped group; worker task transitions publish `task.updated`).
- .NET MAUI starter at `mobile/Avzar.Mobile/` for Android/iOS.

Configure `AI__OpenAI__ApiKey`, `Billing__Paystack__SecretKey`, `Billing__Paystack__CallbackUrl`, `Redis__ConnectionString`, and `PlatformAdmin__Emails__0` in the hosting environment as applicable. Never commit real secrets. Configure the Paystack webhook URL as `https://<api-host>/api/billing/subscription/webhook`. The Paystack callback page is a return path only; the server activates a plan only after independently verifying a signed provider webhook. Read `docs/VERIFICATION_REPORT_2026-10-16_ROADMAP.md`: this increment has not yet been compiled or tested against PostgreSQL in the current environment.
