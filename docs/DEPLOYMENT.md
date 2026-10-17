# Deployment and local operation

**Status:** instructions for the current transitional architecture. The owner deleted the `avzar-api` Render service on 2026-10-17; recreate it with the section below. The Node/PWA service has not been deployed or verified.

## Current architecture

- `backend-node/`: JavaScript + Express host, static PWA serving, and restricted proxy.
- `pwa/`: vanilla HTML/CSS/JavaScript shell.
- `backend/`: existing authoritative ASP.NET Core API and EF Core migrations.
- PostgreSQL remains the existing API's database. The Node service does not currently access it.
- `web/`: existing Next.js application, including public storefront functionality; do not remove during migration.
- `mobile/`: the experimental Expo source (`mobile/avzar-expo`), kept as reference. The MAUI project is no longer in the repository.

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

## Recreate the API on Render

Built from the repository files (`backend/Dockerfile`, `docker-compose.yml`, `docs/ENVIRONMENT_CONFIGURATION.md`, `docs/RUNBOOK.md`) and Render's documentation. Not yet run; check the first deploy log.

**1. Database.** Reuse the old PostgreSQL instance if it still exists: point `ConnectionStrings__Default` at it and all data and Data Protection keys are kept. Otherwise create a new Render Postgres. A Free Render Postgres expires 30 days after creation (14-day grace period) and has no backups, and a Free web service spins down after 15 minutes idle with an ephemeral filesystem. Use a paid database for anything that matters.

**2. Web service.** New > Web Service > the `rexxwurld/Avzar` repo, branch `main`.

| Setting | Value |
|---|---|
| Language | Docker |
| Root Directory | `backend` (the Dockerfile copies `Directory.Build.props`, `Directory.Packages.props`, `global.json` and `src/` from there) |
| Health Check Path | `/health` |
| Secret File | name `sixlabors_lic`, contents = the full `sixlabors.lic` file. The Dockerfile requires a build secret with id `sixlabors_lic`; Render exposes a secret file under that id, so the build fails without it |

**3. Environment variables.**

| Variable | Value |
|---|---|
| `PORT` | `8080` (the image listens on 8080; if Render reports no open port, check this first) |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `ConnectionStrings__Default` | Npgsql key=value form: `Host=<internal host>;Port=5432;Database=<db>;Username=<user>;Password=<password>` (Render shows a URL form; convert it, URL-encode special characters) |
| `Database__MigrateOnStartup` | `true` for the first deploy; review the migration log |
| `Billing__SeedPlansOnStartup` | `true` for the first deploy only (inserts missing plans, needs migrations applied), then `false` |
| `Proxy__TrustForwardedHeaders` | `true` (Render's load balancer is in front of the service) |
| `App__PublicBaseUrl`, `App__WebBaseUrl` | the HTTPS origin of the PWA/web app (set once it exists; ownership-transfer links need it) |
| `Pdf__QuestPdfLicense` | `community` only if the QuestPDF terms apply to you; otherwise PDF endpoints answer 503 |
| `Email__Provider` | `log` until a real provider is set (`Email__FromAddress`, `Email__Resend__ApiKey`) |
| `Auth__JwtSigningKey` | optional, only for bearer-token clients such as the Expo reference app; `openssl rand -base64 48`. The PWA uses cookies and does not need it |
| `Storage__LocalRoot` | default local disk is lost on every redeploy on Render; use S3-compatible storage before real uploads matter |

**4. After the first deploy.** Open `<service-url>/health`; read the log for the applied migrations (all migrations, including `AddDataProtectionKeys`); then set `Billing__SeedPlansOnStartup` to `false`. A new database means new Data Protection keys, so old sessions and preview links stop working.

**5. Point the clients at it.** Set `API_URL` on `backend-node` (and on `web/` if it is redeployed) to the new service URL. The Expo reference app defaults to `https://avzar-api.onrender.com` (`EXPO_PUBLIC_API_URL`); that only works if Render gives the recreated service that same URL.
