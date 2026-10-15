# Static review (updated 2026-10-15)

The current review workspace has no .NET SDK, Docker, or PostgreSQL client, and could not resolve GitHub. No build or test was independently run here. The supplied backend TRX artifact reports 478/478 passed, and the supplied generated migration was inspected statically; see `PROGRESS.md` for the evidence boundaries. The source archive has no `.git` directory, so live branch ancestry and history could not be checked.

## Generated migration artifact reviewed

The supplied `20261015172445_CiCheck.cs` contains 181 `CreateIndex` calls, filtered indexes (including pending-status and non-null idempotency/number filters), and table-level check constraints. Examples include:
- Monetary/order consistency: order subtotal/discount/total, invoice subtotal/tax/total, payment amounts and verification metadata, invoice line totals, expense amounts, and credit-note amounts.
- State/data integrity: published storefront requires a published version; archived designs must be private; verified/reversed financial records require corresponding audit metadata/reasons; production-task completion timestamp and status must agree.
- Other guards: business-hour validity, positive media/document dimensions/sizes, non-self dependencies, and nonnegative usage counters.

This is static evidence only. It does not prove PostgreSQL accepted the migration, all composite tenant FKs/alternate keys have the intended delete behaviour, or concurrent flows are correct. Review all table foreign-key declarations, unique/filtered index definitions, and financial/audit delete paths against a live PostgreSQL schema before release.

## Looked fine on reading (still unverified)
- Raw SQL in `EntitlementService.ConsumeAsync`, `DocumentNumbers.NextAsync`, `OrderService.NextOrderNumberAsync` (`SqlQuery<long>` + `ToListAsync` then `Single()`, which avoids EF wrapping the INSERT in a subquery) and `Locks.AcquireAsync` (`ExecuteSqlAsync`).
- `UsagePeriod.StartOf` returns `DateTimeKind.Utc`, which Npgsql requires for timestamptz parameters.
- `JsonUnmappedMemberHandling.Disallow` does not affect a `JsonElement Props` member's content.
- `ToTimeLimitedDataProtector` is reachable (Infrastructure references `Microsoft.AspNetCore.App`).
- All test files have `using Xunit;`. Next 15 `params` are typed as `Promise<...>` and awaited.

## Fixed
- `web/middleware.ts`: a client could send its own `x-site-root` header on a non-tenant host. It is now deleted before being set.

## Needs a decision or follow-up
1. **ImageSharp licensing and security verification.** The package is now pinned to 4.1.2. The Release build requires a valid Six Labors license; a current official advisory review and a real Release restore/build remain outstanding. See `docs/RUNBOOK.md` section 8.
2. **CSP vs the editor preview (Step B).** The public site sends `frame-ancestors 'none'`, which will block the dashboard's iframe preview. The `/preview/*` route needs a per-route `frame-ancestors` allowing only the dashboard origin.
3. **Nonce CSP makes pages dynamically rendered**, so the public site is not static or CDN-cacheable as the handoff intends. Decide: keep nonce CSP, or use hash-based CSP for cacheable pages.
4. **Not checkable by reading:** EF translation of the large projections (`InvoiceService.ListAsync`, `DesignService.ListAsync`, `CollectionService`, `PublicSiteService`), and `DnsClient 1.8.0` validity. The generated migration's indexes and check constraints were inspected statically as recorded above; database application and complete FK/delete-behaviour validation remain outstanding.
