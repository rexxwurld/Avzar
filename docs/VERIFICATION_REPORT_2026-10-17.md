# Avzar verification report — 2026-10-17

## Checks completed in this workspace

- `scripts/verify-local.sh` ran the repository-wide JavaScript syntax check successfully.
- Dependency-free Node proxy/PWA/readiness policy suite: **42 passed, 0 failed**.
- All 160 `.ts`/`.tsx` files under `web/src`, `web/e2e`, and `mobile/avzar-expo` passed TypeScript transpile/syntax parsing with the globally installed TypeScript 5.8.3 compiler. This is not a semantic typecheck.
- All 13 applicable JSON files checked in the source tree parse successfully.
- `backend/src/Tailoring.Api/appsettings.json` parses as valid JSON.
- A constructor-argument audit found no missed direct instantiations after making `ILocationScope` a required dependency.
- The committed web lockfile resolves Next.js 15.5.28, React/React DOM 19.3.1, `sharp` 0.35.5 and PostCSS 8.5.30. Next.js 15.5.28 is the patched version named by the maintainers for the October 14, 2026 Image Optimization RCE advisory: https://github.com/vercel/next.js/security/advisories/GHSA-2464-fhr4-w9xw. The CI workflow has blocking `npm audit --audit-level=high` steps.

## Checks blocked here (not failures, but not verified)

- Full Node API suite: `backend-node/node_modules/express/index.js` is missing from an incomplete install. Registry DNS failed, so `npm ci` could not complete.
- Web typecheck, Vitest and production build: `web/node_modules` is absent and the package registry is unreachable.
- .NET build, backend integration tests, migration drift check and the new PostgreSQL-backed inventory tests: .NET 10 SDK is not installed, and PostgreSQL/Docker is unavailable.
- Browser E2E, real email delivery, provider webhooks, staging deployment, Android/MAUI build and offline/device tests were not run.

The verification script therefore exits with status 2 and reports **0 failed checks, 3 blocked check groups**. A separate syntax-only pass covers 160 TypeScript/TSX files, but it cannot replace dependency-backed typechecking. This is deliberately not described as a green build.

## Source changes included in this package

- Required location scope dependencies for order/task/finance/expense/fitting/search/worker/customer-merge services; location checks happen before existence checks; worker lists, assignments, availability and leave management are location-filtered; customer merges require all-location scope because they rewrite cross-location order/fitting/document links.
- Durable outbox notifications for task assignment, issue escalation, next-step readiness, schedule reminders and daily overdue-task reminders.
- Inventory CSV preview/hash-bound commit, scoped location lookup and material creation, task-linked reserve/release/consume controls, and PostgreSQL regression tests for stock accounting.
- PWA finance workflows for free-form and order-linked invoice creation, draft edit/delete, issue/void, credit notes, receipt details, payment actions and expense-category management.
- Updated CI install/audit steps, local verification script, regression checks and project status documentation.

## Required before claiming 100% completion

Run CI against this exact source package and fix any compile, integration, migration, typecheck, unit, browser or dependency-audit failures. Then run staging tests for email delivery, invoice/payment workflows, inventory reservations and storefront publish/rollback. The ASP.NET API and PostgreSQL remain authoritative; this package does not complete the Node.js domain migration or authorize production cutover.
