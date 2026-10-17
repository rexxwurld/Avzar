# Avzar feature inventory

**Updated:** 2026-10-17  
**Source:** static inspection of the supplied project archive plus PWA Pass 17 Phase 4/5 changes; the new screens have not been verified end-to-end.  
**Important:** this archive has no `.git` metadata. The status below does not establish the current GitHub branch or deployed commit. No production API or database was contacted during this work.

Statuses mean: **present** = source exists; **verified** = tested in the stated environment; **partial** = only part of the user workflow exists; **not migrated** = still belongs to the ASP.NET API or old Next.js UI; **blocked** = requires unavailable dependencies/services or real-device/provider verification.

| Feature | Existing implementation observed | Current PWA/Node status | Next work |
|---|---|---|---|
| PWA shell | Existing Next.js app manifest and install helper | **Partial; implemented in `pwa/`** | Installability, browser checks, accessibility and deployment verification |
| Browser sign-in/session/logout | ASP.NET cookie session + CSRF; existing Next.js forms and same-origin proxy | **Partial; UI uses a restricted Node proxy to the existing API** | Verify API integration; add multi-business selection, 2FA and recovery states |
| Node service | `backend-node/` had an unconnected Express/TypeScript health scaffold | **Converted to JavaScript Express foundation** | Dependency installation/tests; database health; migrate domain by domain |
| PostgreSQL | Existing EF Core models and migrations | **Preserved; Node does not write business data** | Schema map and isolated staging integration |
| Customers | ASP.NET APIs and Next.js customer UI | **PWA first slice implemented against the existing API** | Verify on staging; add communication log, CSV tools, merge UI and browser tests; keep duplicate detection and concurrency behavior covered |
| Measurements | Templates, sets, revisions/history/compare in existing API | **PWA create-profile, revision, and history screens added against existing API; not migrated to Node** | Verify with staging data; add comparison UI and browser/API integration tests |
| Orders and production | Existing order lifecycle, production tasks and worker APIs | **PWA order list/detail/create/edit, lifecycle actions, task list/detail, assignment and worker task actions wired to existing API; not migrated to Node** | Staging verification, concurrency/lifecycle edge cases, API contract tests and browser tests |
| Worker/shop scheduling | Existing schedule APIs and schedule-related UI | **PWA worker availability, leave/calendar, skills and shop schedule preview/apply wired to existing API; not migrated to Node** | Verify time-zone behavior, leave conflict rules, schedule revalidation and notification delivery in staging |
| Finance/payments | Existing invoice, payment, receipt, expense and credit-note code | **Not migrated to PWA/Node** | Verify monetary invariants, permissions, idempotency and payment webhooks |
| Staff/roles/locations | Existing ASP.NET authorization and web screens | **PWA staff list/invite, activation, role/location assignment wired to existing API; not migrated to Node** | Staging authorization tests and real invitation-email verification |
| Inventory | Existing inventory API and UI | **Not migrated to PWA/Node** | Preserve stock ledger, reservations, imports and permissions |
| AI studio | Existing AI endpoints and web UI | **Not migrated to PWA/Node** | Verify provider credentials, entitlements, usage/cost limits and file flow |
| Public storefronts | Existing Next.js public storefront renderer and API | **Preserve existing implementation** | Keep public renderer available during business-dashboard migration |
| Subscriptions | Existing plan/entitlement and billing code | **Not migrated to PWA/Node** | Contract tests and server-side entitlement parity |
| Offline actions | MAUI starter contains an offline task-action queue; browser PWA shell caches static assets only | **Partial; no business writes are queued yet** | Design IndexedDB queue, idempotency, conflict handling and sensitive-data cleanup |
| Notifications | Existing SignalR code and some backend scheduling foundations | **Not verified end-to-end** | Implement and test Web Push and real scheduled delivery on Android/iPhone |
| Expo / MAUI | Both experimental mobile folders are present in the archive | **Not the initial product direction; retained as reference** | Do not delete until their useful behavior is inventoried and PWA replacement needs are understood |

## First implementation slice

- Added `pwa/index.html`, `pwa/styles.css`, and `pwa/app.js` as a vanilla-JavaScript mobile-first shell.
- Added an installable manifest and static-asset service worker. The service worker explicitly bypasses API routes and does not cache user data.
- Added an Express static host and a constrained `/backend/api/**` proxy to keep the existing ASP.NET API authoritative during transition.
- The PWA sign-in view calls the existing CSRF, login, session, and logout endpoints, including multi-business selection and two-factor/recovery-code states.
- Added real customer list/search/pagination, create/edit, duplicate confirmation, status change, detail view, and measurement-profile listing against existing ASP.NET endpoints. The PWA does not invent sample customer/order/finance records.
- Added measurement-profile creation from active templates, template-driven numeric fields, append-only revision recording, and revision-history viewing. All writes continue to use the existing permission-protected ASP.NET API; no Node database writes were introduced.
- Added permission-gated order search/filter/pagination and order detail views, then expanded them with create/edit (multiple garment lines), quote/confirm/cancel/deliver actions, and production-task generation against the existing ASP.NET API. Server-side pricing, permission, status, version, and lifecycle rules remain authoritative.
- Added production task list/detail, assignment, worker task actions, manager unblock/cancel/override actions, individual worker availability/leave/calendar/skills, staff invite/activation/role/location management, and shop schedule preview/apply screens. Schedule apply submits the preview's versioned plan for server revalidation.
- Converted the parallel Node scaffold from TypeScript to JavaScript to align with the owner's maintainable stack.
- Added proxy-path/origin tests and Express integration tests. Full test execution remains pending because package installation timed out in this environment.

## Known limitations of this slice

- Node does not yet connect to PostgreSQL and is not ready to replace the ASP.NET API. `/health/ready` deliberately remains 503.
- The proxy's production behavior and cookie semantics still need verification against a staging deployment.
- Multi-business selection and two-factor sign-in are not implemented in the PWA yet.
- Customer management and core measurement profile/revision screens have a first PWA implementation. Order and production, staff, worker schedule and shop schedule screens are now connected to existing ASP.NET endpoints, but remain unverified end-to-end. Finance, inventory, AI studio and storefront editing screens have not yet been migrated to the PWA. Measurement comparison UI remains open.
- The service worker caches only the static shell (cache version `avzar-shell-v2`). It does not implement offline business mutations or synchronization.
- iPhone/Android installation, push behavior, and browser-specific limitations have not been tested on physical devices.
