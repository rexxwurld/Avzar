# Avzar working-copy verification report — 2026-10-16

## Source changes in this archive

- Worker-specific weekly availability, approved leave intervals, daily workload capacity, location-filtered individual calendars, management endpoints and `/workers/schedule` UI.
- Schedule preview/apply integration with worker availability/leave/capacity; queued assignment emails and future 24-hour reminders through the durable email outbox; unique deduplication keys; cancellation/purging of pending messages for superseded slot times.
- Forward migrations `20261016230000_AddEmailDeduplication` and `20261016233000_AddWorkerAvailabilityAndLeave`, with updated EF model snapshot and migration target models.
- Storefront editor controls for custom pages under entitlements, page slug/SEO/navigation, section style, existing media selection, template-switch preview/apply, custom-domain DNS verification, and existing save/preview/publish/rollback paths.
- Added planner unit-style cases and PostgreSQL integration tests for worker availability/leave/capacity, schedule preview/apply, queued notifications and stale-preview rejection. Existing order-to-production, storefront lifecycle, and finance backend integration tests remain in place.
- Hardened leave cancellation to verify that the worker is active in the current business and that the leave row belongs to that business, closing a cross-tenant cancellation path found during follow-up review.

## Checks performed in this environment

- TypeScript/TSX syntax transpilation: **104 files, zero syntax diagnostics**.
- `package.json` and `tsconfig.json` JSON parsing: passed.
- Backend `.csproj` XML parsing: passed.
- Follow-up security review: leave cancellation now checks active membership and business ownership before mutation.
- .NET SDK availability: **not available**; backend build and integration tests could not run.
- PostgreSQL/Docker availability: **not available**; migrations were not applied and PostgreSQL integration tests were not run.
- npm dependency installation: blocked because the registry could not resolve and the offline npm cache lacked required packages.
- Full TypeScript typecheck, Vitest, and Next production build: **not run successfully**. A bare global `tsc` invocation reports missing Next/React/Node dependencies; that output is not a valid project typecheck.
- Git metadata: absent from the source archive. No branch/commit relationship to live `main` can be established here.
- Push, merge and deployment: not performed.

## Required before merge/deployment

1. Run .NET Release build and all backend integration tests in CI.
2. Apply the new migrations to a clean disposable PostgreSQL database, verify model-snapshot drift, and test rollback/upgrade paths.
3. Run web `npm ci`, typecheck, Vitest and Next production build; add browser E2E for schedule management and storefront save/preview/template-switch/publish/rollback.
4. Verify cross-tenant/location access, concurrent schedule apply, notification deduplication/retry/superseded-reminder handling, and the full order-to-production and finance workflows against PostgreSQL.
5. Verify real email provider delivery and webhook status in staging. Queueing a message is not delivery confirmation.
6. Run Docker Compose/full-stack smoke tests and the remaining security/infrastructure checks.

This report intentionally distinguishes source changes from verification evidence. Do not treat the archive as release-verified until these checks pass.
