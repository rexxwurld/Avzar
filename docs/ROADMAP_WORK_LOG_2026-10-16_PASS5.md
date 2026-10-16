# Avzar roadmap work log — Pass 5

## Changes in this pass

- Added `web/src/lib/storefront-editor.ts`, a small immutable reorder helper used by the storefront editor's reorder controls.
- Added a drag-and-drop section ordering panel to the storefront editor. It remains draft-only until the existing Save draft action is used, and keeps the existing move-up/move-down controls as an accessible fallback.
- Added Vitest unit tests for business-week validation/time normalization and storefront section reordering.

## Verification

- The previous Pass 4 archive passed `unzip -t` before extraction.
- Node.js 22.16.0 and npm 10.9.2 are available.
- `npm ci --ignore-scripts --no-audit --no-fund --prefer-offline` timed out in this environment. `web/node_modules` is therefore not available, so Vitest, TypeScript typecheck and Next build were not run.
- .NET SDK and Docker are not available in this environment; backend integration tests and infrastructure smoke tests were not run.

## Still outstanding

- Cross-business HTTP coverage for expenses, storefront/media/designs/collections, inventory, customer portal grants and worker availability/leave.
- Broader web unit coverage for finance, inventory, schedule, reports, billing, AI studio and portal; dashboard/report logic extraction.
- `@types/node` upgrade and lockfile regeneration (registry access timed out; current lockfile unchanged).
- Storefront media upload picker (the existing media library is still the upload location), editor iframe CSP policy, Playwright installation and editor/publish/rollback browser tests.
- Ownership-transfer email verification, CSV workflows for additional entities, Redis queues/backplane/client, and live integration checks.
- Paystack test-mode, email delivery, MAUI build/sync/conflict tests, Docker/S3/ClamAV/Redis/DNS and published storefront smoke tests.

No production-readiness claim is made by this pass.
