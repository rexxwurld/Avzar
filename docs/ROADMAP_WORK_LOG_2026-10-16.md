# Roadmap implementation work log — 2026-10-16

This log records code changes made in the roadmap continuation pass. It does not claim production readiness.

## Implemented in this pass

- Added a platform-admin email suppression list endpoint and read-only web page, protected by the configured `PlatformAdmin:Emails` allowlist.
- Added a named rate-limit policy for email webhooks.
- Expanded CI output for verbose web tests, build logs/route-table capture, and migration inventory.
- Added `InvoiceDraftEditor` to the invoice detail page. It edits due date, notes, terms and (for free-form draft invoices only) line descriptions, quantity, unit price, discount and per-line tax. It submits the invoice version for optimistic concurrency; order-generated invoice lines remain locked.
- Added an environment configuration guide that maps the existing `.env.example` settings to intended use and explicitly calls out unverified provider wiring.

## Verification status

- ZIP archive integrity checked after packaging.
- Static inspection only for newly edited C# and TypeScript/TSX files. No .NET SDK or Docker is available in this execution environment, and npm registry access previously timed out; therefore backend compilation, frontend typecheck/build, automated tests, migrations against PostgreSQL and provider integration tests remain **unverified**.
- `@types/node` remains at the version currently in the supplied lockfile until dependency registry access is available and the lockfile can be regenerated reproducibly.

## Still open

- Cross-business isolation tests for remaining finance, storefront/media/design/collection, inventory, customer portal and worker availability/leave routes.
- Automated UI/unit/Playwright coverage and extraction of dashboard/report logic.
- Worker leave request/approval workflow; remaining staff expense/category and order invoice-history UI gaps.
- Drag/drop storefront canvas, media picker polish and safe editor preview policy.
- Ownership-transfer email verification, broader CSV import/export, Redis-backed SignalR/queue wiring and real-client integration.
- Paystack test-mode, email delivery/webhook, AI/reporting/inventory concurrency, customer portal, MAUI build/sync/conflict tests.
- Docker smoke tests and staging verification of object storage, ClamAV, Redis, DNS/custom domains and storefront publishing.
