# Remaining Avzar roadmap work

This file tracks work not yet proven complete. An implementation is not considered verified until the relevant automated or live check has run successfully.

## Recently implemented, awaiting dependency-enabled verification
- Expense/category management UI and order invoice history (Pass 3).
- Worker leave request/review workflow and review metadata migration (Pass 4).
- Email suppression admin screen and email webhook rate limiting (earlier pass).
- Storefront section drag-and-drop ordering and pure-helper unit tests (Pass 5).
- Direct image upload in the storefront media picker, using the existing tenant-scoped media upload endpoint (Pass 6).
- Shared dashboard/report metric view-model extraction with unit tests (Pass 6).
- Added HTTP cross-business regression tests for expense/category reads and writes, inventory item/movement reads and stock changes, and worker availability/leave reads and cancellation (Pass 6).
- Added tenant-scoped expense CSV export and a finance-page export link; customer and inventory import/export remain as existing implementations.
- Added API-level cross-business regression probes for storefronts, designs, collections and customer portal link revocation (execution still requires the .NET/PostgreSQL test environment).
- Fixed Vitest discovery: tests under `src/**/__tests__` are now included, alongside legacy `test/` tests. Added focused finance, inventory, worker-schedule and billing view-model tests, and wired their validation helpers into the relevant components.
- Narrowed `frame-ancestors` to same-origin only on tokenized `/preview/` routes; all public/other routes retain `frame-ancestors 'none'`.

## Testing and verification
- Added cross-business HTTP regression coverage for expenses/categories, inventory items/movements, and worker availability/leave; media upload/read/delete isolation still needs an HTTP probe; the newly added storefront/design/collection and customer portal revocation probes still need execution.
- Existing web unit tests cover storefront reordering and business schedules; added dashboard/report metric view-model tests. Added focused finance, inventory, worker schedule and billing helper tests. AI studio and portal still need dedicated component-level tests.
- `@types/node` remains at 22.10.7 because `registry.npmjs.org` returned `EAI_AGAIN`; upgrade to at least 22.12 and regenerate `package-lock.json` on a machine with registry access. This pass does not claim the dependency upgrade is complete.
- CI diagnostics were expanded in the previous pass, but a fresh CI run is still required to capture actual test counts, Next route table and committed migration list.

## Staff dashboard
- Run typecheck/tests for expense management, invoice draft editing and order invoice history.
- Validate leave request/approval API, permissions, overlap rules and migration against PostgreSQL.

## Storefront
- Direct upload is now wired into the storefront media picker. Verify file selection, upload rejection cases and tenant isolation with Playwright.
- Verify template-switch flow and test the editor iframe CSP in a browser. The middleware now permits same-origin framing only on the `/storefront` editor route, and only same-origin ancestors for tokenized `/preview/` routes; public storefronts and all other routes keep `frame-ancestors 'none'`.
- Install Playwright and test editing, publish and rollback.
- Verify public publish-to-render with real deployment and storage.

## Backend and integrations
- Expense CSV export is now implemented. Expense CSV import and any additional entity imports still need implementation if required by the product scope; customer and inventory import/export exist.
- Implement Redis-backed queues and SignalR Redis backplane, and connect a real web/mobile SignalR client.
- Implement verified ownership-transfer email flow.
- Confirm Data Protection keys persist across Render restarts.
- Test Paystack in test mode, signature verification, replay/idempotency and real email delivery.
- Test AI, reports, inventory reservations under concurrency, customer portal and MAUI sync/conflict behavior.

## Infrastructure / release gate
- Run Docker Compose smoke tests and verify real S3, ClamAV, Redis, DNS and storefront publish/render.
- Do not call Avzar production-ready until the relevant tests and live integration checks pass.


## Pass 7 update
- Fixed Vitest configuration so the existing `src/lib/__tests__` files are actually discovered; previously only `web/test/**/*.test.ts` was included.
- Added pure finance, inventory, worker leave-interval and billing view-model helpers with unit tests; wired checkout price validation, inventory quantity validation and leave interval validation into the corresponding UI components.
- Added HTTP cross-business tests for storefront, design, collection, and customer-portal link revocation paths. These are source additions only until the CI/PostgreSQL suite runs.
- Verification remains blocked locally: no `node_modules`, .NET SDK or Docker in the available environment.

## Pass 8 update
- Added the official ASP.NET Core SignalR StackExchange.Redis package and conditional Redis backplane registration. Setting `Redis__ConnectionString` now enables cross-replica SignalR hub fan-out; without it the app retains the in-process hub manager.
- Updated `.env.example` and `docs/ENVIRONMENT_CONFIGURATION.md` to document the backplane behavior. Redis job queues are still not implemented; the database outbox remains the email queue.
- Verification is source-level only. NuGet restore/build and a multi-instance Redis test have not run in this environment.

## Pass 9 update
- Extracted AI Studio prompt normalization into `web/src/lib/ai-studio.ts` and wired fashion-design and assistant-builder requests through it. Required fields are trimmed and validated, oversized values are rejected, and optional blank text receives a stable default before paid AI calls.
- Extracted customer-portal fragment token parsing into `web/src/lib/customer-portal.ts` and wired the portal client through it. Empty, oversized, whitespace-containing, and fragment-injected tokens are rejected before the access request; the URL fragment is still removed from browser history by the existing client flow.
- Added Vitest unit tests for AI Studio input normalization and customer-portal token parsing.
- Verification remains source-only in this environment; npm dependencies are not installed, so Vitest/typecheck/build have not run. No claim of passing tests is made.

## Pass 10 (2026-10-16)

Implemented in source (build still unverified in this environment):
- Added tenant-scoped CSV export endpoints for designs and collections. Both reuse the existing tenant-filtered list services, preserve supported list filters, page in batches of 100, cap a single export at 100,000 rows, emit UTF-8 BOM CSV, and use the shared CSV writer's formula-injection protection.
- These extend the existing customer, inventory, and expense CSV capabilities; imports for designs/collections are still not implemented.

Still open: Playwright dependency/setup and end-to-end tests; ownership-transfer email verification; Redis-backed job queue beyond the durable database email outbox; SignalR client; further cross-business HTTP tests; complete unit test coverage; Node type package upgrade; real provider and deployment checks.

Verification for this pass: source-level review only. The environment's npm registry lookup failed with `EAI_AGAIN`; no .NET SDK or Docker is available, so no build or integration tests were run. Do not treat these additions as verified or production-ready.

## Pass 11 — Playwright and ownership verification

Implemented in source; build/test execution is blocked by this environment's missing dependencies/.NET SDK and package-registry DNS failure:
- Added `@playwright/test`, a Playwright config, a staging-only authenticated lifecycle test, and a `test:e2e` script. The test creates a disposable storefront, edits and saves the draft, publishes versions 1 and 2, then restores version 1 and checks the rollback version. It refuses to run mutations unless credentials and `PLAYWRIGHT_ALLOW_MUTATIONS=true` are supplied and the base URL is staging or localhost. The email confirmation page also preserves the token when the owner must sign in first.
- Wired optional Playwright browser installation and E2E execution into CI when staging URL, credentials, and mutation opt-in are configured. The test intentionally skips when these are absent; a skipped test is not counted as passing verification.
- Changed the public ownership-transfer API from immediate transfer to an email-verification request. The owner must re-enter their password and (if enabled) second factor; Avzar sends a 30-minute, single-use link. The token hash—not the raw token—is stored. A second request cancels earlier pending links. Confirmation consumes the token and performs the role change within the same transaction and ownership lock.
- Added the ownership challenge entity/configuration/migration/model snapshot, email template, owner-facing request form on Team, confirmation page, and tests for deferred transfer, token hashing, cancellation, replay prevention, and template safety.
- Added `App__PublicBaseUrl` to `.env.example` and the environment configuration guide. It must point to the public HTTPS web origin; `App__WebBaseUrl` remains a fallback.

Verification notes:
- JSON manifests and lockfile parse successfully, and the Playwright dependency entries were added to the lockfile. Registry access returned `EAI_AGAIN`, so npm could not install Playwright and the lockfile could not be generated by npm itself; the manually updated lockfile must be regenerated with `npm install --package-lock-only` on an internet-enabled machine before release.
- Global `tsc` was attempted, but the web dependencies are not installed, so it reported missing Next.js/React/Vitest/Playwright modules and could not provide a valid project typecheck.
- The .NET SDK and Docker are unavailable here, so the new EF migration and ownership integration tests have not been compiled or run. Run CI with PostgreSQL before applying this migration to production.


## Pass 12 update — CSV parser strictness

- The shared CSV parser now rejects malformed quote placement and trailing characters after a closing quote with `csv_malformed`.
- Regression tests cover all three malformed-field cases.
- This is source-level work only. Run backend CI and the existing CSV integration tests before merging or deploying. Other integration and release blockers listed above remain open.

## Pass 12 — Redis queue test increment
- Added real-Redis Testcontainers integration tests for unkeyed enqueue, idempotent enqueue, distinct idempotency keys, and invalid payload rejection.
- Added the centrally pinned `Testcontainers.Redis` dependency.
- **Not verified:** no .NET SDK or Docker is available in this environment; CI must restore, compile and execute the tests.
- Still open: worker retry/dead-letter/recovery tests, connecting producers and handlers to actual business workflows, and multi-instance SignalR Redis backplane verification.
