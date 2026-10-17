# Node API Compatibility Matrix

This matrix is the migration ledger for the current ASP.NET API. The Node service in `backend-node/` is only a foundation; all domain routes below are currently **not migrated** there. Existing ASP.NET routes remain the source of truth until parity and cutover checks pass.

| Domain | Current API route family | Existing evidence | Node API status | Migration gate |
|---|---|---|---|---|
| Health | `/health` / ASP.NET health configuration | Inspect `Program.cs` | Node has `/health/live`; `/health/ready` deliberately returns 503 | Add DB readiness and deployment checks before calling it ready |
| Auth | `/api/auth/login`, `/api/auth/register-business`, `/api/auth/me`, `/api/auth/token`, `/api/auth/token/refresh`, `/api/auth/token/revoke` | `AuthController.cs`, `AccountControllers.cs` | Not migrated | Preserve account lockout, multi-business selection, two-factor inputs, session revocation, expiry and claims |
| Account security | `/api/account/*` | `AccountControllers.cs` | Not migrated | Verify password, TOTP, recovery, sessions, export/deletion and authorization |
| Staff / roles / locations | `/api/staff`, `/api/roles`, `/api/locations` | `AdminControllers.cs` | Not migrated | Verify role grants and location scope at resource level |
| Customers | `/api/customers` | `Phase2Controllers.cs`, `ImportExportControllers.cs` | Not migrated | Preserve search/paging, duplicate conflict, merge rules, version checks and customer permission behavior |
| Measurements | `/api/customers/{id}/measurements`, `/api/measurement-templates`, `/api/measurement-sets` | `Phase2Controllers.cs`; `Measurements.cs` DTOs | Not migrated | Preserve templates, validation, append-only revisions, history/compare and tenant ownership |
| Orders | `/api/orders` | `Phase2Controllers.cs`; `Orders.cs` | Not migrated | Preserve server totals, idempotency, quote/confirm/cancel guards, concurrency and frozen measurement snapshots |
| Fittings | `/api/fittings` | `Phase2Controllers.cs` | Not migrated | Preserve offset-aware time input, location scope and status transitions |
| Production workflows | `/api/workflows`, `/api/tasks`, `/api/my/tasks`, `/api/workers/*` | `Phase3Controllers.cs` | Not migrated | Preserve task prerequisites, worker ownership, checklist validation, audited overrides and task history |
| Shop schedule | `/api/business/schedule`, `/api/tasks/schedule/*`, worker schedule and leave routes | `AccountControllers.cs`, `Phase3Controllers.cs` | Not migrated | Preserve availability, capacity, holidays, leave, deduplication and durable reminders |
| Finance | `/api/invoices`, `/api/payments`, `/api/receipts`, `/api/expenses`, `/api/expense-categories` | `FinanceControllers.cs` | Not migrated | Preserve monetary precision, status transitions, location scoping, payment verification/reversal and audit history |
| Billing / entitlements | `/api/billing/subscription/*`, `/api/plans`, `/api/billing/entitlements`, `/api/platform-admin/plans` | `SubscriptionBillingController.cs`, `AdminControllers.cs` | Not migrated | Verify provider callbacks, signatures, entitlement enforcement and subscription state changes |
| Inventory | `/api/inventory/*` | `InventoryController.cs` | Not migrated | Preserve movement ledger, import preview/commit, reservation/consumption invariants |
| Designs / collections / pricing | `/api/designs`, `/api/design-categories`, `/api/collections`, `/api/pricing` | `Phase2Controllers.cs` | Not migrated | Preserve plan entitlements, media references and pricing calculations |
| Media / documents / PDFs | `/api/media`, `/public/media`, `/api/documents`, PDF endpoints | `MediaControllers.cs`, `DocumentsController.cs`, `PdfControllers.cs` | Not migrated | Preserve ownership checks, scanning/re-encoding, signed access and content-disposition safety |
| AI | `/api/ai/*` | `AiController.cs` | Not migrated | Preserve server-only secrets, plan gates, provider failures and usage/cost controls |
| Storefront / inquiries | storefront API routes, `/public/*`, `/api/inquiries`, Next.js `/sites/[slug]/*` renderer | `StorefrontControllers.cs`, `web/src/app/sites/` | Not migrated | Public renderer must remain hosted and published storefronts must survive API transition |
| Email/webhooks | `/api/webhooks/email/*`, outbox-backed internal services | `EmailWebhooksController.cs`, application email code | Not migrated | Verify signatures, suppression behavior, idempotency, retries and actual provider delivery |
| Search / reports / imports | `/api/search`, `/api/reports`, CSV import/export endpoints | `ImportExportControllers.cs`, `ReportsController.cs` | Not migrated | Preserve filtering, pagination, strict CSV parsing and permission boundaries |

## Contract migration rules

- For every endpoint, record request JSON, response JSON, status codes, error codes, permission name, tenant/location scope, idempotency/concurrency behavior, and tests before implementing its Node replacement.
- Prefer contract fixtures derived from the current DTOs and integration tests. Do not expose C# implementation types to the mobile app; use a stable TypeScript API contract.
- Do not attempt to share ASP.NET cookies with the Node service. Mobile bearer tokens and web cookie/CSRF sessions have different security models.
- Existing JWT signing configuration is server-side. If Node issues tokens, coordinate issuer/audience/keys and rotation deliberately. Do not change token validation on production clients in the same step as a wholesale API cutover.
- During staged migration, route each domain to one authoritative writer. Do not dual-write production records without an explicit transaction/outbox/reconciliation design.
- Each migrated domain must pass authorization, cross-tenant access, invalid input, conflict, idempotency, and regression tests before traffic is switched.
