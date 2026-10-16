# Progress checklist

## Verification status (read first)
- **Pending verification:** `CrossTenantApiTests.cs` (5 HTTP cross-business tests for customers, orders, invoices, payments, receipts, tasks) was added after run #243 and has not been compiled or run; expect 709 backend tests if all pass.
- **Newest CI evidence (2026-10-16, run #243, commit `1409b30b6a424dac763b0a5b1fa9c9dd57a3b7da`):** backend, web and migrations all succeeded. Backend: 0 warnings/errors, **704/704 tests**, model-drift gate clean. Web: 0 audit vulnerabilities, typecheck clean, **162 Vitest tests (9 files)**, Next 15.5.28 build compiled (route table in the supplied log was truncated after `/orders/new`). Migrations job: succeeded, counts not seen. See the handoff for gaps.
- **Latest CI evidence (2026-10-16, run #238, commit `40306ac4305531c97270b11be655257a2d3defae`):** backend, web and migrations jobs all reported **succeeded** on the run page. The pasted logs had collapsed steps, so exact test counts, Vitest/build summaries and migration schema counts were **not seen**. For reference, runs #236 and #237 executed 704 backend tests (675 passed, 29 failed) before the fixes below; do not quote 704/704 for #238. Runs #231-#237 failed on missing `using` directives (`ShopScheduleService`, `CustomerPortalController`), `decimal.IsFinite` in `InventoryService`, a strict-index TS error in `StorefrontManager.tsx`, and relationship blocks ordered before their principal entity in `AppDbContextModelSnapshot.cs` and the `AddInventory`, `AddCustomerPortalGrants` and `AddSubscriptionPayments` Designer files. Fixes changed code and model descriptions only; no migration SQL was edited. Open: `@types/node` below Vite's optional peer range. Statically resolved, not compiled: CS9113 (`CustomerPortalController` has no `tenant` parameter in this archive); generate-migration default name now `AddNextChange`. The #230 entry below is historical.
- **Latest supplied CI evidence (2026-10-16, run #230, commit `c349faf9023a9a0c1c6d5e7ddb5950c76016953c`):** backend job succeeded. .NET 10 Release build: **0 warnings / 0 errors**; integration tests: **693 passed / 0 failed / 0 skipped**; EF model-drift gate: `Committed migrations match the model`. The `backend-test-results` artifact was uploaded. The web job succeeded; `npm ci` and `npm audit` completed with **0 reported vulnerabilities**, but the log excerpt supplied did not show the exact Vitest count or typecheck/build summaries. `npm ci` emitted a peer warning: `@types/node@22.10.7` is below Vite 8.3.4's declared `>=22.12.0` range. The migration job output for #230 was not supplied, so clean-PostgreSQL migration application is not confirmed for this run.
- **Archive inspection:** the supplied ZIP has no `.git` metadata, so branch, HEAD, remotes, worktree changes and slice ancestry cannot be independently established. The archive DOES contain `web/src/app/(app)/orders/[id]/page.tsx`; the older #226 note below saying the detail route was missing is stale for this archive. The web app currently has login/register/invite, staff/roles/locations, customer workflows, order list/detail/create/edit and quote/confirm/cancel UI. The latest working-copy increment also adds a read-only production task dashboard for managers and assigned workers; this increment has not been included in CI.
- **Local verification limitation:** this environment has Node 22 but no .NET SDK or Docker. `npm ci` timed out before installing dependencies; a subsequent `npm run typecheck` therefore failed because Next/React/Vitest/Node type packages were absent. That is an environment/dependency-install blocker, not a meaningful typecheck of the changed code. No local Vitest or Next production build was completed. Use CI to verify the changed web code.
- **Still not verified end-to-end:** Docker Compose/full-stack smoke test (explicitly deferred until dashboard and storefront editor are ready), storefront publish-to-render, real email delivery, S3, ClamAV, real Redis, DNS, payments, push, AI, mobile builds, and Data Protection key persistence across the deployed Render API's restart.
- **Migrations:** the historical run #207 applied eight migrations to clean PostgreSQL 17.11 (67 tables, 33 check constraints, 113 FKs, 8 partial indexes, no cascading FKs on financial/audit tables). Run #230's migration job has not been shown. Never regenerate `InitialSchema`; use forward migrations only.
- **Dependency maintenance:** `web/package.json` pins Next.js 15.5.28, React `^19.2.8`, Vitest 4.1.11 and a PostCSS override. `web/package-lock.json` is committed and CI uses `npm ci`. Upgrade `@types/node` to a compatible Node 22 release (`>=22.12.0`) when dependencies are next edited; regenerate the lockfile and rerun CI.

Legend: [x] written  |  [ ] not started  |  "unverified" = written but never compiled or run

## Phase 1 - Foundation, auth, tenancy, staff, permissions
- [x] Repo structure, central package versions (unverified), Dockerfile, compose, .env.example
- [x] Domain entities, permission catalogue, default roles
- [x] AppDbContext: tenant query filters, write guard, composite FKs (unverified)
- [x] Business registration (creates business, default location, default roles, owner membership) (unverified)
- [x] Login / logout / me, lockout, cookie session, CSRF, rate limiting (unverified)
- [x] Staff invitations, accept invitation (new accounts only), deactivate/reactivate (unverified)
- [x] Roles + custom roles + permission editing, locations (unverified)
- [x] Audit log writes for every mutation (unverified)
- [x] Integration tests for tenant isolation against real PostgreSQL (written, never run)
- [x] Compile, EF migration generation and tests: green in CI run #37 (see Verification status). Still to do: `./scripts/bootstrap-backend.sh` locally and inspect the generated migration SQL
- [x] Audit log read endpoint (`audit.view`): `GET /api/audit`, `AuditReadTests` (5) pass in CI #115
- [ ] Email verification, password reset / account recovery
- [x] Join an invitation with an existing account (`AccountTests`, pass in CI #115)
- [x] API tests for authorization (WebApplicationFactory): `ApiAuthorizationTests` (7) pass in CI #115 after the multipart fix
- [ ] Next.js app: register, login, accept-invite, staff & roles screens
- [ ] Mobile/bearer token flow (Phase 7)

## Phase 2 - Customers, measurements, orders
- [x] Customers: CRUD, search/filter/sort/paging, status, optimistic concurrency (unverified)
- [x] Duplicate detection + explicit override, merge with data move (unverified)
- [x] Communication log, fitting appointments (unverified)
- [x] Measurement templates (6 starters, custom fields/units/ranges), validated values (unverified)
- [x] Append-only measurement revisions, history, comparison (unverified)
- [x] Orders: server-side totals, DB check constraint, atomic order numbers, idempotent create (unverified)
- [x] Frozen measurement snapshots per garment (unverified)
- [x] Draft -> Quoted -> Confirmed / Cancelled with guarded transitions (unverified)
- [x] 13 integration tests in `Phase2Tests` (written, never run)
- [ ] **Compile, create the migration (re-run bootstrap script), run all tests** - needs .NET 10 SDK + Docker
- [ ] Payments / deposits / outstanding balance (Phase 6)
- [ ] Attachments: garment photos, design references (needs object storage)
- [ ] Customer notifications (Phase 5)
- [~] Location-scoped staff access: orders, fittings and order search written 2026-10-15 (unverified, see Step A section). Invoices, payments and receipts are scoped since 2026-10-15 (see Step A finance section); tasks are NOT scoped yet
- [ ] Production-task generation on confirm (Phase 3)
- [~] API-level authorization: the generic endpoint-surface test (every endpoint protected, 401 anonymous) exists; per-endpoint behavioural tests do not
- [ ] Next.js screens: customers, measurement forms, order builder

## Phase 3 - Workflows, tasks, dependencies, assignment
- [x] Configurable workflows: stages, prerequisite graph with cycle detection, skills, checklists, conditional steps (unverified)
- [x] Default 10-stage workflow seeded at registration (unverified)
- [x] Task generation on order confirm, same transaction; manual / per-garment-workflow generation (unverified)
- [x] Ad-hoc tasks with prerequisites; manager edit with concurrency; cancel; unblock (unverified)
- [x] Assignment with skill + workshop validation; audited override needing permission and reason (unverified)
- [x] Worker actions: start, pause, resume, complete (checklist), report issue; one task in progress; ownership enforced (unverified)
- [x] Alterations flow (conditional step re-opened from the fitting) (unverified)
- [x] Order status derived from tasks; Ready/Delivered guards; cancel cascades; derived progress (unverified)
- [x] Derived overdue/eligible flags; append-only task history (unverified)
- [x] Worker skills management (unverified)
- [x] 9 pure unit tests (`ProductionDomainTests`) and 19 integration tests (`Phase3Tests`) (written, never run)
- [ ] **Compile, create the migration (re-run bootstrap), run all tests** - needs .NET 10 SDK + Docker
- [~] First-pass pure scheduling algorithm added in `Tailoring.Domain.Production.ProductionSchedulePlanner`; tests written but not run. API integration, persistence, worker availability/leave, capacity rules and schedule UI remain outstanding (Phase 4)
- [ ] Notifications for assignments, blocks, overdue, newly eligible steps (Phase 5)
- [ ] Material reservations / shortage handling against stock (Phase 6)
- [ ] Progress photo attachments
- [ ] Amendments to confirmed orders
- [~] API-level authorization: generic endpoint-surface test exists; per-endpoint behavioural tests do not
- [ ] Next.js screens: workflow editor, task board, worker task list

## Step 1 (master prompt, Phase 1 completion) - Plans and entitlements
- [x] Tables: Plans, PlanEntitlements, TenantSubscriptions (1 per business), TenantEntitlementOverrides, UsageCounters (unverified)
- [x] Entitlement catalogue (`EntitlementCatalog`): the single list of gates; 8 limits, 6 flags, 6 tiers, incl. storefront template/theme/branding/section/page/domain keys (unverified)
- [x] Pure resolver `EffectiveEntitlements`: tenant override (unexpired) > plan > restrictive default; corrupt values fail closed (unverified)
- [x] Initial plans seeded once from spec values, then admin-owned; the seeder only ADDS new keys, never overwrites (unverified)
- [x] `IEntitlementService`: flags, tiers, stock limits (advisory lock, must be in a transaction), periodic limits (one atomic conditional upsert) (unverified)
- [x] Enforced today: active customers (create + reactivate), staff seats (invite, accept, reactivate), orders/month, fittings/month (unverified)
- [x] Registration puts every business on Free; expired subscription behaves as Free; downgrade never deletes data (unverified)
- [x] `GET /api/plans` (public, pricing page source) and `GET /api/billing/entitlements` (members) (unverified)
- [x] 7 pure rule tests + 9 integration tests incl. concurrency (`EntitlementTests`) (written, never run)
- [ ] **Compile, re-run the bootstrap script to generate the migration, run all tests**
- [ ] Platform-admin endpoints to edit plans/overrides (Phase 9; needs a platform-admin identity). Until then edit via SQL
- [ ] Enforce designs.max / storage / storefront keys when those modules exist
- [ ] Billing-period anchored quota windows (currently calendar month, UTC; seam is `UsagePeriod`)
- [ ] Custom-domain flag is false in every seeded plan on purpose; flip when the feature ships
- Defaults NOT in the spec (mine, editable): pages per storefront 1/3/8/20, custom pages Pro+, reports tier per plan
- Note: production must set `Billing__SeedPlansOnStartup=true` once, after migrations (Development seeds automatically)

## Step 2a (master prompt, Phase 2) - Design library
- [x] Designs: kind (ready-made / made-to-order / service), category, tags, fabrics/colours/sizes, optional price (rounded, currency-stamped), production days, featured, variations via parent design (unverified)
- [x] Stable per-business slugs (generated once; rename never moves the URL); unique index; race -> `slug_conflict` (unverified)
- [x] Private by default; Public only while Active; archiving forces Private; the database CHECK enforces it too (unverified)
- [x] Counts against `designs.max` (Active only); restore is limited; advisory-locked, so concurrent creates cannot overshoot (unverified)
- [x] Optimistic concurrency (`stale_version`), tenant isolation incl. composite FKs for category/parent (unverified)
- [x] Search (ILIKE, wildcards literal), filters (category, kind, visibility, status, featured, tag), categories CRUD (unverified)
- [x] Permissions `designs.view` / `designs.manage` (Owner + Manager get them for NEW businesses) (unverified)
- [x] 6 slug unit cases + 12 integration tests (`DesignTests`) (written, never run)
- [ ] **Compile, re-run bootstrap to regenerate the migration, run all tests**
- [ ] Existing businesses' Manager roles lack the two new permissions until edited (Owner is unaffected)
- [ ] Design images / sketches / attachments: need the media + object-storage step (next)
- [ ] Collections and featured-collection ordering (with the media step)
- [ ] Design-to-order link (OrderGarment.DesignId + snapshot of name/price at order time)
- [ ] Next.js screens: design list, editor, categories

## Step 2b (master prompt, Phase 2) - Media uploads and design images
- [x] `MediaAsset` + `DesignImage` tables; composite FKs keep media and designs inside one business (unverified)
- [x] Upload pipeline: bounded read (12 MB), magic-byte sniffing (file name and declared type ignored), only JPEG/PNG/WebP (no SVG, no GIF), pixel cap checked from the header BEFORE decoding, concurrency gate on decoding (unverified)
- [x] Every image is decoded and RE-ENCODED to WebP in three variants (orig <= 2560px, md 960, sm 320): EXIF/GPS, ICC, XMP and any appended bytes are dropped (unverified)
- [x] Server-generated storage keys only; `LocalDiskObjectStorage` refuses traversal and writes atomically; failed uploads delete what they wrote (unverified)
- [x] Plan storage limit (`storage.gb.max`) enforced with the same advisory-lock pattern; deleting frees space; `GET /api/media/usage` (unverified)
- [x] Authenticated, tenant-scoped file route; anonymous `/public/media/{id}/{variant}` serves ONLY images on Public + Active designs, everything else is an identical 404; nosniff + cache headers; rate limits (`upload`, `public`) (unverified)
- [x] Design galleries: ordered, cover = first, alt text, replace-all, max 10, archived designs locked; list shows `CoverMediaId` (unverified)
- [x] Images in use cannot be deleted (`media_in_use`) (unverified)
- [x] 6 processor tests, 9 storage cases, 11 integration tests (written, never run)
- [ ] **Compile, restore packages (ImageSharp 3.1.7 is my guess: bump to the latest patched 3.1.x), regenerate the migration, run all tests**
- [ ] LICENSE CHECK: SixLabors ImageSharp is under the Six Labors Split License (free for open source / small companies, paid above a revenue threshold). Confirm it fits Avzar, or swap `IImageProcessor` to SkiaSharp (MIT)
- [ ] NO malware scanning. Re-encoding disarms image payloads; documents/PDF uploads are not accepted at all yet
- [ ] S3-compatible `IObjectStorage` (production), orphan-file cleanup job, CDN in front of `/public/media`
- [ ] Browser-side: the web app must send `X-XSRF-TOKEN` on the multipart upload
- [ ] Logo / banner / favicon / share-image purposes arrive with the storefront builder
- [ ] Collections (next), design-to-order link, Next.js screens (gallery uploader)
- Compose now seeds plans (`Billing__SeedPlansOnStartup=true`) and mounts a media volume; migrations must still be applied first

## Step 2c (master prompt, Phase 2) - Design collections
- [x] Collections: name, stable slug, description, Private/Public, sort order, ordered designs (max 100), optimistic concurrency (unverified)
- [x] A Public collection's summary reports `PublicDesignCount` (Public + Active only) next to `DesignCount`, so the UI can warn "3 of 8 are hidden" (unverified)
- [x] Cross-business designs rejected at service level and by composite FKs; archived designs cannot be added; archiving a design keeps it in the collection but stops it showing publicly (unverified)
- [x] Deleting a collection never touches its designs (unverified)
- [x] 8 integration tests (`CollectionTests`) (written, never run)
- [ ] **Compile, regenerate the migration, run all tests**
- [ ] No plan limit on collections yet (add a catalogue key if you want one)
- [ ] Still open in Phase 2: design-to-order link, quotations + pricing calculator, invoices/receipts/payments, expenses, Next.js screens

## Step 3 (master prompt, Phase 2) - Pricing calculator and design-to-order link
- [x] Pure `PriceCalculator` (decimal only, per-line half-away-from-zero rounding): materials, labour (explicit or default hourly rate), complexity multiplier (labour only), other/outsourced costs, overhead % (on materials + labour), markup OR margin target, round-up to a step, tax shown separately (unverified)
- [x] Markup (% of cost) and margin (% of price) are separate; both are always reported. Margin target capped at 95% (100% is impossible) (unverified)
- [x] Labour is never silently free: hours with no rate and no default rate are rejected (unverified)
- [x] Per-business `PricingProfile` (one row, versioned, DB range checks); absent profile = starting defaults and `IsConfigured=false` (unverified)
- [x] `POST /api/pricing/calculate` (stateless), `GET/PUT /api/pricing/profile`; permissions `pricing.view` / `pricing.manage` (costs and margins are sensitive) (unverified)
- [x] Order lines can carry `Pricing`: the SERVER recomputes it and stores a frozen snapshot (inputs, rules, result); a final price different from the calculated one needs a reason; rule changes never rewrite history (unverified)
- [x] Costs/margins are NOT in `OrderDto`; only `GET /api/orders/{id}/pricing` (pricing.view) returns them; supplying `Pricing` on an order needs pricing.view at service level (unverified)
- [x] Order lines can link a catalog design (`DesignId`): Active designs of the same business only; name + price are snapshotted and survive rename/archive; unlinking clears them (unverified)
- [x] 13 calculator tests (hand-worked example, 500-case property check) + 11 integration tests (`PricingTests`) (written, never run)
- [ ] **Compile, regenerate the migration, run all tests.** OrderService now takes `IPricingService` + `IPermissionService`
- [ ] Tax is shown by the calculator but not yet applied to orders (order totals are tax-free until invoicing)
- [ ] Per-design default cost recipes (a saved material/labour template per design) are not built
- [ ] Next: invoices, receipts and payments (with tax), then expenses, then the Next.js screens for Phase 2

## Step 4 (master prompt, Phase 2 completion) - Invoices, payments, receipts, credit notes, expenses
- [x] Invoices: draft -> issued -> void; numbers (`INV-2026-00001`) minted atomically only at issue (no gaps from abandoned drafts); totals computed by the server; per-line discount then tax; due date defaults to issue + 14 days (unverified)
- [x] Bill an order: Full (itemised, order discount split to the cent), Deposit (percent or amount), Balance; billed amount can never exceed the order total; drafts count, void frees it; only Confirmed+ orders; order-lock via advisory lock; idempotency keys (unverified)
- [x] Payments: a claim is Pending and counts for nothing; only Verified payments reduce the balance; "mark as paid" does not exist; verify/reject/reverse need `payments.verify`; verified payments name their verifier (DB CHECK); overpayment refused at record AND verify; idempotent record; advisory-locked so concurrent payments cannot double count (unverified)
- [x] Receipts exist only for verified payments, immutable, voided (not deleted) on reversal, with balance-after (unverified)
- [x] Credit notes (reduce balance, never below zero, numbered `CN-`), void rules (no live payments/credits) (unverified)
- [x] Order cancel is blocked while live invoices exist (unverified)
- [x] Plan gates: deposits/partial payments/credit notes (`finance.partial_payments`) and expenses (`finance.expenses`) start at Starter; Free must pay in full (unverified)
- [x] Expenses: default categories per business, deactivate-not-delete, void-with-reason, versioned edits, no future dates, summary by category (money out only, never profit) (unverified)
- [x] Default `Accountant` role (money only); permissions invoices/payments/expenses view/manage + payments.record/verify (unverified)
- [x] Tests: 8 pure invoice-math groups + 31 finance integration tests + 6 expense tests (written, never run)
- [ ] **Compile, run the bootstrap (one fresh migration), run all tests** (see docs/RUNBOOK.md)
- [ ] PDF documents, emailing invoices/receipts (needs a real email sender)
- [ ] One payment can cover only one invoice (no multi-invoice allocation yet)
- [ ] Customer-uploaded payment proof (storefront/portal phases create Pending payments through the same service)
- [ ] Quotations are order status Quoted (no separate document); a printable quote comes with PDF generation
- [ ] Reports (revenue vs cash vs receivables vs profit) are Phase 5

## PHASE 2 BACKEND STATUS: feature-complete as written; UNVERIFIED until compiled and tested
Web screens for Phase 2 do not exist (no Next.js app yet).

## Phase 3 - Storefront builder: backend + public renderer (unverified until compiled)
- [x] Layout schema: a storefront is ONE validated JSON document (theme, branding, navigation, pages, sections, archive). Strict parsing (unknown property = error); allow-listed section types/variants/fonts; #RRGGBB colours only; bounded numbers; https-only external links; plain text with no angle brackets; every media/collection/category id checked against the tenant
- [x] 8 structurally different templates (standard, bespoke, ready-to-wear, luxury, african-heritage, bridal, streetwear, atelier-studio); 6 theme presets; 16 section types, 8 built-in presets + per-business custom presets
- [x] Plan gates enforced server-side on save AND publish: template tier, page count, custom pages, advanced section types, section styling, theme level (none/basic/advanced/full), branding level, builder flag, storefront count, custom domain; a lower plan can remove advanced content but not add more
- [x] Draft vs published: draft invisible to visitors; publish writes an immutable version; rollback publishes a COPY of an old version as a new one (history only grows) and resets the draft; revision check stops silent overwrites
- [x] Template switching: preview with kept/converted/archived lists, confirmation token bound to that exact plan, nothing deleted (unsupported sections go to a restorable archive), switching back restores them; theme untouched unless asked
- [x] Publishing blocked if an enabled workflow (inquiry/booking/custom order/contact) has no visible entry point; contrast warnings (WCAG) with one-click fix suggestions
- [x] Public API (no login): published snapshot only, hidden pages/sections/archive stripped, public+active designs only, collections, categories, sitemap/robots (own address only), preview links (time-limited, unguessable), custom-domain resolve + "allowed" endpoint for on-demand TLS
- [x] Site images public only while in the LIVE version; cannot be deleted while any version/draft uses them
- [x] Inquiries/quotes/order requests/appointments/contact: work under every template, honeypot + per-IP rate limits + optional Cloudflare Turnstile; staff list/review/reject/convert (finds or creates the customer, makes a DRAFT order, idempotent)
- [x] Custom domains: DNS TXT proof, verified-only, disabled by default in every plan, stops resolving if the plan loses the feature
- [x] Next.js public renderer (web/): host to site routing, strict nonce CSP, controlled section components, forms, SEO metadata, JSON-LD, sitemap/robots, preview
- [x] Tests: 33 pure storefront tests + 25 integration tests (incl. the acceptance scenario: two businesses, different templates/pages/colours/fonts, no leakage) + 22 web unit tests (written, never run)
- [ ] **Compile, run `dotnet test`, `npm install && npm run typecheck && npm test` in web/**
- [~] Staff dashboard is partially implemented (auth, staff/roles/locations, customers, and order list/detail/create/edit with quote/confirm/cancel UI; current order UI awaits a fresh CI typecheck/test/build). A first storefront draft editor now exists (draft/appearance/publish/version history and section presets); the polished drag-and-drop canvas, undo/redo, media/domain UI and template-switch confirmation remain open.
- [ ] Not built: pricing page UI, onboarding wizard, customer portal
- [ ] Data Protection keys must be persisted in production (preview links) - see RUNBOOK
- [ ] Fonts load from Google Fonts (CSP allows it); self-hosting is a privacy hardening step
- Page limits default to 5/5/12/25 so each plan can hold its templates' own pages (earlier 1/3/8/20 would not)

## Phases 4-8
Not started or incomplete: scheduling engine and daily plans, notifications/SignalR/Hangfire, inventory/stock reservations, MAUI app/offline sync, completion of the storefront editor, customer portal, platform admin and deployment hardening. Storefront APIs and public renderer are implemented; the editor is an initial JSON-based slice.


## Step A - Foundation gaps (in progress; everything below is unverified: no .NET SDK or Docker was available)
- [x] Durable email outbox, dispatcher with retries/backoff/dead-letter, lease-based multi-instance safety, HTML+text templates (staff invitation, verify email, password reset), SMTP and Resend transports, startup config validation (unverified; `EmailTests.cs` written, never run)
- [x] Staff invitation now queues through the outbox using the template (unverified)
- [ ] **Regenerate the single `InitialSchema` migration** (new `EmailOutbox` table)
- [ ] Email provider webhooks for delivered/bounced status
- [ ] Email verification, password reset, optional TOTP 2FA, session/refresh-token management, account export/deletion
- [ ] JWT + rotating refresh tokens (mobile)
- [x] S3-compatible storage (`S3ObjectStorage`, `Storage:Provider=s3`), orphan-file cleanup (`OrphanFileCleaner`), antivirus interface (`IVirusScanner`: NoOp + ClamAV), document uploads (`DocumentsController`, `DocumentSniffer`); tests in `StorageMaintenanceTests.cs` and `DocumentTests.cs`. Compiled and the suite passed in CI #37; not exercised against a real bucket or ClamAV (blocked by external configuration)
- [ ] Redis
- [ ] PDF generation
- [ ] CSV import/export, global search
- [ ] Working hours / holidays
- [x] ImageSharp advisory resolved: upgraded to 4.1.2 (needs a Six Labors license at build time, see docs/RUNBOOK.md section 8). Waiting on the license key before backend CI can go green again.
- [x] Static review (reading, not running) of raw SQL (EntitlementService, FinanceSupport, OrderService, EmailDispatchService) and the large EF projections (InvoiceService, DesignService, CollectionService, PublicSiteService): no defects found; queries are parameterised; patterns are ones EF Core/Npgsql translate. Perf note: InvoiceService.ListAsync repeats correlated subqueries for count and page; fine at current scale.

## Step A (handoff) - Account security, sessions, business hours  (all UNVERIFIED: nothing here has been compiled or run)
- [x] Email verification: link emailed at registration (best effort) and on request; `POST /api/auth/confirm-email`, `POST /api/account/resend-verification`; per-address throttle (checks `EmailOutbox`); optional `Auth:RequireVerifiedEmail` (default OFF so nobody is locked out)
- [x] Password reset: `POST /api/auth/forgot-password` (always 202, no account enumeration), `POST /api/auth/reset-password`; own token provider so reset links last `Auth:PasswordResetMinutes` (30) while confirmation links last hours; a reset ends all device sessions and clears lockout
- [x] Change password (`POST /api/account/change-password`): ends device sessions, keeps the current browser signed in
- [x] TOTP two-factor with Identity's authenticator provider + 10 single-use recovery codes: setup/enable/disable; login accepts `twoFactorCode` or `recoveryCode`. Lockout is now handled by hand in `AuthService` (the old `CheckPasswordSignInAsync` reset the failure counter on a correct password, which would have allowed unlimited 2FA guessing)
- [x] Mobile bearer auth: `POST /api/auth/token` (sign in), `/refresh`, `/revoke`. 10-minute JWT access tokens + rotating refresh tokens (`UserSessions` table, hashes only). Reuse of an old refresh token revokes the session; parallel refresh is serialised by xmin. Every bearer request checks the session is still active, so revoking a device is immediate. Enabled only when `Auth:JwtSigningKey` (32+ chars) is set
- [x] Session management: `GET/DELETE /api/account/sessions`, `POST /api/account/sessions/revoke-all` (device sessions only; web cookie sessions are not listed)
- [x] `GET /api/account/export` (profile, memberships, device sessions) and `POST /api/account/delete` (password + 2FA; blocked for business owners; staff are anonymised, memberships deactivated, financial/audit rows kept)
- [x] Business hours (7 days, DB check constraints) and closed dates: `GET/PUT/POST/DELETE /api/business/schedule...`, permission `business.settings`
- [x] `CsrfFilter` replaces `AutoValidateAntiforgeryToken`: same rule, plus requests carrying `Authorization: Bearer` are exempt (a bad bearer token never falls back to the cookie)
- [x] Web cookies now re-validate the security stamp every minute (was 30) so resets and deactivation bite quickly
- [x] Test fix: `FakeUserTenant` was defined twice (Phase3Tests and PricingTests) which would not compile; one definition now
- [x] Tests written (never run): `AccountTests.cs` (session rules, hours rules, verification, reset, throttle, 2FA, refresh rotation/reuse/parallel, sessions, export, deletion, business hours + isolation + concurrency)
- NEW PACKAGE (version is a guess, bump to latest 10.0.x): `Microsoft.AspNetCore.Authentication.JwtBearer 10.0.0`
- NOT DONE from Step A: Redis, PDF generation, contact/CSV import-export, global search, join-with-existing-account, ownership transfer, TOTP replay protection (a code can be reused inside its ~90 s window), per-user 2FA attempt rate limit beyond lockout
- Schema changed (UserSessions, BusinessHours, BusinessHolidays): re-run `./scripts/bootstrap-backend.sh` after deleting `Persistence/Migrations`


## Step A (final slice) - Redis rate limits, PDFs, customer CSV, search  (all UNVERIFIED: nothing here has been compiled or run)
- [x] Redis (optional, `Redis__ConnectionString`): the `auth`/`upload`/`public-submit`/`public` rate limits are counted in Redis so they hold across API replicas; Redis failure falls back to per-process limits; `/health` reports Degraded. Tested with a fake counter only (`RedisLimiterTests.cs`). NOT done: caching, queues, SignalR backplane (Step D). NEW PACKAGE (version a guess): `StackExchange.Redis 2.8.16`
- [x] PDFs: invoice, receipt, credit note, quotation (order), measurement sheet, job sheet (manager and worker variants). Pure document builders (`PdfDocuments`, tested in `PdfTests.cs`), QuestPDF renderer, endpoints guarded by the permission of the record. NEW PACKAGE (version a guess): `QuestPDF 2025.7.0`. Requires an explicit `Pdf__QuestPdfLicense` decision (see RUNBOOK section 9). NOT done: logos/brand colours, email/share by link (deferred to the customer portal in Step B)
- [x] Customer CSV import/export: preview then commit with file hash, create-only default, typed confirmation before overwriting, plan limits respected, formula-injection-safe export (`ImportExportTests.cs`). Only customers; other record types are not importable
- [x] Global search across customers, orders, invoices, designs, inquiries, filtered by the caller's permissions (`SearchTests`)
- Historical status at this point in the log (superseded by later implementation and CI evidence): the remaining Step A list included email webhooks, existing-account invitations, ownership transfer, TOTP replay protection, PDF branding/share links and Redis caching/queues. See the current verification summary at the top and later dated CI entries for the reconciled state.


## Session 2026-10-15 (handoff V2 baseline review; read-only, no code changed)
Environment: no .NET SDK, Docker, `.git` or npm network access, so **no build, test or install was run**. Done by reading only:
- Rewrote `README.md` as a product README; claims cross-checked against `PROGRESS.md`, `docker-compose.yml`, `Directory.Packages.props`, `Program.cs` and the CI workflow.
- CI run #64 (commit `889afec`) is reported successful from the supplied run page (backend 1m52s, web 47s). Its job logs were not available, so the license gate passing is inferred from the workflow (the step exits 1 on an empty secret), not observed.
- Found: there is no root `ci.yml` (only `.github/workflows/ci.yml` and `generate-migration.yml`), so the suspected duplicate does not exist in this snapshot.
- Found: integration tests use `EnsureCreated` (`TenantIsolationTests.cs`), so CI has never applied a migration to PostgreSQL; CI only checks that `dotnet ef migrations add` succeeds. (Stale: a `Migrations` folder with `InitialSchema` is now committed.)
- Historical 2026-10-15 finding (superseded by later web slices): at that time `web/` contained only the public renderer and preview; the staff dashboard and editor had not been built.
- Next: (1) in a network-enabled environment, clone `main`, confirm 889afec; (2) decide the migration baseline and apply it to a clean PostgreSQL; (3) compose smoke test; (4) WebApplicationFactory authorization tests; (5) upgrade Actions majors after checking releases; (6) begin dashboard/editor.

## Artifact review 2026-10-15 (CI run #64 artifacts; static inspection, nothing re-run)
- `backend-test-results`: `results.trx` reports 478 total, 478 executed, 478 passed, 0 failed/error/not-executed. Test window 18:00:48-18:01:34 UTC on 2026-10-15; the migration artifact is stamped 18:01:53, consistent with run #64's backend job. Run/commit provenance is not stored inside the TRX, so the link to #64 is by timing and by your upload, not proven.
- `generated-migration` (`20261015180153_CiCheck`): 64 `CreateTable` calls, 181 `CreateIndex`, 31 check-constraint references, 6 filtered indexes, 50 composite `(Id, BusinessId)` foreign keys. Tables include EmailOutbox, UserSessions, BusinessHours, BusinessHolidays, Documents, i.e. the model matches the post-#37 schema.
- Delete behaviour on financial/audit tables (read from the migration): AuditLogs, Expenses, Orders, Invoices, CreditNotes, Payments, Receipts all use `Restrict` toward their parents. Only child-line tables cascade (`OrderGarments` from Orders, `InvoiceLines` from Invoices). This supports, but does not prove, the "never delete financial records" rule; it was not exercised against a live database.
- Still not verified: the SQL was never applied to PostgreSQL (tests use `EnsureCreated`), filtered-index semantics, and concurrency behaviour in the live schema.


## CI run #85 (2026-10-15, after the Step A batch and location scoping)
- Build compiled. 527 tests: 526 passed, 1 failed. The failure was `Phase2Tests.Measurement_snapshot_stays_frozen_when_the_customer_is_remeasured`: a pre-existing flaky assertion that searched the whole snapshot JSON for "108", which also matched the random microseconds in `recordedAtUtc`. Fixed by asserting only on the snapshot's `values`. Not a product-code defect. Not yet re-run.
- CI run #86 (push 16cbde4, the flaky-test fix): reported Success (web and backend jobs). Test counts were not read from the artifacts.
- Migration check #2 FAILED (2026-10-15): `dotnet ef database update` refused with `PendingModelChangesWarning`: the model has changes the committed `InitialSchema` migration does not contain (known: `AspNetUsers.LastTotpStep`). This confirms the stale-migration finding above. The later "Generate initial migration #3" run was green only because that workflow exits early when a Migrations folder exists, so it changed nothing.
- Fix prepared, NOT yet run: `generate-migration.yml` now adds a forward migration (input `migration_name`, default `AddLastTotpStep`) when migrations already exist, prints its operations, and commits it. Then re-run Migration check.
- Found by inspection (confirmed by Migration check #2, see above): the committed `20261015214018_InitialSchema` migration and snapshot do not contain `AspNetUsers.LastTotpStep` (tests use `EnsureCreated`, so CI cannot see this). Regenerate the migration before any database is built from migrations.

## Step A remaining gaps - code written 2026-10-15 (UNVERIFIED: not compiled, not run; no .NET SDK in the authoring environment. CI is the first check.)
- [x] **Audit-read endpoint**: `GET /api/audit` (`audit.view`), paged, newest first, filters (action, entity type, entity id, actor, from/to UTC; `to` exclusive), actor name resolved. Read-only: no write or delete path. Tenant-filtered. Tests: `AuditReadTests` (isolation, no-tenant, paging/order/actor name, filters). Note: `DetailsJson` is returned as stored, so it is visible to anyone with `audit.view`; review what call sites put in details before widening that permission.
- [x] **TOTP replay protection**: `Totp`/`TotpVerifier` (RFC 6238, same +/-2 step window Identity used) record the newest accepted time step in `AspNetUsers.LastTotpStep` with one conditional UPDATE, so a used code, an older code, or two parallel requests with the same code are rejected. The enabling code is consumed too. The column is marked never-written-by-normal-saves so Identity's whole-row updates cannot reopen a used code. Cleared when the authenticator key is replaced (disable). **Schema change: new `LastTotpStep` column on AspNetUsers.** Tests: `TotpTests` (RFC vectors, checked independently in Python, window, malformed input, base32) and in `AccountTests` (replay, parallel, key replacement). Side effect to expect: a user must wait for the next 30 s step to reuse 2FA after enabling or disabling.
- [x] **Per-user 2FA attempt limit**: already covered, not changed. A wrong second factor calls `AccessFailedAsync`, so it feeds the same 5-attempts/15-minute lockout as passwords (`AuthService`, `AccountService`). Recorded here because the earlier "NOT DONE" line was misleading.
- [x] **Join an invitation with an existing account**: `POST /api/auth/accept-invitation/existing` (signed in). Only the signed-in owner of the invited email address can use the link; single-use, expiry, revocation, seat limit and already-a-member checks apply; deleted accounts refused; unique (business, user) index backstops races. The new-account route still returns `account_exists` for known emails (message updated). Shared logic factored out of `AuthService`. `TokenHasher` is now public (tests use it). Tests in `AccountTests` (8). Invitation acceptance had no tests before this.
- [x] **Business ownership transfer**: `POST /api/business/ownership-transfer`. Current owner only, must re-enter the password (and a 2FA/recovery code if enabled; failures count toward lockout). Target must be an active member of the same business. Runs under a per-business advisory lock inside one transaction: target becomes owner with the Owner role, previous owner becomes a Manager, audited. Tests in `AccountTests` (success, wrong password, non-owner, invalid targets, 2FA, racing transfers). NOT done: notification emails to both parties, an at-most-one-owner database constraint (enforced by lock and transaction only).
- [~] **Location-scoped staff access (partial)**: new permission `locations.all`. Owners and holders of it see every location; everyone else sees only the locations in their membership, and a member with no locations sees nothing (fail closed). Applied to **orders** (list, get, pricing, create, update incl. moving location, quote/confirm/cancel/deliver, idempotent replay), **fittings** (list, create, set status, linking an order) and the **orders section of global search**. A hidden record returns 404 (order_not_found / fitting_not_found), creating or moving into a forbidden location returns 403 `location_forbidden`. Default roles keep their old reach: Owner and Manager (everything), Accountant (given `locations.all`); production roles never had order access. Calls with no signed-in user (public/system flows such as the storefront inquiry path) are unscoped. Implementation: `ILocationScope`/`LocationScope`, injected as an OPTIONAL trailing constructor parameter (null = unscoped, so existing tests compile unchanged; production DI registers it). Tests: `LocationScopeTests` (15). **No schema change.** NOT scoped yet: customers (no location column), invoices, payments, credit notes, receipts, production tasks and the order count on the customer list, customer merge (moves fittings across locations; needs `customers.merge`). Existing businesses created before this change have stored Manager/Accountant roles WITHOUT `locations.all`: add it via the role-permissions endpoint or those roles become location-restricted.
- Still NOT done from Step A: email provider webhooks (delivered/bounced), PDF logos/brand colours, Redis caching/queues/SignalR backplane, remaining CSV import/export (only customers), scoping of invoices/payments/tasks/customers by location, `WebApplicationFactory` API authorization tests (they would also catch a missing `ILocationScope` registration, which the optional parameter would otherwise hide), background-processing hardening.
- To verify: push and read the CI result; a compile error in any of the files above is the most likely failure. Then re-run the migration check (new column).


## Session 2026-10-15 (migration drift gate; UNVERIFIED until CI runs)
- Hand-written forward migration `20261015230000_AddLastTotpStep` (adds nullable `bigint` `AspNetUsers.LastTotpStep`), with Designer file and updated `AppDbContextModelSnapshot.cs`. Written without a .NET SDK, so it has not been compiled or applied. Check: run **Migration check**; if it still reports `PendingModelChangesWarning`, read the log for what else the snapshot lacks. Do not also run "Generate initial migration" (it would add a duplicate).
- `ci.yml`: the old "Check EF model produces a migration" step passed even when migrations were stale (it only proved `migrations add` runs). Replaced by `dotnet ef migrations has-pending-model-changes`, which fails the backend job when the model and committed migrations differ and uploads the missing migration as the `pending-migration` artifact. The `generated-migration` artifact is gone. Not yet run: the first CI run after this change is its test.
- Not changed: Actions major versions (Node 20 deprecation warnings). Needs a check of current releases first.
- Next (Step A remaining): scope invoices, payments, receipts, credit notes and tasks by location (via the order's location); email provider webhooks; PDF branding; `WebApplicationFactory` authorization tests.


## Step A: location scoping extended to finance (VERIFIED by CI 2026-10-15: 536 of 536 tests passed, including the 9 in `FinanceLocationScopeTests`; the code was first written without an SDK and three test-setup mistakes were fixed along the way)
- [x] Invoices follow the location of the order they were billed from; payments, receipts and credit notes follow their invoice. A caller restricted to some locations (not the owner, no `locations.all`) sees only those. A hidden record returns the same 404 as a missing one (`invoice_not_found`, `payment_not_found`, `receipt_not_found`, `order_not_found`), including on idempotent replays and races.
- [x] Applied in `InvoiceService` (list, get, update/delete draft, issue, void, credit note, create-from-order, replay), `PaymentService` (record, verify, reject, reverse, list, get, receipts) and the global search invoice hits. PDFs go through these services, so they are covered. Shared helper: `FinanceScope` in `FinanceSupport.cs`.
- Rule for invoices with no order (standalone): visible only to unrestricted callers, and scoped staff get 403 `location_forbidden` if they try to create one (fail closed). Decide with the owner whether standalone invoices should get their own location later.
- Same optional-constructor pattern as orders (`ILocationScope? locations = null`): if the scope service were ever unregistered, scoping would silently switch off. The planned `WebApplicationFactory` tests should assert the registration.
- Tests: `FinanceLocationScopeTests.cs` (all passing in CI): own-location visibility, 404 for hidden, standalone rule, void/bill hidden, replay, payments, receipts, search, owner and `locations.all`, member with no locations.
- Still NOT scoped: production tasks (`TaskService`, `MyTaskService`), expenses (no location column), reports. Tasks are next.

## Step A: location scoping extended to production tasks (code written 2026-10-15; UNVERIFIED until CI runs)
- A task follows the location of its order. `TaskService` (list, get, create, update, assign, unblock, cancel, override, generate-for-order, request-alterations) now hides tasks and orders at locations the caller may not see; a hidden task is a 404 `task_not_found` (a hidden order is `order_not_found`, a garment at a hidden location is `garment_not_found`). Filtering the list by a hidden order id returns an empty page, not an error.
- Deliberately NOT scoped: `MyTaskService` (a worker's own assigned tasks: being assigned the work is what authorises it) and the worker workload counts in `WorkerService`. Revisit if the owner wants workers restricted to their location's tasks.
- Same optional-constructor pattern (`ILocationScope? locations = null`); the planned `WebApplicationFactory` tests must assert the scope service is registered.
- Tests: `TaskLocationScopeTests.cs` (written, never run).
- Location scoping remaining: expenses (no location column) and reports.

## Step A: PDF branding (code written 2026-10-16; UNVERIFIED until CI runs)
- Each business can set an accent colour, a logo, a contact line (phone, email, tax number) and a footer note. All PDFs (invoice, receipt, credit note, quotation, measurement sheet, job sheet) draw them. An unbranded business keeps the previous neutral look (navy `#1F3A5F`).
- API: `GET`/`PUT /api/business/branding` (permission `business.settings`). PUT replaces everything; blank or null clears a field. The logo is a `MediaAsset` already uploaded via `/api/media` (purpose `SiteImage`); another business's image is a 404 `media_not_found`.
- Safeguards: colour must be `#RRGGBB` and have at least 3:1 contrast on white (`invalid_brand_color`, `brand_color_too_light`); text fields are single-line, control characters stripped, length-capped; nothing is saved if any field is invalid; the audit entry records field names only. The logo is resized to fit 240x80 and re-encoded to PNG for the PDF. If the logo is deleted or unreadable the document still renders, without a logo. No user-supplied HTML or fonts.
- Schema: six nullable columns on `Businesses` (`BrandColor`, `LogoMediaId`, `ContactPhone`, `ContactEmail`, `TaxNumber`, `PdfFooterNote`). Migration `20261016120000_AddPdfBranding` and the snapshot are HAND-WRITTEN (no SDK here). The `migrations` job and the drift gate in `ci.yml` are the check. If they complain, read the `pending-migration` artifact; do not run "Generate initial migration" with the same name.
- No new entitlement: branding is available on every plan (a deliberate choice; adding a gate needs a catalogue entry, all four plans, enforcement and tests).
- Not covered by tests: `PdfService` end to end with a real stored logo (needs the media pipeline and storage); the renderer is tested with a PNG logo, and the service validation, isolation and clearing are tested. Check a real invoice PDF by eye after deploy.
- Still to do in Step A: email provider webhooks, remaining CSV import/export, Redis queues and SignalR backplane, background-job recovery, `WebApplicationFactory` authorization tests.

## CI run #111 (2026-10-16): green after the `ContrastOnWhite` visibility fix
- Run #111 (commit 9ee7823) succeeded: backend, migrations and web jobs. The earlier failure (#110) was `BrandingService.ContrastOnWhite` being `internal` while `PdfBrandingTests` called it; it is now `public static`.
- Per-test results were not inspected here, so the task-location-scope and PDF-branding tests are "passed in a green run", not individually reviewed.

## Step A: `WebApplicationFactory` authorization tests (code written 2026-10-16; UNVERIFIED until CI runs)
- New: `ApiFactory.cs` (real API on a Testcontainers PostgreSQL; Development environment so the cookie works over HTTP and real migrations run; env vars set only while the host starts) and `ApiAuthorizationTests.cs`. Added `Microsoft.AspNetCore.Mvc.Testing` 10.0.0 and a project reference to `Tailoring.Api`.
- Tests: `ILocationScope` is registered; every controller endpoint has `[Authorize]`/`[HasPermission]`/`[AllowAnonymous]` or is on an explicit allow-list of 10 open auth endpoints (a new unprotected endpoint fails the test); every protected endpoint returns 401 anonymously; cookie writes need the CSRF token (missing and wrong token -> 400 `invalid_csrf`); bearer writes are exempt; a bad bearer never falls back to the cookie; another business gets 404 for a customer; a member without the permission gets 403; a deactivated member is refused on the next request.
- Not covered: behavioural location scoping through HTTP (a restricted member seeing only their location's orders/invoices/tasks); that is covered at service level only.
- Still to do in Step A: email provider webhooks, remaining CSV import/export, Redis queues and SignalR backplane, background-job recovery.

## CI runs #114 and #115 (2026-10-16): multipart 401 test fixed
- #114 (commit "Add files via upload"): backend job failed, 559 of 560 passed. Failure: `ApiAuthorizationTests.Anonymous_requests_to_protected_endpoints_get_401` got 415 (not 401) from `POST /api/documents`, `POST /api/customers/import/preview` and `POST /api/media`. Cause: these actions bind `IFormFile`/`[FromForm]`, so routing rejects a JSON body with 415 before authentication. The assertion was not weakened.
- Fix (commit "Update ApiAuthorizationTests.cs", #115): `ControllerEndpoint` has a `Multipart` flag (from `IAcceptsMetadata` content types, or a form/form-file parameter binding source); those endpoints are probed with a `MultipartFormDataContent`.
- Result: TRX reports 560/560 passed (see Verification status). The `migrations` job of #115 succeeded. The warning annotation on #115 was not read; it is most likely the known Node 20 action deprecation (unconfirmed).
- Static findings this session: no root `ci.yml` exists (no duplicate); `ci.yml` has three jobs (`backend`, `web`, `migrations`); all actions are still `@v4`.
- Still to do in Step A: email provider webhooks, remaining CSV import/export (customers only), Redis caching/queues and SignalR backplane, background-job recovery, behavioural API-level location-scope tests, location scoping for customers/expenses/reports.
- Next, in order: (1) [done] all three jobs of #115 confirmed green; (1b) NEW, security: upgrade Next.js (see the last section); (2) check action releases and bump `checkout`, `setup-node`, `setup-dotnet`, `upload-artifact` in `ci.yml` and `generate-migration.yml`; (3) compose smoke test (needs Docker); (4) finish Step A; (5) Step B dashboard and storefront editor (nothing built yet in `web/` beyond the public renderer and preview).

## CI run #115, `migrations` job log (read in full 2026-10-16)
- Release build: **0 warnings, 0 errors**. `dotnet-ef` 10.0.13. PostgreSQL service container: 17.11.
- `dotnet ef database update` on an empty database applied, in order and each recorded in `__EFMigrationsHistory`: `20261015214018_InitialSchema`, `20261015230000_AddLastTotpStep` (`ALTER TABLE "AspNetUsers" ADD "LastTotpStep" bigint`), `20261016120000_AddPdfBranding` (six nullable `Businesses` columns). It ended with "Done."
- The first `fail: ... SELECT ... FROM "__EFMigrationsHistory"` line (and the matching PostgreSQL `relation ... does not exist` server log) is expected on a clean database; it is not a failure.
- **Real schema inspection (observed):** `tables=65 check_constraints=33 foreign_keys=112 partial_indexes=6`. The "financial/audit FKs that cascade (expect none)" query printed nothing, so none of Payments, Receipts, CreditNotes, Invoices, Expenses, AuditLogs or Orders has a cascading foreign key of its own. The six partial indexes seen in the log: `Orders`/`Invoices`/`Payments` idempotency keys, `Invoices` number, `Workflows` one-default, `EmailOutbox` pending.
- Observed and by design (not checked with the owner): child rows cascade from their parent for `InvoiceLines` -> `Invoices`, `OrderGarments` -> `Orders`, and several others. The CI query only inspects each listed table's own foreign keys, so it would not catch a cascade from a parent. Invoices that are issued are never deleted by the application, and `Payments`, `CreditNotes` and `Receipts` reference invoices with `RESTRICT`.
- What this proves: the committed migrations build the schema on clean PostgreSQL, with composite same-business foreign keys, the check constraints and partial unique indexes present. What it does not prove: that every constraint listed in the model exists (only counts and thresholds are asserted), nor behaviour of the application against a migration-built database beyond what `ApiFactory` exercises.
- **Warning annotation (observed):** "Node.js 20 is deprecated. The following actions target Node.js 20 but are being forced to run on Node.js 24: `actions/checkout@v4`, `actions/setup-dotnet@v4`." A Node `punycode` deprecation notice also appears in post-job cleanup; it comes from the runner, not from Avzar code. Both are harmless for now. The `backend` and `web` jobs also use `setup-node@v4` and `upload-artifact@v4`; their own warnings were not read.
- Still not seen: the `backend` and `web` job results of #115.

## CI run #115, `web` job log (read in full 2026-10-16)
- `npm install`, `npm run typecheck` (`tsc --noEmit`), `npm test` (vitest 2.1.8) and `npm run build` (Next.js 15.1.6) all passed. Node 22.23.3.
- Web tests: **1 file, 22 tests** (`test/links.test.ts`). There are no component or end-to-end tests yet.
- Historical build output from the initial renderer-only stage (superseded by later web slices): only public renderer and preview routes existed; dashboard routes were added later.
- **SECURITY FINDING (open, high priority):** `npm install` printed `npm warn deprecated next@15.1.6: This version has a security vulnerability` (references CVE-2025-66478) and `9 vulnerabilities (3 moderate, 3 high, 3 critical)`. The web app serves the public storefront renderer, so this is internet-facing. Do: run `npm audit` in `web/`, upgrade `next` (and `react`/`react-dom` if the advisory requires it) to the patched release for the 15.1 line or newer, then re-run typecheck, tests and build. Do not use `npm audit fix --force` blindly. Not yet done; CI cannot catch this because the audit does not fail the job.
- Warning annotation on the `web` job: Node.js 20 deprecation for `actions/checkout@v4` and `actions/setup-node@v4` (forced onto Node 24). Not an error.
- `npm install` is used (no committed `package-lock.json`), so dependency versions float between runs. Commit a lockfile and switch to `npm ci`.

## Session 2026-10-16 (security upgrade and Actions bump; UNVERIFIED until CI runs)
Environment: no .NET SDK, no npm network, so nothing below was built or run here. CI is the first check.
- **Next.js/React security upgrade** (`web/package.json`): `next` 15.1.6 -> **15.5.26**, `react` and `react-dom` 19.0.0 -> **^19.2.8**. Reasons, from the advisories read on 2026-10-16: 15.1.6 and React 19.0.0 are affected by CVE-2025-66478 / CVE-2025-55182 (RSC remote code execution, CVSS 10), the December 2025 DoS and source-exposure advisories, and later 2026 advisories; the 15.1 line is no longer patched (15.5.x is the Maintenance LTS line; 15.5.21 fixed nine July 2026 issues; 15.5.26 is the 22 September 2026 out-of-band release). React patched floor for the 19.2 line is 19.2.8 (CVE-2026-44907). I did not look for releases newer than 15.5.26; check before deploying. Possible breakage from a 4-minor jump: `middleware.ts` (CSP nonce), `next.config.mjs` headers and route handlers were not changed; the build and the 22 tests are the check. If `npm run build` fails, read the error before changing code.
- **Remaining npm audit findings are unknown** (cannot run `npm audit` here). Dev-only tools (vitest 2.1.8, vite) may still be flagged.
- `ci.yml`: new non-blocking step `npm audit --omit=dev --audit-level=high` in the `web` job (`continue-on-error: true`). Make it blocking after one clean run.
- **GitHub Actions bumped** (Node 24 versions, from the sources found on 2026-10-16): `checkout@v4`->`v6`, `setup-node@v4`->`v6`, `setup-dotnet@v4`->`v5`, `upload-artifact@v4`->`v6`, in `ci.yml` and `generate-migration.yml`. I could not open the actions' release pages, so these majors come from third-party workflow changes; CI confirms them. Runner minimum for Node 24 actions is v2.327.1 (GitHub-hosted runners are newer).
- Still open: commit a `package-lock.json` (`npm install` creates it; then switch CI and the Dockerfile to `npm ci`), enable Dependabot or monthly dependency review (Next.js now ships monthly security releases).

## Step A: email provider webhook (Resend) - code written 2026-10-16 (UNVERIFIED: not compiled, not run; no .NET SDK here. CI is the first check.)
- New endpoint `POST /api/webhooks/email/resend` (`EmailWebhooksController`): anonymous, CSRF-exempt, authenticated ONLY by the Svix signature (`SvixSignature`: HMAC-SHA256 over `id.timestamp.body`, constant-time compare, 5-minute freshness window, several `v1` signatures allowed). 401 bad signature, 404 when `Email:Resend:WebhookSecret` is unset (fail closed), 413 body over 64 KB, otherwise 200.
- `EmailWebhookService`: one conditional `ExecuteUpdateAsync` per event (status only moves up: Delayed < Delivered < Bounced/Complained), so retries, duplicates, out-of-order and parallel events converge without locks. Tracks `email.delivered`, `email.delivery_delayed`, `email.bounced`, `email.complained`; other event types are acknowledged and ignored. No recipient address is stored from the payload; bounce text is cleaned and capped at 300.
- **Schema change:** `EmailOutbox.DeliveryStatus` (varchar 20), `DeliveryUpdatedAtUtc`, `DeliveryDetail` (varchar 300), plus a filtered index on `ProviderMessageId`. Migration `20261016200000_AddEmailDeliveryStatus`, its Designer and the snapshot are HAND-WRITTEN (no SDK). The Designer body was copied from the updated snapshot by script and checked only by line count (+14 lines vs the previous Designer: 3 properties and 1 index). The `migrations` job and the drift gate are the check; if they complain, read the `pending-migration` artifact and do not run "Generate initial migration" with the same name.
- Tests (`EmailWebhookTests.cs`, written, never run): signature vector computed independently in Python; tamper, stale/future timestamp, malformed header, wrong key; status precedence matrix; parsing; PostgreSQL service tests (disabled, bad signature, replay window, duplicate, bounce never undone, late delayed, unknown message, parallel convergence, isolation between messages); HTTP tests through the real pipeline (no cookie, no CSRF). `ApiFactory` now sets `Email__Resend__WebhookSecret` to a test-only value.
- Not done: acting on bounces/complaints (suppression list so we stop mailing dead addresses), Resend IP allow-list, per-endpoint rate limit (signature check is cheap but the endpoint is public), real-provider test.
- Still open in Step A: Redis caching/queues/SignalR backplane, remaining CSV import/export (customers only), background-job recovery, behavioural API-level location-scope tests, location scoping for customers/expenses/reports.

## Step A: email suppression list - code written 2026-10-16 (UNVERIFIED: not compiled, not run; no .NET SDK here. CI is the first check.)
- New table `EmailSuppressions` (unique lower-case `Address`, `Reason` Bounced/Complained). Migration `20261016210000_AddEmailSuppressions`, its Designer and the snapshot entry are HAND-WRITTEN; the Designer body was copied from the snapshot by script (identical). The `migrations` job and the drift gate are the check; if they complain, read the `pending-migration` artifact.
- Webhook: permanent bounces (`bounce.type` = Permanent) and complaints insert a suppression row via one `INSERT ... SELECT ... ON CONFLICT DO NOTHING` from the outbox row; transient/undetermined bounces do not.
- Dispatcher: checks the list at send time; suppressed recipients are rejected (Failed, body purged) without calling the transport.
- Known limits: address matching uses .NET `ToLowerInvariant` on one side and PostgreSQL `lower()` on the other (identical for ASCII addresses); no admin UI to lift a suppression; the provider's `to` field is not used.
- Tests added in `EmailWebhookTests.cs` (rules, service, parallel) and `EmailTests.cs` (dispatcher). Never run.
- Web items still open: commit `web/package-lock.json`, switch CI/Dockerfile to `npm ci`, make `npm audit` blocking after reading its output in run #136.

## CI run #144 (commit 63732ee), read 2026-10-16
- backend: green, 0 warnings, **641/641 tests** (up from 618: the suppression tests ran and passed). The drift-gate output lines were not in the pasted log; the step is green, so no drift.
- migrations: green (`AddEmailSuppressions` applied to a clean PostgreSQL). Step output collapsed; schema counts not read (expect tables=66).
- web: green. 22 vitest tests, Next.js 15.5.26 build OK. The "1 error" annotation is the non-blocking `npm audit` step exiting 1.
- `npm audit --omit=dev` (runtime): `next` flagged moderate (SSG/ISR cache poisoning, GHSA-4jqv-mc3x-m676, GHSA-mcj8-r9mp-w47p) and `postcss` high (bundled with next, 4 advisories). npm names next@15.5.28 as the fix. Changed `web/package.json` to `next` 15.5.28 (UNVERIFIED until CI).
- `npm install` also reports 8 vulnerabilities in total (2 critical, 2 high); the audit with dev dependencies included is not yet read. Likely vitest/vite tooling (vitest 2.1.8); not part of the runtime image. Check after the lockfile exists.
- Still open: commit `web/package-lock.json`, `npm ci` in CI and Dockerfile, make audit blocking once clean.

## CI run #149 (web job), read 2026-10-16
- `npm ci` works with the committed lockfile; typecheck and 22 tests pass; Next 15.5.28 cleared the two moderate Next advisories.
- `npm audit --omit=dev` still reports `postcss` <=8.5.22 (high, 4 advisories) nested inside `next`; npm's only offered fix is `next@16.4.2` (breaking). The audit step (non-blocking) therefore still exits 1 and produces the "1 error" annotation.
- Assessment (not verified): the advisories need attacker-controlled CSS or source-map comments; Next only runs postcss at build time on this repo's own CSS, and storefront content is validated config, not CSS. Low practical exposure, but the audit stays red until fixed.
- Change prepared (UNVERIFIED until CI): `overrides: { next: { postcss: ">=8.5.23 <9" } }` in `web/package.json`. The lockfile must be regenerated (run "Generate web lockfile") before `npm ci` will accept it. If the build or audit still fails, do NOT upgrade to Next 16 blindly; read the error first.

## CI run #152 (web job), read 2026-10-16: STEP 0 CLOSED
- `npm ci` from the committed lockfile; `npm audit --omit=dev --audit-level=high` reports **0 vulnerabilities** (the postcss override worked); typecheck, 22 tests and the Next 15.5.28 build pass.
- Audit step made BLOCKING in `ci.yml` (continue-on-error removed): UNVERIFIED until the next run.
- Remaining dev-only findings (`npm install` shows 6: 3 moderate, 1 high, 2 critical, all outside the runtime audit; likely vitest/vite tooling): not shipped, review when upgrading vitest.
- Next: (3) Docker Compose smoke test, (4) Step A leftovers, (5) Step B.

## Step A: one-owner constraint and locations.all back-fill - written 2026-10-16 (UNVERIFIED: not compiled, not run; CI is the first check)
- Partial unique index `UX_Memberships_OneOwner` on `Memberships(BusinessId) WHERE "IsOwner"`; `OwnershipService.TransferAsync` now saves the outgoing owner first, then the new owner, in the same transaction (the existing ownership tests, including the racing-transfers test, exercise this against the EnsureCreated schema).
- Migration `20261016220000_AddOneOwnerGuardAndLocationsAllBackfill` (hand-written, Designer copied from the snapshot): back-fills `locations.all` for system Manager/Accountant roles that lack it AND were never edited via the API (no `role.permissions_changed` audit row); then creates the index. It fails loudly if a business already has two owners.
- Tests: `OwnerGuardAndBackfillTests.cs` (constraint, transfer order, back-fill incl. edited role, custom role with a system name, idempotence). The back-fill SQL is run directly because the xUnit suite uses EnsureCreated; the `migrations` CI job only proves the migration applies to an empty database (no rows to back-fill).
- Still open in Step A: ownership-transfer notification emails, Redis caching/queues/SignalR backplane, CSV import/export beyond customers, background-job recovery, behavioural location-scope tests and scoping for customers/expenses/reports.

## Dev-dependency security fix (vitest 4) and lockfile, read 2026-10-16: STEP 0 FULLY CLOSED
- CI run #160 (f05d9d8): all three jobs green (`backend` 2m 13s, `migrations` 1m 20s, `web` 33s). `npm ci` still printed `6 vulnerabilities (3 moderate, 1 high, 2 critical)` even though the runtime-only audit (blocking since #152) was clean.
- Cause (read from the manual "Audit report" workflow, `audit.yml`): all six findings were in the vitest 2.1.8 toolchain (vitest, @vitest/mocker, vite, vite-node, esbuild, tinypool); none in runtime dependencies, none shipped. npm's suggested `audit fix --force` target (vitest 2.1.9) was NOT used; the advisory (GHSA-82fw-gwwq-j7x9) names 4.1.11 as the fixed version.
- Change: `vitest` 2.1.8 -> **4.1.11**; `overrides` simplified to `{ "postcss": ">=8.5.23 <9" }` (was nested under `next`).
- Problem found and fixed: `npm install --package-lock-only` crashed on the runner's npm 10.9.9 with `Cannot read properties of null (reading 'edgesOut')` (in arborist `#loadPeerSet`, while resolving vitest's optional peers). Removing the old lockfile and simplifying the override did not help. Fix in `generate-lockfile.yml`: install `npm@latest` (12.2.0), delete `package-lock.json`, regenerate it, print the tail of npm's debug log on failure. A lockfile written by npm 12 installs fine with `npm ci` on npm 10.9.9 (verified in #169).
- "Audit report" run #3: full audit and runtime-only audit both **0 vulnerabilities**.
- `ci.yml` audit step is now `npm audit --audit-level=high` on the whole tree (was `--omit=dev`). CI run #169 (9a07dd8): all three jobs green (see Verification status for details).
- New or changed workflow files: `generate-lockfile.yml` (rebuild from scratch with latest npm), `audit.yml` (manual `workflow_dispatch`, writes both audit reports to the run summary; can be deleted or kept).
- Gotcha: commits pushed by `generate-lockfile.yml` use the default `GITHUB_TOKEN`, so GitHub does not start CI for them. Start CI by hand (`workflow_dispatch`) afterwards.
- Non-blocking: `npm ci` prints an ERESOLVE peer warning (vite 8 via vitest 4 wants `@types/node` ^20.19.0 || >=22.12.0; the repo pins 22.10.7). Build and tests pass. Bump `@types/node` to a 22.x at or above 22.12 the next time `package.json` is edited.
- This supersedes the open web items in the sections for runs #115, #144, #149 and #152 (commit a lockfile, `npm ci`, make audit blocking, dev-only findings).
- Email webhook and suppression list: covered by the suite that passed in #144 (641/641). The one-owner constraint and `locations.all` back-fill: the migration applied to a clean PostgreSQL in #169 and the `backend` job is green, so `OwnerGuardAndBackfillTests` are part of the passing suite; per-test results and the test count were not read.
- Still unverified: Docker Compose startup, full-stack smoke test, storefront publish-to-render, every external integration (email, S3, ClamAV, Redis, DNS, payments, push, AI), mobile builds. The web app has only the public renderer and preview.
- Next: (3) Docker Compose smoke test (needs Docker), (4) Step A leftovers, (5) Step B dashboard and storefront editor with Playwright tests.

## Step A: location scoping for expenses and the expense summary (written 2026-10-16; UNVERIFIED: not compiled, not run; CI is the first check)
- Decision (owner): expenses and reports get location scoping; **customers stay unscoped** (a customer can visit several locations, and merge already moves fittings across them). The only report that exists today is the expense summary; revenue/profit reports are Phase 5.
- `Expense.LocationId` (nullable, composite FK to `BusinessLocation` with `Restrict`, index `(BusinessId, LocationId)`). null = business-wide: visible only to callers who see every location (owner or `locations.all`). **Needs a forward migration**: run the `Generate initial migration` workflow with `migration_name` = `AddExpenseLocation`, then start CI by hand. Until then the `ci.yml` drift gate and the `migrations` job will fail; the tests build the schema with EnsureCreated and are not affected.
- `ExpenseService` gets the optional trailing `ILocationScope? locations = null` (same pattern as orders/invoices). Restricted callers: list, get, update, void and summary see only expenses at their locations; a hidden expense is a 404 `expense_not_found`. Recording or moving an expense needs one of their own locations (403 `location_forbidden`, including "no location given"). Unknown or inactive location: 400 `unknown_location`.
- API: `UpsertExpenseRequest.LocationId` (on update, omitting it keeps the current location; it cannot be cleared once set), `ExpenseDto.LocationId`, `ExpenseListQuery.LocationId`, and `GET /api/expenses/summary?locationId=`.
- Tests: `ExpenseLocationScopeTests` (9, written, never run).
- Also written (UNVERIFIED): `LocationScopeApiTests` (5), behavioural location scoping through real HTTP with the real login: a member limited to one location lists only that location's orders, a hidden order is 404 (same as a made-up id), creating an order at another location is 403 `location_forbidden`, a member with no locations sees nothing, the owner sees everything. Covers orders only; expenses are plan-gated (free plan) so they stay at service level. `ApiFactory` gained `SeedLocationAsync` and `AssignLocationsAsync` (setup helpers).
- Expected backend total if everything passes: 670 (656 + 9 + 5), once the 656 baseline is green (the baseline needs the one-line `ChangeTracker.Clear()` fix in `An_unusable_address_does_not_block_the_transfer_or_the_other_notice`).
- Still open in Step A: Redis caching/queues/SignalR backplane, CSV import/export beyond customers, background-job recovery, HTTP-level scope tests for invoices/payments/tasks, email extras.


## CI run #188 (07b0d59), read 2026-10-16
- `migrations` (1m 27s) and `web` (29s) succeeded (logs read). Backend: supplied TRX **670 total / 670 passed / 0 failed** (run window 09:51:29-09:52:25 UTC, 50 test classes; no run id, so the link to #188 rests on the zip's commit hash and the timing). It includes the ownership-notification tests, `ExpenseLocationScopeTests` (9) and `LocationScopeApiTests` (5).
- Render deploy: API live at https://avzar-api.onrender.com (root answers 404, i.e. up). Log showed `Email:Provider` = `log`, non-persisted Data Protection keys and `Proxy:TrustForwardedHeaders` off. Set `Proxy__TrustForwardedHeaders=true`, the Resend variables and `Database__MigrateOnStartup=true`.

## Data Protection keys in PostgreSQL (written 2026-10-16; UNVERIFIED until CI)
- `AppDbContext` implements `IDataProtectionKeyContext`; `AddDataProtection().SetApplicationName("avzar").PersistKeysToDbContext<AppDbContext>()`. Sessions (Identity cookie), antiforgery, reset/verify links and preview links now survive restarts. Keys are not encrypted at rest.
- **Needs a forward migration**: run `Generate initial migration` with `migration_name` = `AddDataProtectionKeys`, then start CI by hand. Until then the drift gate and the `migrations` job fail.

## Step A: production task location scoping - CORRECTION and real implementation (written 2026-10-16; UNVERIFIED until CI)
- The earlier section "location scoping extended to production tasks" claimed `TaskService` was scoped and that `TaskLocationScopeTests.cs` existed. In the 07b0d59 tree neither was true: `TaskService` had no `ILocationScope` and no such test file existed, so staff limited to a location could list, read and change tasks at other locations.
- Now: `TaskService(…, ILocationScope? locations = null)`. A task follows its order's location. List filters by the caller's locations (so `OrderId`/`GarmentId` filters cannot reach hidden tasks); get, update, assign, unblock, cancel and override on a hidden task are 404 `task_not_found` (override checks visibility BEFORE permission/reason so it reveals nothing); create and generate on a hidden order are 404 `order_not_found`; request-alterations on a hidden garment is 404 `garment_not_found`. `MyTaskService` and worker counts stay unscoped by design.
- Tests: `TaskLocationScopeTests` (10, written, never run). Expected backend total if all pass: 680.

## CI run #207 (3714953), read 2026-10-16: items moved from UNVERIFIED to verified
- Verified at test level by the 680/680 pass (and the green drift gate and `migrations` job): production-task location scoping (`TaskLocationScopeTests`, 10), expense location scoping (`ExpenseLocationScopeTests`, 9), HTTP-level order scoping (`LocationScopeApiTests`, 5), ownership-transfer notification emails, and `AddDataProtectionKeys` / `AddExpenseLocation` (applied to a clean PostgreSQL; model and migrations match). This supersedes the "UNVERIFIED until CI" and "Needs a forward migration" notes in the sections for expenses, Data Protection keys and task scoping above, and the "to be generated" note for `AddDataProtectionKeys`.
- Backend count history: 560 (#115) -> 641 (#144) -> 648 -> 670 (#188) -> **680 (#207)**.
- Still not verified: the real behaviour behind the Data Protection keys on the deployed Render API (sessions surviving a restart); real email delivery (Render had `Email:Provider=log`; set the Resend variables, `Proxy__TrustForwardedHeaders=true` and `Database__MigrateOnStartup=true`); the Vitest count and the contents of PR #3 (slice 3, customers); `docker compose up`; storefront publish-to-render; every external integration; mobile builds.
- Not verified from #207: `workflows` list now has `ci.yml`, `generate-lockfile.yml`, `generate-migration.yml` (the manual `audit.yml` is no longer in the repository snapshot we have; it was in the earlier handoff).
- Next: (1) read the `web` step logs / PR #3 diff and record the Vitest count and what slice 3 added; (2) Docker Compose smoke test; (3) remaining Step A: Redis caching/queues/SignalR backplane, CSV import/export beyond customers, background-job recovery, HTTP-level scope tests for invoices/payments/tasks, email extras; (4) Step B dashboard and visual storefront editor with Playwright tests.

## Step A: HTTP-level location-scope tests for invoices, payments, receipts and tasks (written 2026-10-16; UNVERIFIED: not compiled, not run; CI is the first check)
- New file `backend/tests/Tailoring.IntegrationTests/FinanceTaskLocationScopeApiTests.cs` (13 tests, `[Collection("api")]`, real login, real permission handler). Test-only change: no production code was touched, no migration.
- Covers: restricted member lists only own-location invoices, payments and tasks; a hidden invoice, payment, receipt or task is 404 with the same code as a missing one; invoices with no order are hidden from restricted callers (fail closed); writes on hidden records (void, delete draft, record payment, reverse, reject, verify, cancel task, generate tasks) are 404 and leave the record unchanged for the owner; hidden invoice, receipt and job-sheet PDFs are 404 (the 404 happens before rendering, so no PDF licence is needed); a member with no locations sees nothing; the owner sees everything.
- Not covered: positive PDF downloads, credit-note PDFs, worker (`/api/my/tasks`) endpoints (unscoped by design).
- At the time this entry was written, 693 was an expected backend total (680 + 13). This expectation was later confirmed by the supplied CI #230 backend log: 693 passed, 0 failed, 0 skipped. Baseline before those 13 tests: CI #207 (3714953), 680/680.

## Step B: order workflow UI (implemented in the supplied archive; local verification blocked)
- The archive contains the order list and read-only detail pages, plus this increment's `web/src/app/(app)/orders/new/page.tsx`, `web/src/app/(app)/orders/[id]/edit/page.tsx`, `web/src/components/app/OrderForm.tsx`, and `OrderActions.tsx`.
- Create order: selects an active customer and active workshop location, supports customer search, promised date, priority, delivery details, notes, garment lines, manual unit prices and discount. Create uses `Idempotency-Key`; totals remain server-calculated. The UI never accepts client-calculated totals or exposes cost/margin data.
- Edit: sends the API's optimistic-concurrency `version`, retains existing measurement/design links, only offers Draft/Quoted editing, and explains that editing a quoted order returns it to Draft.
- Lifecycle actions: quote Draft orders, confirm Quoted orders (with `orders.confirm` permission), and cancel eligible orders with a required reason and confirmation prompt. API responses remain authoritative; invoices can block cancellation server-side.
- Added unit tests for action visibility and permission/status combinations in `web/test/orders.test.ts`. No backend or migration change was made.
- **Verification:** the supplied CI #230 backend suite passed 693/693 and web job succeeded, but the pasted web log did not include the exact test count or build/typecheck summaries. Local `npm ci` timed out and dependencies were unavailable, so the new UI has not been locally typechecked, tested or production-built. The next CI run must verify the new pages/components and the updated tests.
- **Known limits:** no design/measurement picker in the order form; garment lines can be entered manually. Invoice creation/payment workflows, delivery action UI, production-task links and pricing calculator integration remain separate increments. The current API controller exposes quote/confirm/cancel but not the service's delivery operation.


## Step B: production task dashboard and actions (implemented in working copy; verification blocked)
- Added `web/src/app/(app)/tasks/page.tsx` and `[id]/page.tsx`: manager users with `tasks.view` get the paged `/api/tasks` list and filters for status, stage and overdue work; worker-only users with `tasks.work` get the caller-scoped `/api/my/tasks` list and open/completed/all views. Task detail shows instructions, requirements, prerequisites and event history.
- Added `web/src/lib/production-types.ts`, `web/src/lib/production.ts` and `web/src/components/app/TaskActions.tsx`. Worker-only controls call the existing start/pause/resume/complete/report-issue endpoints; completion requires all checklist items. Manager controls call assignment, optional audited override, unblock and cancel endpoints. Added `web/test/production.test.ts` for pure query/display helpers, enabled the Production navigation item and added minimal attention/checkbox styling.
- This increment is only partially complete: daily schedule planning, planned-time editing, automated reminders/notifications, browser-level action tests and manager task editing are not implemented in the UI yet. The backend remains authoritative for permissions, location scoping, worker assignment and state transitions.
- Verification: `npm ci --no-audit --no-fund` timed out again. No local Vitest run, full TypeScript typecheck or Next production build could be completed. CI run #230 predates this increment and is not evidence for these changes. No backend code or migration changed.
- Next: run CI on the combined order + production UI changes, address all failures, then add integration/e2e coverage and daily schedule planning/notifications.

## Step B: storefront draft editor increment (implemented in working copy; verification blocked)
- Added `/storefront` and `StorefrontManager`: list/select tenant-owned storefronts, create from a plan-allowed template, load the current draft and catalog, edit page title/visibility, section visibility/layout variant/content JSON, add preset sections, reorder/remove sections, and edit theme JSON. It preserves IDs and sends the expected draft revision so the backend rejects stale edits instead of overwriting them silently.
- Added private preview-link flow, explicit publish confirmation with a version note, unpublish, version history/rollback, and storefront contact/settings form. Publishing is permission-gated in the UI and still enforced by the API. Plan issues and contrast warnings returned by the API are shown to the editor.
- Enabled Storefront navigation for users with `storefront.view`; API permission/plan validation remains authoritative. Added editor code-area styling.
- Limitation: this is a first usable draft-management/editor slice, not yet the polished drag-and-drop visual builder. Section props and theme configuration currently use JSON editors; media picker, custom pages, domain management, template-switch confirmation, and browser e2e tests remain open. Template switching's preview/confirm flow is not yet surfaced in the UI.
- Verification: npm dependency installation timed out again; no local Vitest run, full typecheck, or Next build could be performed. TypeScript syntax transpilation and archive integrity checks are the available local checks. CI #230 predates this change and does not verify it. No backend or migration changed.

## Step B: business hours and closure calendar UI (implemented in working copy; verification blocked)
- Added `/settings` for members with `business.settings`, backed by the existing business schedule API. The page edits all seven days' open/closed state and opening/closing times, displays the business time zone, and adds/removes named closure dates. API validation remains authoritative; local validation prevents obvious invalid time ranges.
- Enabled the Settings navigation item for users with `business.settings`.
- Verification: syntax transpilation is run across the web source/tests; a focused TypeScript check uses temporary dependency stubs because npm cannot reach the registry in this environment. Full `npm ci`, Vitest, full strict typecheck, and Next build remain blocked by npm registry DNS (`EAI_AGAIN`). No backend or migration changed.

## Step B: finance and invoice workflows UI (implemented in working copy; verification blocked)
- Enabled `/finance` for users with invoice, payment or expense-view permissions. The page provides searchable/status/outstanding/overdue invoice listing, pending payment claims, and an expense-by-category summary, each shown only when the signed-in user has the corresponding permission.
- Added `/finance/invoices/new` for free-form draft invoices, with active-customer selection, line items, discounts, default/per-line tax, due date, notes and terms. Creation uses a stable idempotency key until the form changes. The server remains authoritative for invoice totals.
- Added `/finance/invoices/[id]` for invoice detail, server-calculated line totals, PDF link, issue/delete-draft/void/credit-note actions, payment recording, pending-payment verify/reject, verified-payment reversal, and receipt/credit-note PDF links. Permissions control which controls appear; API enforcement remains authoritative.
- Added an order-detail action to create Full, Deposit or Balance draft invoices for eligible confirmed/in-production/ready orders. It uses an idempotency key and relies on the backend to prevent duplicate billing.
- Limitations: expense summary is read-only (no expense/category CRUD UI); invoice draft editing, order invoice history, payment search/filtering, and e2e tests remain open. PDF endpoints may return the configured licensing error until production PDF licensing is configured.
- Verification: syntax transpilation passed for 98 TS/TSX files. Focused typechecking of the new components/pages with temporary dependency stubs passed. A standalone business-schedule helper check passed 6 assertions. Full `npm ci` fails on npm registry DNS (`EAI_AGAIN`), so Vitest, full strict typecheck and Next production build remain unverified. No backend/migration changes; no commit, push, merge or deployment.


## Step C1: first-pass production scheduling domain logic (historical increment; superseded by Steps C2 and D)
- Added `backend/src/Tailoring.Domain/Production/ProductionSchedulePlanner.cs`: deterministic scheduling across configured business hours and holidays; per-worker non-overlap against existing reservations; dependency-aware ordering; priority/deadline/stage ordering; explicit unscheduled reasons for missing assignment/estimate, unresolved prerequisites and no available slot. No estimate or worker is fabricated.
- Added three pure scheduling cases to `backend/tests/Tailoring.IntegrationTests/ProductionDomainTests.cs` for business-hours/non-overlap, dependencies/reservations, missing estimates and closure dates.
- Added `docs/production-scheduling.md` with current capabilities, limitations and next steps. Updated architecture and master handoff status.
- At this earlier checkpoint the planner was domain-only. The API preview was added in Step C2 and the web preview screen in Step D. Schedule persistence/apply, worker availability, and notifications remain open. No migration is needed for preview-only scheduling.
- Verification at this checkpoint: `.NET` SDK was not installed, so the backend tests and compilation were not run.


## Step C: shop schedule preview API (implemented in working copy; not compiled or run)
- Added `IShopScheduleService` and `ShopScheduleService`, plus `POST /api/tasks/schedule/preview` on `api/tasks`. The endpoint requires `tasks.view`; the service independently checks the `scheduling.shop` entitlement.
- The service loads the tenant's time zone, seven-day business-hours configuration, closure dates, pending/scheduled tasks and task dependencies. It omits completed/cancelled prerequisites, considers in-progress/paused planned slots as reservations, and applies `ILocationScope` to the task candidates. It returns scheduled and unscheduled task explanations without modifying task records.
- Added planner tests for dependency cycles, independent-worker progress, UTC start requirements and horizon bounds. Updated the scheduling guide, architecture, README and master handoff.
- **Verification blocked:** this environment has no .NET SDK, so no C# build or test suite ran. Static inspection and archive validation are not compile verification. No migration, commit, push, merge or deployment was performed.
- Remaining before schedule apply: worker leave/availability and capacity, robust DST tests, transaction + optimistic concurrency + audit for persisted plan application, HTTP tests for permissions/entitlements/tenant/location isolation, notifications, and web/mobile schedule views.


## Step D: shop schedule preview web UI (implemented in working copy; not build-verified)

- Added `/tasks/schedule`, linked in navigation for users with `tasks.view`.
- UI calls `POST /api/tasks/schedule/preview`, accepts a UTC start date and bounded horizon, shows proposed slots in the business time zone, and lists unscheduled-task reasons.
- Explicitly labels the output as preview-only; no task times are written.
- Uses existing same-origin API helper, including CSRF handling.
- Verification: source-level checks only; `npm ci`, typecheck, Vitest and Next build have not run in this environment.

### Shop schedule apply increment (2026-10-16)
- Added `POST /api/tasks/schedule/apply`, gated by `tasks.manage` and `scheduling.shop` entitlement.
- Preview responses now include task row versions. Apply recalculates server-side, rejects stale candidate sets/versions and partial schedules, then saves planned times transactionally with `schedule_applied` task events.
- The web schedule screen now offers Apply only when every candidate is schedulable and submits the preview's version map; stale plans must be regenerated.
- Notifications remain unimplemented and the API reports `notificationsQueued: false`. No worker leave/capacity model or mobile calendar yet.
- Verification remains incomplete: .NET SDK and npm dependencies are unavailable in this environment; no backend compile/integration tests or full web build/tests were run for this increment.


## Scheduling hardening follow-up (2026-10-16)
- Updated the shop schedule preview to treat existing `Scheduled`, `InProgress`, and `Paused` task slots outside the candidate set as worker busy-time reservations. This matters for location-restricted managers: a worker can be assigned across locations, and the manager's visible task set must not permit a schedule that overlaps work already planned elsewhere. Only time ranges are used; hidden task IDs/details are not returned in the preview.
- Verification boundary: source-level change only in this workspace. The .NET SDK and Docker are unavailable here, so backend compilation and PostgreSQL-backed regression tests have not been run. CI must verify this change before merge/deploy.

- Security follow-up: `ShopScheduleService` now requires `ILocationScope` rather than accepting a nullable optional dependency. A missing registration can no longer silently disable location filtering for schedule previews. Source-level only; CI must compile the constructor/DI wiring and run location-scope tests.


## 2026-10-16: worker calendars, schedule notifications, storefront editor extensions (IMPLEMENTED IN WORKING COPY; verification blocked)
- Added tenant-scoped worker recurring availability, daily capacity, approved leave persistence, API endpoints, and a worker calendar endpoint with location filtering. The planner respects worker-specific hours, approved leave, daily capacity and existing reservations; leave is not counted as productive workload. New forward migration: `20261016233000_AddWorkerAvailabilityAndLeave`.
- Schedule apply now queues assignment emails and eligible 24-hour reminders through the existing durable email outbox. Added a filtered unique deduplication key and migration `20261016230000_AddEmailDeduplication`; pending schedule mail for superseded time slots is cancelled/purged. Existing retry, leasing, suppression, and provider delivery-status handling remain the delivery path. `NotificationsQueued` only means a new message was queued, not delivered.
- Added `/workers/schedule` UI for individual calendars, recurring availability/capacity, approved leave, and planned tasks. Updated shop schedule UI messaging to reflect notification queue behavior.
- Extended storefront editor with custom page add/remove (subject to plan limits), URL slug, SEO title/description, navigation visibility, section style editing, existing-media picker for supported image sections/logo, template-switch preview/apply, and custom-domain DNS verification controls. Existing draft save/preview/publish/version rollback workflows remain server validated; media upload/delete remain in the Media library.
- Added planner tests for worker availability, leave and capacity, PostgreSQL integration tests for schedule preview/apply and stale preview rejection, and worker availability/leave service tests. Existing order/production, storefront, and finance integration test suites remain in place.
- **Not verified:** no .NET SDK or Docker is available, and registry DNS is unavailable. Backend compile, PostgreSQL integration tests, migration application/model drift, web typecheck, Vitest and Next production build remain unrun. TypeScript/TSX syntax transpilation passed for all 104 web TypeScript files; this is syntax-only, not a full typecheck. Do not merge/deploy until CI and disposable-PostgreSQL migration checks pass.

## Roadmap implementation increment (2026-10-16)

Added source for the next roadmap slice on top of the recovered 2026-10-16 ZIP:

- Inventory domain/persistence/API/UI: materials, location-scoped stock visibility, movement history, receive/adjust/reserve/release/consume operations, serializable transactions, xmin version checks, low-stock status, and tenant-isolation tests.
- Reports: date-scoped summary and daily series for order value, delivered orders, verified receipts, non-void expenses, and production workload; tenant/location-aware cache key and 30-second distributed cache (Redis when configured, memory fallback).
- Customer portal: hashed expiring/revocable email magic links, secure order/progress/invoice/payment read model, and a customer-detail action to send the link.
- AI studio: optional OpenAI fashion concept image generation and storefront-assistant configuration draft endpoints/UI, guarded by permissions, rate limiting, and plan entitlements.
- Subscription/platform admin: Paystack NGN checkout initialization, signature + provider verification before applying a plan change, payment attempt persistence, and configuration-allowlisted global plan editing.
- SignalR business event hub with business/user group isolation; worker task actions broadcast `task.updated`.
- .NET MAUI Android/iOS starter with bearer login, SecureStorage tokens, refresh-token rotation, assigned-task cache and offline task-action queue.
- Added forward migrations `20261017090000_AddInventory`, `20261017100000_AddCustomerPortalGrants`, and `20261017110000_AddSubscriptionPayments`; updated model snapshot, tenant isolation tests, config examples and architecture docs.

**Verification is not yet complete.** This environment has no .NET SDK or Docker/PostgreSQL runtime, so C# compilation, EF migration execution/drift checks, integration tests, MAUI builds, and live Paystack/OpenAI/Redis/SignalR checks have not run. The implementation is a source increment, not a production-certified completion. The MAUI client still needs conflict resolution, broader offline data, push notifications and device testing. See `docs/VERIFICATION_REPORT_2026-10-16_ROADMAP.md`.
