# Avzar feature inventory

**Updated:** 2026-10-17  
**Source:** static inspection of the supplied project archive plus PWA Pass 17 and subsequent inventory/finance PWA slices; new screens have not been verified end-to-end.  
**Important:** this archive has no `.git` metadata. The status below does not establish the current GitHub branch or deployed commit. No production API or database was contacted during this work.

Statuses mean: **present** = source exists; **verified** = tested in the stated environment; **partial** = only part of the user workflow exists; **not migrated** = still belongs to the ASP.NET API or old Next.js UI; **blocked** = requires unavailable dependencies/services or real-device/provider verification.

| Feature | Existing implementation observed | Current PWA/Node status | Next work |
|---|---|---|---|
| PWA shell | Existing Next.js app manifest and install helper | **Partial; implemented in `pwa/`** | Installability, browser checks, accessibility and deployment verification |
| Browser sign-in/session/logout | ASP.NET cookie session + CSRF; existing Next.js forms and same-origin proxy | **PWA sign-in/session/logout, multi-business selection and 2FA/recovery states present against the existing API** | Verify API integration and session expiry/logout behavior in staging |
| Node service | `backend-node/` had an unconnected Express/TypeScript health scaffold | **Converted to JavaScript Express foundation** | Dependency installation/tests; database health; migrate domain by domain |
| PostgreSQL | Existing EF Core models and migrations | **Preserved; Node does not write business data** | Schema map and isolated staging integration |
| Customers | ASP.NET APIs and Next.js customer UI | **PWA customer CRUD, duplicate handling, communication log, permission-gated merge UI, and customer portal link send/revoke plus public portal view implemented against existing API** | Live overview includes API customer counts; verify on staging; add CSV tools and browser tests; keep duplicate detection and concurrency behavior covered |
| Measurements | Templates, sets, revisions/history/compare in existing API | **PWA create-profile, revision, history, and revision-comparison screens added against existing API; not migrated to Node** | Verify with staging data and browser/API integration tests |
| Fittings | Existing ASP.NET fitting appointment endpoints | **PWA list/create/status workflow added against existing API** | Verify appointment validation, timezone behavior and permissions in staging |
| Orders and production | Existing order lifecycle, production tasks and worker APIs | **PWA order list/detail/create/edit, lifecycle actions, task list/detail, assignment and worker task actions wired to existing API; not migrated to Node** | Staging verification, concurrency/lifecycle edge cases, API contract tests and browser tests |
| Worker/shop scheduling | Existing schedule APIs and schedule-related UI | **PWA worker availability, leave/calendar, skills and shop schedule preview/apply wired to existing API; local-date initialization fixed; not migrated to Node** | Verify time-zone behavior, leave conflict rules, schedule revalidation and notification delivery in staging |
| Finance/payments | Existing invoice, payment, receipt, expense and credit-note code | **Partial PWA slice**: invoice list/detail/issue, payment record/verify/reject, credit-note issue and receipt detail, plus expense list/create/summary/export/void call the existing API; share/provider flows are not fully surfaced | Verify monetary invariants, permissions, idempotency and payment webhooks |
| Staff/roles/locations | Existing ASP.NET authorization and web screens | **PWA staff list/invite, activation, role/location assignment wired to existing API; invitation errors now display and allow retry; not migrated to Node** | Staging authorization tests and real invitation-email verification |
| Inventory | Existing inventory API and UI | **Partial PWA slice**: stock list, material create, receive/adjust, low-stock status and movement history; task reservation/consumption, import workflows and end-to-end verification remain | Preserve stock ledger, reservations, imports and permissions |
| AI studio | Existing AI endpoints and web UI | **Not migrated to PWA/Node** | Verify provider credentials, entitlements, usage/cost limits and file flow |
| Business setup/settings | Existing ASP.NET business schedule and PDF branding APIs | **PWA opening-hours, holiday, and document-branding screens added against existing API; account security screen uses existing password/2FA/session APIs** | Verify schedule/holiday writes, branding persistence, password changes, 2FA setup and session revocation in staging |
| Public storefronts | Existing Next.js public storefront renderer and API | **Preserve existing implementation** | Keep public renderer available during business-dashboard migration |
| Subscriptions | Existing plan/entitlement and billing code | **Not migrated to PWA/Node** | Contract tests and server-side entitlement parity |
| Offline actions | MAUI starter contains an offline task-action queue; browser PWA shell caches static assets only | **Partial; no business writes are queued yet** | Design IndexedDB queue, idempotency, conflict handling and sensitive-data cleanup |
| Notifications | Existing email outbox, dispatcher, SignalR and schedule-assignment notifications | **Partial**: task assignment, blocked-task escalation to task managers, newly eligible next-step notices and schedule email queueing are written to the durable email outbox; no verified Web Push/mobile delivery and overdue reminder worker | Compile and integration-test the new task-notification paths; verify retries, deduplication and real delivery; implement/test Web Push and overdue reminders |
| Expo / MAUI | Both experimental mobile folders are present in the archive | **Not the initial product direction; retained as reference** | Do not delete until their useful behavior is inventoried and PWA replacement needs are understood |

## First implementation slice

- Added `pwa/index.html`, `pwa/styles.css`, and `pwa/app.js` as a vanilla-JavaScript mobile-first shell.
- Added an installable manifest and static-asset service worker. The service worker explicitly bypasses API routes and does not cache user data.
- Added an Express static host and a constrained `/backend/api/**` proxy to keep the existing ASP.NET API authoritative during transition.
- The PWA sign-in view calls the existing CSRF, login, session, and logout endpoints, including multi-business selection and two-factor/recovery-code states.
- Added real customer list/search/pagination, create/edit, duplicate confirmation, status change, detail view, and measurement-profile listing against existing ASP.NET endpoints. The PWA does not invent sample customer/order/finance records.
- Added measurement-profile creation from active templates, template-driven numeric fields, append-only revision recording, revision-history viewing, and revision comparison via the existing API. All writes continue to use the existing permission-protected ASP.NET API; no Node database writes were introduced.
- Added permission-gated order search/filter/pagination and order detail views, then expanded them with create/edit (multiple garment lines), quote/confirm/cancel/deliver actions, and production-task generation against the existing ASP.NET API. Server-side pricing, permission, status, version, and lifecycle rules remain authoritative.
- Added a live business overview, business opening-hours/holiday/document-branding settings, account security/password/2FA/session screens, customer communication log and merge UI, fitting appointments, production task list/detail, assignment, worker task actions, manager unblock/cancel/override actions, individual worker availability/leave/calendar/skills, staff invite/activation/role/location management, and shop schedule preview/apply screens. Schedule apply submits the preview's versioned plan for server revalidation.
- Converted the parallel Node scaffold from TypeScript to JavaScript to align with the owner's maintainable stack.
- Added proxy-path/origin tests and Express integration tests. Full test execution remains pending because package installation timed out in this environment.

## Known limitations of this slice

- Node does not yet connect to PostgreSQL and is not ready to replace the ASP.NET API. `/health/ready` deliberately remains 503.
- The proxy's production behavior and cookie semantics still need verification against a staging deployment.
- Multi-business selection and two-factor sign-in UI are present; both require staging verification against real authentication policies and provider behavior.
- Customer management, communication history/merge, measurement profile/revision/history/comparison, order lifecycle, fittings, production tasks, staff management, worker schedules, shop scheduling, business settings and account security are connected to existing ASP.NET endpoints, but remain unverified end-to-end. Finance and inventory now have partial PWA screens; expenses, credit notes, receipts, inventory imports/task material actions, AI studio and complete storefront editing still need work.
- The service worker caches only the static shell (cache version `avzar-shell-v6`). It does not implement offline business mutations or synchronization.
- iPhone/Android installation, push behavior, and browser-specific limitations have not been tested on physical devices.
