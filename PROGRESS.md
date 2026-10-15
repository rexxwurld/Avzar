# Progress checklist

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
- [ ] **Compile, generate the first EF migration, run the tests** - needs a machine with .NET 10 SDK + Docker
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
- [ ] Location-scoped staff access to orders/customers
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
- [ ] Redis, S3-compatible storage, orphan cleanup, antivirus interface, document uploads
- [ ] PDF generation
- [ ] CSV import/export, global search
- [ ] Working hours / holidays
- [ ] Review notes: see `docs/STATIC_REVIEW.md` (ImageSharp 3.1.7 advisory question is still open)
