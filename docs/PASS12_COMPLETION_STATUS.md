# Pass 12 completion status — 2026-10-16

This report separates source changes from evidence. It must not be interpreted as a production-readiness sign-off.

## Added in this continuation

- Added Redis Streams queue primitives (`IRedisJobQueue`, `IRedisJobHandler`, and a hosted consumer) with atomic idempotency-key enqueue, consumer-group recovery, five-attempt retry, and dead-letter stream. Enabled only when `Redis__ConnectionString` is configured; local Docker Compose now includes Redis persistence.
- Important boundary: no application-specific job producers/handlers are registered yet, and the queue has not been exercised against a live Redis instance. It is a foundation, not a completed background-jobs feature.
- Added a .NET MAUI `BusinessEventsClient` using `Microsoft.AspNetCore.SignalR.Client`.
- The client requests a refreshed access token from `AvzarApi`, uses automatic reconnect, subscribes to `task.updated`, and causes the authoritative task list to reload.
- Updated MAUI dependency injection and page lifecycle to start/stop the SignalR connection.
- Updated the API's Smart authentication selector and JWT bearer message handler to support SignalR WebSocket `access_token` only under `/hubs/*`. Normal API routes do not accept query-string bearer tokens.
- Added an HTTP integration test for SignalR negotiate using the query token and documented the client behaviour and limits.

## Status of the requested work

| Work item | Status | Evidence / remaining work |
|---|---|---|
| Redis-backed durable job queue | Queue primitives added; integration incomplete | Redis Streams consumer group, idempotent enqueue, recovery claim, retry and dead-letter handling are present. Still needs registered business handlers/producers, live Redis tests, graceful-shutdown/replay tests and production Redis persistence/failover verification. |
| SignalR client | Source implemented; runtime unverified | MAUI client and API query-token support added. Requires .NET 10 MAUI build plus live negotiate, WebSocket, reconnect, token expiry/revocation and Redis-backplane tests. |
| Remaining security tests | Partially covered; not fully verified | Added a SignalR query-token negotiation regression test. Full API authorization, tenant isolation, CSRF, session revocation, rate-limit and concurrency suites must run in CI against PostgreSQL. |
| Web unit tests | Existing tests only; not rerun | `npm ci` timed out in this environment. Vitest, typecheck and production build remain unverified here. |
| Additional import/export workflows | Not implemented in this pass | Current CSV import/export is customer-only. Orders, inventory, measurements and other record types need separate preview/validation/commit/export workflows with tenant and permission tests. |
| Dependency updates | Partially updated | Added the centrally pinned .NET 10 SignalR client package. Full NuGet/npm restore and advisory review remain unverified; do not treat this as a complete dependency audit. |
| Paystack live integration | Not tested | Requires non-production provider credentials, callback URL, reachable HTTPS deployment and verification of success, failure, duplicate webhook, wrong amount/currency and replay cases. Never use production credentials in test logs. |
| Real email delivery | Not tested | Requires configured SMTP/Resend credentials and a verified sender/domain; test delivery, bounce/suppression webhooks, retries and duplicate dispatch. `log` transport is not real delivery. |
| MAUI synchronization | Partially implemented; device-unverified | Existing offline task queue plus new realtime refresh are source features. Android/iOS builds, offline conflict/replay tests and real device testing remain. iOS build requires macOS/Xcode. |
| Deployment infrastructure | Not integration-tested | Docker Compose/full-stack smoke tests, production migrations, persistent Data Protection keys, TLS/proxy, DNS/storefront routing, backups and health checks remain unverified. |

## Verification attempted for this archive

- ZIP integrity check: passed.
- `npm ci --ignore-scripts --no-audit --no-fund`: timed out; web test/typecheck/build cannot be claimed.
- .NET SDK: unavailable in this environment; backend compilation/tests and MAUI build cannot be claimed.
- Docker: unavailable; PostgreSQL, Redis, provider and deployment integration tests cannot be run here.
- Added Redis Streams queue source and Compose Redis service, but could not compile or run queue tests without .NET SDK/Docker.

## Required before calling these items complete

1. Run backend Release build and all integration tests in CI with PostgreSQL/Testcontainers.
2. Run web `npm ci`, `npm run typecheck`, `npm test`, and `npm run build`.
3. Build Android MAUI and test sign-in, hub negotiation, event delivery, reconnection, session revocation, offline queue replay and conflict cases on a device/emulator.
4. Register real application job producers and handlers, then test the Redis Streams queue against Redis for idempotent enqueue, worker crash recovery, retry, dead-letter and graceful shutdown; do not count the SignalR backplane or Redis rate limiter as a queue.
5. Implement import/export for additional record types with preview, hash confirmation, formula-safe exports, permission enforcement, tenant isolation and bounded uploads.
6. Run Paystack and email provider tests with non-production credentials, then execute Docker/deployment smoke tests.

No live credentials, external service sessions, or device runtime were available in this environment. The above external tests cannot honestly be marked complete without those dependencies.
