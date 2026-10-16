# Avzar - master engineering handoff (rebuilt 2026-10-16)

Repository: github.com/rexxwurld/Avzar, branch `main`. The repository, CI logs and `PROGRESS.md` are the source of truth; this file only orients the next session. Rules that still apply from the V5 handoff: preserve user work, never weaken or delete tests, never claim unverified behaviour, forward-only migrations (never regenerate `InitialSchema`), never print secrets, no force-push, no deploys or real emails/payments without authorization.

## Verified (CI run #207, commit 3714953, all three jobs green)
- Backend: 680 passed / 0 failed / 0 skipped. Release build 0 warnings; model-drift gate passes.
- Migrations: eight, applied to a clean PostgreSQL 17.11 (67 tables, 33 check constraints, 113 foreign keys, 8 partial indexes, no cascading FKs on financial/audit tables).
- Web: job green; Vitest 115/115 in #212 (6 files).
- Ownership-transfer notification emails, expense/task/order location scoping: covered by the 680.

## Latest run (CI #212, commit 92f4f14, success)
- Web: Vitest 6 files / 115 tests passed (read from the job summary).
- Backend job green, but the count was not read; the TRX artifact is unread.

## Written, awaiting count confirmation
- `FinanceTaskLocationScopeApiTests.cs` (13 tests). Expected total 693. Run #212 is green, but the count is not confirmed until the TRX or `dotnet test` line is read.

## Not verified
Docker Compose, full-stack smoke test (deferred by the owner until the dashboard and storefront editor exist), storefront publish-to-render, real email (Resend), S3, ClamAV, Redis, DNS, payments, push, AI, mobile builds, Data Protection key persistence on the Render deploy.

## Web state
Public renderer and preview exist. Dashboard started: login, register, accept-invite, staff, roles, locations, customers. No orders/invoices/inventory screens, no storefront editor yet.

## Remaining Step A
Redis caching/queues/SignalR backplane, CSV import/export beyond customers, background-job recovery, email extras (suppression admin UI, webhook rate limit, Resend IP allow-list, real-provider test), PDF share links (after customer portal).

## Next
1. Read the backend TRX from #212 (expect 693) and record the real count.
2. Record the real count in `PROGRESS.md` and here.
3. Then pick the next Step A item, or continue the dashboard.
Housekeeping: bump `@types/node` to 22.x >= 22.12 next time `package.json` changes; enable Dependabot or a monthly review.
