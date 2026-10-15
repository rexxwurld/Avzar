# Progress checklist

## Verification status (read first)
- **Verified by CI run #37 (GitHub Actions, commit 139b9f0, green):** `dotnet build -c Release`, `dotnet test -c Release` (full backend suite incl. Testcontainers PostgreSQL), `dotnet ef migrations add` (EF model generates a migration), web `npm run typecheck`, `npm test`, `npm run build`.
- **Not yet verified:** `docker compose up`, publishing a storefront via Swagger and viewing it in the public renderer, exact test counts (see the `backend-test-results` artifact), migration SQL inspection (check constraints, partial unique indexes, composite FKs), and every external integration (email, storage, DNS, etc.).
- The per-item "(unverified)" tags below pre-date CI #37. A green suite supports them but does not prove each line; retag an item only when a specific test covers it.
- Dependency security: ImageSharp 4.1.2 (needs a Six Labors license at build time, see docs/RUNBOOK.md section 8); transitive pins for Pkcs, SSH.NET and AWSSDK.Core in `Directory.Packages.props`.

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
- [ ] Audit log read endpoint (`audit.view`)
- [ ] Email verification, password reset / account recovery
- [ ] Join an invitation with an existing account
- [ ] API tests for authorization (WebApplicationFactory): forbidden without permission, deactivated user locked out
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
- [ ] API-level authorization tests (WebApplicationFactory) for the new endpoints
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
- [ ] Task planned times, daily schedules, availability/leave, scheduling engine (Phase 4)
- [ ] Notifications for assignments, blocks, overdue, newly eligible steps (Phase 5)
- [ ] Material reservations / shortage handling against stock (Phase 6)
- [ ] Progress photo attachments
- [ ] Amendments to confirmed orders
- [ ] API-level authorization tests (WebApplicationFactory) for the Phase 3 endpoints
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
- [ ] NOT BUILT: the dashboard and the visual editor (drag-and-drop, live preview iframe, undo/redo). The API for it exists (draft/appearance/switch/publish/versions/presets, and the catalog now includes each section's field schema so the editor can generate forms)
- [ ] Not built: pricing page UI, onboarding wizard, customer portal
- [ ] Data Protection keys must be persisted in production (preview links) - see RUNBOOK
- [ ] Fonts load from Google Fonts (CSP allows it); self-hosting is a privacy hardening step
- Page limits default to 5/5/12/25 so each plan can hold its templates' own pages (earlier 1/3/8/20 would not)

## Phases 4-8
Not started: scheduling engine & publication, notifications & SignalR & Hangfire, inventory & finance, MAUI app, storefront module, platform admin, deployment hardening.


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
- Step A is now code-complete EXCEPT: email provider webhooks (delivered/bounced), join-with-existing-account, ownership transfer, TOTP replay protection, per-user 2FA attempt rate limit beyond lockout, PDF logos, PDF share links, Redis caching/queues. None of Step A has been exercised against real credentials (email, S3, ClamAV, Redis).


## Session 2026-10-15 (handoff V2 baseline review; read-only, no code changed)
Environment: no .NET SDK, Docker, `.git` or npm network access, so **no build, test or install was run**. Done by reading only:
- Rewrote `README.md` as a product README; claims cross-checked against `PROGRESS.md`, `docker-compose.yml`, `Directory.Packages.props`, `Program.cs` and the CI workflow.
- CI run #64 (commit `889afec`) is reported successful from the supplied run page (backend 1m52s, web 47s). Its job logs were not available, so the license gate passing is inferred from the workflow (the step exits 1 on an empty secret), not observed.
- Found: there is no root `ci.yml` (only `.github/workflows/ci.yml` and `generate-migration.yml`), so the suspected duplicate does not exist in this snapshot.
- Found: integration tests use `EnsureCreated` (`TenantIsolationTests.cs`), so CI has never applied a migration to PostgreSQL; CI only checks that `dotnet ef migrations add` succeeds. (Stale: a `Migrations` folder with `InitialSchema` is now committed.)
- Found: `web/` contains only the public renderer and preview; there is no dashboard or editor.
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


## Step A: location scoping extended to finance (code written 2026-10-15; UNVERIFIED: not compiled or run, no .NET SDK in the authoring environment; CI is the first check)
- [x] Invoices follow the location of the order they were billed from; payments, receipts and credit notes follow their invoice. A caller restricted to some locations (not the owner, no `locations.all`) sees only those. A hidden record returns the same 404 as a missing one (`invoice_not_found`, `payment_not_found`, `receipt_not_found`, `order_not_found`), including on idempotent replays and races.
- [x] Applied in `InvoiceService` (list, get, update/delete draft, issue, void, credit note, create-from-order, replay), `PaymentService` (record, verify, reject, reverse, list, get, receipts) and the global search invoice hits. PDFs go through these services, so they are covered. Shared helper: `FinanceScope` in `FinanceSupport.cs`.
- Rule for invoices with no order (standalone): visible only to unrestricted callers, and scoped staff get 403 `location_forbidden` if they try to create one (fail closed). Decide with the owner whether standalone invoices should get their own location later.
- Same optional-constructor pattern as orders (`ILocationScope? locations = null`): if the scope service were ever unregistered, scoping would silently switch off. The planned `WebApplicationFactory` tests should assert the registration.
- Tests: `FinanceLocationScopeTests.cs` (written, never run): own-location visibility, 404 for hidden, standalone rule, void/bill hidden, replay, payments, receipts, search, owner and `locations.all`, member with no locations.
- Still NOT scoped: production tasks (`TaskService`, `MyTaskService`), expenses (no location column), reports. Tasks are next.
