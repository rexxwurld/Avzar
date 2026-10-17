# Avzar: Status

Single current-status file. Detailed unfinished work is in `TODO.md`; `PROGRESS.md` and `docs/MASTER_ENGINEERING_HANDOFF.md` are history. Source code and the latest CI results take precedence over every document.

## Direction (owner, 2026-10-17)

- Primary client: the installable vanilla HTML/CSS/JavaScript PWA (`pwa/`), served by Node/Express (`backend-node/`).
- The ASP.NET Core API (`backend/`) and PostgreSQL stay authoritative. Node only serves the PWA and proxies `/backend/api/**`; no business domain has moved to Node. Any later move is domain by domain (`docs/NODE_API_COMPATIBILITY_MATRIX.md`).
- New staff/owner screens are built in the PWA. `web/` (Next.js) stays for the public storefront renderer and preview; its management screens are not being extended.
- `mobile/avzar-expo` is reference only. The MAUI project (`mobile/Avzar.Mobile`) is not in the 2026-10-17 archive.

## Current state (CI #329, 2026-10-17)

- Backend job (commit `7e9f6aa`, "Create MediaIsolationApiTests.cs"): Release build 0 warnings / 0 errors, **741/741 tests pass** (TRX: 741 passed, 0 failed, 0 skipped), committed migrations match the EF model.
- New in #329: `MediaIsolationApiTests` (6 HTTP tests: read/list/open by id, delete by id, client `BusinessId` ignored on upload and as a header, no-permission member, anonymous) all pass.
- web, pwa-node, migrations and Expo jobs were last seen green in #318; only the backend log was reviewed for #329.
- Deployed on Render: API at https://avzar-api.onrender.com, web at https://avzar-web.onrender.com. The PWA host (`backend-node`) has not been deployed or verified.
- PWA coverage (read from `pwa/app.js` routes, not run): sign-in with business selection and 2FA/recovery, home overview, customers and measurements (create, revise, history, compare), orders (list, detail, create/edit, quote/confirm/cancel/deliver, task generation), fittings, task board and detail, shop schedule, worker schedules and leave, finance (invoices, payments, credit notes, receipts), expenses, inventory, staff list/invite, customer-portal links, business settings and account security. Not in the PWA yet: storefront editor, AI studio, billing/plans, platform admin, ownership transfer and a reports page. Offline: static-shell cache only (`avzar-shell-v17`); no offline write queue.
- Not production-ready. Playwright e2e, real email/Paystack/S3/Redis/DNS checks and device tests have not run.

Rules that still apply: forward-only migrations (never touch `InitialSchema`), never trust client `BusinessId`, never weaken tests, no production-ready claims, no Docker smoke test (owner decision), run CI after every increment.

## Open defect: customer create returns 403 on the deployed web (2026-10-17)

- `POST /api/customers` needs `customers.manage` (owners always pass). The web app shows "You do not have permission to do that." only when the error body has no `code`, which rules out `plan_limit_reached`, `not_a_member` and CSRF failures (CSRF is a 400).
- Two causes remain: (a) the Next proxy refusing a write whose `Origin` host differs from `Host` (`web/src/lib/proxy.ts`, body `{"status":403,"title":"Cross-origin request refused."}`), or (b) the API permission handler returning an empty 403 for a non-owner whose role lacks `customers.manage`.
- Not yet confirmed. Read the response body of the failed `POST /backend/api/customers` (network tab or Render log). Check the same write through `backend-node`, whose proxy also refuses cross-origin writes.

## 1. Do next (small, concrete)

- [ ] Capture the 403 response body (see Open defect) and fix the cause: proxy Origin/Host handling, or the role's permissions (add a backfill so existing Manager roles get the newer permissions).
- [ ] Deploy `backend-node` on Render with `API_URL=https://avzar-api.onrender.com`. Check sign-in, customer create and an order action through the cookie + CSRF proxy, then install the PWA on a phone.
- [ ] Verify the existing PWA screens against staging, in this order: customers/measurements, orders (quote/confirm/cancel), tasks, finance. Record what works and what does not.
- [ ] Add the offline write queue (IndexedDB, idempotency keys, conflict handling) per `docs/OFFLINE_SYNC.md`.
- [ ] Review the web, pwa-node, migrations and Expo job results of the latest run; confirm the Next.js multiple-lockfile warning is gone (root `package-lock.json` is no longer in the repo).
- [ ] Expo (`mobile/avzar-expo`): 28 npm vulnerabilities and no committed lockfile. Commit a lockfile and add an audit step, or retire the folder.
- [ ] Add Playwright secrets/vars (`PLAYWRIGHT_BASE_URL`, `PLAYWRIGHT_EMAIL`, `PLAYWRIGHT_PASSWORD`, `PLAYWRIGHT_ALLOW_MUTATIONS=true`) so the storefront e2e runs against staging or localhost only.

Done since the last update (checked in the archive or CI): media isolation HTTP tests (#329); MessagePack pin cleared the advisories (#320); `fix-lockfile.yml`, `cleanup-docs.yml` and `generate-lockfile.yml` are gone from `.github/workflows` (only `ci.yml`, `generate-migration.yml` and `mobile-ci.yml` remain); root `package-lock.json` removed; MAUI project removed from the repo.

## 2. Verification gaps (code exists, not proven)

- [x] HTTP cross-business probes for media upload/read/delete: done (`MediaIsolationApiTests`, 6 tests, #329). Confirm the newer storefront/design/collection/portal-revocation probes ran in CI.
- [ ] Redis job queue: worker retry / dead-letter / recovery tests, and real producers and handlers. Today only primitives and enqueue tests exist.
- [ ] SignalR: Redis backplane is wired; confirm a web/mobile client actually consumes it, and test multi-instance.
- [ ] Component-level web tests for AI studio and the customer portal (helpers are tested, components are not). Check coverage for storefront, finance, inventory, worker schedule, billing.
- [ ] Browser/staging checks: storefront template switch, editor iframe CSP, upload rejection, publish-to-render, PWA install/update, offline.
- [ ] Notification delivery with a real email provider: task assignment, blocked-task escalation, next-step ready, schedule reminders, overdue reminders. `notificationsQueued` is not delivery confirmation.
- [ ] Paystack in test mode: checkout, signature verification, replay/idempotency.
- [ ] Email provider webhooks (delivered/bounced) against the real provider.
- [ ] Real S3, ClamAV, Redis, DNS (custom domains), OpenAI (AI studio).
- [ ] PostgreSQL integration of inventory reservation under concurrency; customer portal; offline sync and conflict behavior (the MAUI client is gone; the PWA has no offline queue yet).
- [ ] Leave request/approval flow against PostgreSQL (overlap rules, permissions).

## 3. Production/ops blockers

- [ ] Data Protection keys are stored in PostgreSQL (`PersistKeysToDbContext`, migration `AddDataProtectionKeys`). Confirm preview links and sessions survive a Render restart. See `docs/RUNBOOK.md`.
- [ ] `SIXLABORS_LICENSE_KEY` secret is required by CI (backend job fails without it). Confirm the license fits your revenue tier.
- [ ] Production S3 storage config, CDN in front of `/public/media`, orphan-file cleanup scheduling.
- [ ] Self-host fonts (currently Google Fonts, allowed by CSP; privacy hardening).
- [ ] `App__PublicBaseUrl` set to the public HTTPS web origin (ownership-transfer links depend on it).

## 4. Features not built (or not confirmed)

- [ ] Progress-photo attachments on tasks.
- [ ] Amendments to confirmed orders.
- [ ] Tax applied to order totals (calculator shows it; orders are tax-free until invoicing).
- [ ] Multi-invoice payment allocation (one payment covers one invoice today).
- [ ] Customer-uploaded payment proof (portal/storefront creating Pending payments).
- [ ] Printable/standalone quotation document (Quoted is only an order status).
- [ ] Per-design default cost recipes.
- [ ] Billing-period-anchored quota windows (currently UTC calendar month; seam is `UsagePeriod`).
- [ ] Plan limit for collections (add an entitlement key if wanted).
- [ ] CSV: import for designs, collections and expenses (export exists for designs, collections, expenses; import exists for customers and inventory).
- [ ] Polished drag-and-drop storefront canvas (current editor is structured data with JSON for theme/section props).
- [ ] Custom-domain flag is false in every seeded plan on purpose; flip when the feature ships.
- [ ] Existing businesses' Manager roles lack the newer permissions until edited (Owner unaffected). Migration/backfill or release note.
- [ ] Pricing page UI and onboarding wizard (out of PWA follow-up scope).
- [ ] Node owning any domain (all business logic stays in ASP.NET; Node only serves the PWA and proxies).
- [ ] PWA screens still missing: storefront editor, AI studio, billing/plans, platform admin, ownership transfer and a reports page.

## 5. Exists in code per a search, but confirm before closing

I only checked that these appear in the code, not that they work. Verify, then tick off.

- [ ] Email verification and password reset (templates and endpoints present).
- [ ] Account export/deletion.
- [ ] JWT + rotating refresh tokens for mobile (some `RefreshToken` references).
- [ ] Platform-admin plan editing (a `/platform-admin/plans` page exists).
- [ ] Design-to-order link (`DesignId` appears widely).
- [ ] Expense/category CRUD UI and order invoice history (reported added in the roadmap, no dedicated expense component found by filename).

---

## 6. Stale boxes you can close (covered by CI)

These were "compile, create migration, run all tests" or "never run" checkpoints. CI #318 compiled everything, applied all migrations on clean PostgreSQL, passed the EF model-drift gate, and ran 735/735 backend tests:

- Every "Compile / regenerate migration / run all tests" box in Phases 1 to 3 and Steps 1 to 5.
- `@types/node` upgrade and lockfile regeneration.
- #289 build error (`OverdueTaskReminderWorker` missing `using`) and the `Phase3Tests` schedule-mail count.
- Expo mobile install failure.
- "Regenerate the single `InitialSchema` migration" (forbidden: forward migrations only).
- Redis, PDF generation, working hours/holidays, global/order search: implemented.
- Email outbox, S3 storage, ClamAV interface, document uploads: implemented (real-provider verification stays in section 2).
