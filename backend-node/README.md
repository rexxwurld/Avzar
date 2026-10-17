# Avzar PWA + Node.js foundation

This is the first implementation slice for the agreed JavaScript-first, mobile-first PWA direction.

## Current behavior

- Plain HTML, CSS, and JavaScript PWA shell in `../pwa/`.
- Installable web manifest, existing Avzar icons, service worker, and a static-asset-only cache.
- Sign-in, session check, and sign-out UI using the existing ASP.NET API contract.
- Express serves the PWA from the same origin and provides a restricted `/backend/api/**` proxy during migration.
- The proxy forwards only allowlisted headers, refuses present cross-origin browser writes, blocks provider webhook paths, and disables caching for API responses. Client-IP forwarding is disabled by default; enable `TRUST_PROXY_HEADERS=true` only if the hosting ingress is trusted to sanitize/append `X-Forwarded-For`.
- `/health/live` reports process liveness. `/health/ready` deliberately remains `503 not_ready`; Node is not the production API and no Node database migration has been completed.
- No fake customer/order/finance data and no business API is claimed to have migrated.

## Local setup

Use Node.js 22.13 or newer.

```sh
cp .env.example .env
npm install
npm test
npm run dev
```

Set `API_URL` to the existing ASP.NET API reachable from this Node service. Do not commit real credentials. The example URL is for local development only.

Open `http://localhost:8081`. The PWA can serve its shell without the upstream API, but sign-in requires a reachable existing API.

## Migration safety

The existing ASP.NET API remains authoritative. This Node service does not connect to PostgreSQL or write business data. Do not switch production traffic until database mapping, authentication compatibility, tenant isolation, domain parity, automated tests, staging verification, and rollback are complete.

The initial PWA is a foundation, not a completed product. Customer list/detail/create/edit and measurement profile/revision/history screens are wired to the existing ASP.NET API but still require staging verification. Multi-business selection and two-factor login in the PWA, orders, scheduling, offline write queues, and the remaining business modules are still to be implemented and tested.
