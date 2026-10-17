# Deployment and local operation

**Status:** instructions for the current transitional architecture. A production deployment of the new Node/PWA slice has not been verified in this environment.

## Current architecture

- `backend-node/`: JavaScript + Express host, static PWA serving, and restricted proxy.
- `pwa/`: vanilla HTML/CSS/JavaScript shell.
- `backend/`: existing authoritative ASP.NET Core API and EF Core migrations.
- PostgreSQL remains the existing API's database. The Node service does not currently access it.
- `web/`: existing Next.js application, including public storefront functionality; do not remove during migration.
- `mobile/`: existing MAUI and experimental Expo sources retained as references.

## Run the PWA/Node service

Prerequisite: Node.js version matching `backend-node/package.json` (`>=22.13.0`). From the repository root:

```sh
cd backend-node
cp .env.example .env
npm ci
npm test
npm run dev
```

Configure `API_URL` in `backend-node/.env` to the existing ASP.NET API's reachable base URL. Keep it unset when you intentionally want to test the unavailable-upstream response. Do not put secrets in the file or commit it. The service listens on `HOST`/`PORT` (defaults `0.0.0.0:8081`).

## Health checks

- `GET /health/live` confirms the Node process responds.
- `GET /health/ready` probes the existing ASP.NET API's `/health` endpoint with a 2.5-second timeout and no-cache request. It returns HTTP 200 only when `API_URL` is configured and the upstream health endpoint succeeds; missing configuration, non-2xx health, invalid URLs, and network failures return a generic HTTP 503 without exposing upstream response details. This is readiness for the transitional PWA/proxy, not proof that Node has migrated database access or business workflows.

## Production checklist (not yet verified)

1. Use a managed HTTPS ingress and a supported Node LTS runtime.
2. Set `NODE_ENV=production`, `API_URL`, `PORT`, and `HOST` in the host's secret/configuration UI.
3. Keep `TRUST_PROXY_HEADERS=false` unless the ingress is controlled and sanitizes forwarded headers; validate this against the hosting provider before changing it.
4. Configure health checks to distinguish liveness from readiness.
5. Restrict upstream API network access where possible and ensure browser and API origins match the cookie/CSRF design.
6. Verify login, logout, session expiry, CSRF, tenant boundaries, and all migrated workflows against staging.
7. Keep PostgreSQL migrations under the existing EF Core process until a Node domain migration has been reviewed. Do not let a new ORM auto-sync or recreate production tables.
8. Configure backups and retention for the existing PostgreSQL service, then test a restore into an isolated environment. A backup without a successful restore test is not verified.
9. Verify logs contain no passwords, session cookies, tokens, payment secrets, or sensitive customer details.
10. Roll back by directing users to the existing web application and retaining the existing ASP.NET API. No production cutover is performed by this repository archive.

## External services

Email, Paystack, AI, object storage, Redis, and browser push require provider configuration and real integration tests. Do not mark them as delivered solely because configuration variables or interfaces exist. See `.env.example` and `docs/RUNBOOK.md` for the existing .NET deployment settings.
