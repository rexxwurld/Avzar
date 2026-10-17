# Avzar: Status

Single current-status file. Older dated logs were removed; git history keeps them.

## Current state (CI run #319, 2026-10-17)

- Backend: build 0 errors, **735/735 tests pass**, committed migrations match the EF model.
- web, pwa-node, migrations: green (run #318). Mobile CI (Expo): green.
- Warning left: MessagePack advisories. Pinned `MessagePack` 2.5.303 in `backend/Directory.Packages.props`; confirm NU1902/NU1903 are gone on the next run.
- Not production-ready. Playwright e2e, real email/Paystack/S3/Redis/DNS checks and device tests have not run.

## Where things live

- `README.md`: overview, setup, layout
- `docs/RUNBOOK.md`, `docs/DEPLOYMENT.md`, `docs/ENVIRONMENT_CONFIGURATION.md`: ops and config
- `docs/architecture.md`: design
- `docs/MASTER_ENGINEERING_HANDOFF.md`, `PROGRESS.md`: long history (kept for now; this file is the current list)
- `docs/FEATURE_INVENTORY.md`, `docs/NODE_API_COMPATIBILITY_MATRIX.md`, `docs/MOBILE_NODE_MIGRATION_INVENTORY.md`: Node/PWA migration ledgers
- `docs/PLAYWRIGHT.md`, `docs/OFFLINE_SYNC.md`, `docs/production-scheduling.md`: feature notes

Rules that still apply: forward-only migrations (never touch `InitialSchema`), never trust client `BusinessId`, never weaken tests, no production-ready claims, no Docker smoke test (owner decision), run CI after every increment.

---

## 1. Do next (small, concrete)

- [ ] Delete `.github/workflows/fix-lockfile.yml` and `cleanup-docs.yml` after use (one-off, both have `contents: write`).
- [ ] Review `.github/workflows/generate-lockfile.yml`: it deletes the lockfile and uses `npm@latest`, which can reintroduce drift. Keep only if used deliberately.
- [ ] Confirm the MessagePack pin (2.5.303 in `backend/Directory.Packages.props`) clears NU1902/NU1903 in the next CI restore/build log. If a new advisory appears, bump the pin within the 2.x line.
- [ ] Resolve the **multiple-lockfiles** warning (root `package-lock.json`, `web/`, `backend-node/`). Remove the root one if unused, or set the Next.js workspace root.
- [ ] Expo: 28 npm vulnerabilities (10 moderate, 18 high) in `mobile/avzar-expo`, no committed lockfile, and the job runs no `npm audit`. Commit a lockfile and add an audit step.
- [ ] Add Playwright secrets/vars (`PLAYWRIGHT_BASE_URL`, `PLAYWRIGHT_EMAIL`, `PLAYWRIGHT_PASSWORD`, `PLAYWRIGHT_ALLOW_MUTATIONS=true`) so the storefront edit/publish/rollback e2e actually runs. Staging or localhost only.
- [ ] Update `PROGRESS.md` / `docs/MASTER_ENGINEERING_HANDOFF.md` "newest CI evidence" to run #318 (735 backend tests; web, pwa-node, migrations green).
- [ ] Confirm a MAUI build run passes after the NU1015 fix (no run seen since run #5).

## 2. Verification gaps (code exists, not proven)

- [ ] HTTP cross-business probe for **media upload/read/delete** (the only isolation area still listed as untested over HTTP). Confirm the newer storefront/design/collection/portal-revocation probes ran in CI.
- [ ] Redis job queue: worker retry / dead-letter / recovery tests, and real producers and handlers. Today only primitives and enqueue tests exist.
- [ ] SignalR: Redis backplane is wired; confirm a web/mobile client actually consumes it, and test multi-instance.
- [ ] Component-level web tests for AI studio and the customer portal (helpers are tested, components are not). Check coverage for storefront, finance, inventory, worker schedule, billing.
- [ ] Browser/staging checks: storefront template switch, editor iframe CSP, upload rejection, publish-to-render, PWA install/update, offline.
- [ ] Notification delivery with a real email provider: task assignment, blocked-task escalation, next-step ready, schedule reminders, overdue reminders. `notificationsQueued` is not delivery confirmation.
- [ ] Paystack in test mode: checkout, signature verification, replay/idempotency.
- [ ] Email provider webhooks (delivered/bounced) against the real provider.
- [ ] Real S3, ClamAV, Redis, DNS (custom domains), OpenAI (AI studio).
- [ ] PostgreSQL integration of inventory reservation under concurrency; customer portal; MAUI sync and conflict behavior.
- [ ] Leave request/approval flow against PostgreSQL (overlap rules, permissions).

## 3. Production/ops blockers

- [ ] **Data Protection keys persisted** across Render restarts (preview links break otherwise). See `docs/RUNBOOK.md`.
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
