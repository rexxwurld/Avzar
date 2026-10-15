# Static review (no build, no tests run)

Reviewed by reading only. No .NET SDK, Docker or network was available, so nothing here is verified by a build or test run.

## Looked fine on reading (still unverified)
- Raw SQL in `EntitlementService.ConsumeAsync`, `DocumentNumbers.NextAsync`, `OrderService.NextOrderNumberAsync` (`SqlQuery<long>` + `ToListAsync` then `Single()`, which avoids EF wrapping the INSERT in a subquery) and `Locks.AcquireAsync` (`ExecuteSqlAsync`).
- `UsagePeriod.StartOf` returns `DateTimeKind.Utc`, which Npgsql requires for timestamptz parameters.
- `JsonUnmappedMemberHandling.Disallow` does not affect a `JsonElement Props` member's content.
- `ToTimeLimitedDataProtector` is reachable (Infrastructure references `Microsoft.AspNetCore.App`).
- All test files have `using Xunit;`. Next 15 `params` are typed as `Promise<...>` and awaited.

## Fixed
- `web/middleware.ts`: a client could send its own `x-site-root` header on a non-tenant host. It is now deleted before being set.

## Needs a decision or follow-up
1. **ImageSharp 3.1.7 is likely vulnerable.** A secondary source (ABP support thread) reports five advisories published 2026-10-07, three high, with no patched 3.1.x and 4.1.2 as the first fixed version. Confirm on the official ImageSharp advisories page. Options: move to 4.x (check API changes and the Split License) or swap `IImageProcessor` to SkiaSharp. Not changed here.
2. **CSP vs the editor preview (Step B).** The public site sends `frame-ancestors 'none'`, which will block the dashboard's iframe preview. The `/preview/*` route needs a per-route `frame-ancestors` allowing only the dashboard origin.
3. **Nonce CSP makes pages dynamically rendered**, so the public site is not static or CDN-cacheable as the handoff intends. Decide: keep nonce CSP, or use hash-based CSP for cacheable pages.
4. **Not checkable by reading:** EF translation of the large projections (`InvoiceService.ListAsync`, `DesignService.ListAsync`, `CollectionService`, `PublicSiteService`), the generated migration's constraints and indexes, and `DnsClient 1.8.0` validity.
