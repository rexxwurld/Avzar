# Avzar: first run, test and credentials

Status (2026-10-16): CI run #207 (commit `3714953`) passed all three jobs: the backend suite reports 680/680 tests passed with a clean model-drift gate, and the `migrations` job applied the eight committed migrations to a clean PostgreSQL 17.11 (67 tables, 33 check constraints, 113 foreign keys, 8 partial indexes). Not rerun in this workspace; see `PROGRESS.md` for evidence and limits. Not a deployment verification (compose and full-stack smoke test are still unrun).

## 1. Prerequisites
- .NET 10 SDK (`backend/global.json` pins 10.0.100) and `dotnet tool install -g dotnet-ef`
- Docker (PostgreSQL for the app, and Testcontainers for the integration tests)

## 2. First build (do these in order)
```bash
cp .env.example .env            # set POSTGRES_PASSWORD
./scripts/bootstrap-backend.sh  # creates the solution, restores, builds, generates ONE migration "InitialSchema"
```
If it fails to build, resolve the actual compiler/restore error before proceeding. ImageSharp is pinned to 4.1.2 and requires a valid Six Labors license for Release builds; see section 8.

**Migration safety:** inspect `backend/src/Tailoring.Infrastructure/Persistence/Migrations` and the target database before generating or replacing migrations. Never delete migration files or reset a database just to make a build pass. Regenerating a single initial migration is appropriate only for a confirmed unreleased schema with no deployed databases and after preserving the existing migration history. For an existing/deployed database, add a forward migration and test it against a disposable PostgreSQL database first.

## 3. Tests
```bash
cd backend && dotnet test       # needs Docker running
```
The supplied `backend-test-results.zip` TRX summary reports 478 tests total, 478 passed, 0 failed, and 0 errors. Independently rerun `dotnet test -c Release` with Docker available; the integration suite uses Testcontainers/PostgreSQL. Do not rely on the supplied artifact as proof of the current checkout's result.

## 4. Run
```bash
docker compose up -d postgres
cd backend && dotnet ef database update --project src/Tailoring.Infrastructure --startup-project src/Tailoring.Api
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Tailoring.Api   # seeds the 4 plans automatically in Development
```
Swagger is on in Development. In Production set `Billing__SeedPlansOnStartup=true` once, AFTER migrations.

## 5. What needs credentials, and when
| Needed for | What | Phase | Today |
|---|---|---|---|
| Database | PostgreSQL connection string (`ConnectionStrings__Default`) | now | required |
| Email (invitations, reset, receipts) | SMTP relay (587/STARTTLS) or Resend API key | A | Code written, UNVERIFIED and untested with real credentials: **blocked by external configuration**. Default `Email__Provider=log` only writes to the log |
| Media in production | S3-compatible bucket (access key, secret, bucket, endpoint) | 2 | Set `Storage__Provider=s3` plus `Storage__S3__*` (bucket, access key, secret, region, optional service URL for R2/MinIO). Default is local disk (`Storage__LocalRoot`). Not yet tested against a real bucket |
| Rate limits across replicas (Redis) | Redis connection string (`Redis__ConnectionString`) | A | Optional. When set, the `auth`, `upload`, `public-submit` and `public` limits are counted in Redis so they hold across API replicas; an outage falls back to per-process limits and `/health` reports Degraded. Unit-tested with a fake counter only: **not tested against a real Redis**. Not yet used for caching, queues or SignalR (Step D) |
| Subscription billing in naira | Paystack or Flutterwave secret key + webhook secret | 9 | not built |
| Push notifications | Firebase (FCM) service account + Apple APNs key | 5/8 | not built |
| AI Fashion Studio / Assistants | OpenAI API key (server-side only) | 6-7 | not built |
| Mobile app stores | Apple Developer + Google Play accounts, signing keys | 8 | not built |
| Public sites + custom domains | Wildcard DNS, wildcard TLS cert, optional Caddy on-demand TLS | 3 | code written, needs your DNS/proxy |
| Spam protection (optional) | Cloudflare Turnstile site key + secret | 3 | code written, untested |

Never put any of these in source control. Use environment variables or a secret manager.

## 5b. Public sites (storefronts)
- DNS: point `*.yourdomain` (wildcard) and the apex at the web container's host. Set `BASE_DOMAIN` (web) and `Storefront__BaseDomain` (API) to the same value.
- TLS for `*.yourdomain`: use a wildcard certificate (Let's Encrypt DNS-01) at your proxy.
- Custom domains: a verified domain needs a certificate on demand. With Caddy:
  ```
  { on_demand_tls { ask http://api:8080/public/domains/allowed } }
  https:// { tls { on_demand } reverse_proxy web:3000 }
  ```
  The `allowed` endpoint answers 200 only for VERIFIED domains on plans that include custom domains, so certificates are never issued for strangers.
  Custom domains are OFF in every plan by default. Turn on `storefront.custom_domain` per plan (or per tenant override) only after you have tested this.
- The API must be reachable ONLY through the web container (it trusts X-Forwarded-For when `Proxy__TrustForwardedHeaders=true`). Do not publish port 8080.
- ASP.NET Data Protection keys are stored in PostgreSQL (`DataProtectionKeys` table, migration `AddDataProtectionKeys`), so they survive restarts and are shared by all instances. They are not encrypted at rest.
- Optional: Cloudflare Turnstile. Site key (`TURNSTILE_SITE_KEY`, public) + secret (`Storefront__CaptchaSecretKey`, server only).
- Web: `cd web && npm ci && npm run typecheck && npm test`.

## 6. Known gaps in what exists now (be aware when testing)
- The current web tree contains the PUBLIC site renderer and its supporting components. The business dashboard and visual builder/editor UI are not implemented yet (use Swagger to drive the existing builder API).
- No malware scanner on uploads (images are re-encoded, which disarms image payloads; other file types are refused).
- With `Email__Provider=log` (default) emails are not delivered; the full message, links included, appears in the application log. Development only.
- Existing businesses created before a permission was added will not have it on non-owner roles until edited.


## 6. Email configuration (Step A)
Mail is queued in the `EmailOutbox` table and delivered by a background worker in the API process, with retries (30s, 2m, 8m, ~32m, ~2h, then 6h cap) and a dead-letter state after `Email__MaxAttempts` (default 6). A provider outage never fails the user's request.

| Setting (env var) | Meaning |
|---|---|
| `Email__Provider` | `log` (default, dev only), `smtp` or `resend` |
| `Email__FromAddress`, `Email__FromName` | Sender. Required for smtp/resend. Use a domain you have verified with the provider (SPF/DKIM) |
| `Email__Smtp__Host`, `__Port` (587), `__UseStartTls` (true), `__Username`, `__Password` | SMTP relay. Implicit TLS (465) is not supported by this transport |
| `Email__Resend__ApiKey`, `Email__Resend__BaseUrl` | Resend API |
| `Email__Resend__WebhookSecret` | Resend webhook signing secret (`whsec_...`). Empty = the webhook endpoint is off (404). The app fails at startup if it is set but malformed |
| `Email__DispatcherEnabled` | Set `false` on hosts that must not deliver |
| `Email__MaxAttempts`, `__BatchSize`, `__PollSeconds`, `__LeaseMinutes` | Tuning |

Notes: "Sent" means the provider accepted the message, not that it reached an inbox. Delivery reports (delivered, delayed, bounced, complained) are recorded in `EmailOutbox.DeliveryStatus` when the Resend webhook is configured (see section below); a permanent bounce or a spam complaint adds the recipient to `EmailSuppressions` and the dispatcher then refuses to send to that address (message goes to Failed, body purged); transient and undetermined bounces never suppress. Bodies are erased from the table once a message is Sent or Failed because they can contain one-time links. A timeout can leave delivery ambiguous; Resend is protected by an idempotency key, SMTP is not, so a rare duplicate is possible. Several API replicas can run the worker safely (rows are claimed with `FOR UPDATE SKIP LOCKED`).


## 7. Account security and mobile sign-in (Step A, unverified)
| Setting (env var) | Meaning |
|---|---|
| `Auth__JwtSigningKey` | 32+ random characters. Mobile sign-in stays OFF (503 `mobile_auth_not_configured`) until set. Generate with `openssl rand -base64 48`. Never commit it. Changing it signs every device out of its access token (refresh tokens still work and get new access tokens) |
| `Auth__JwtIssuer`, `Auth__JwtAudience` | Defaults `avzar-api` / `avzar-mobile` |
| `Auth__AccessTokenMinutes` (10), `Auth__RefreshIdleDays` (30), `Auth__RefreshAbsoluteDays` (90), `Auth__MaxSessionsPerUser` (10) | Token lifetimes and device cap |
| `Auth__RequireVerifiedEmail` | `true` blocks password sign-in until the address is confirmed. Needs a working email provider first |
| `Auth__EmailVerificationHours` (24), `Auth__PasswordResetMinutes` (30), `Auth__EmailThrottleSeconds` (120) | Link lifetimes and the minimum gap between emails to one address |
| `Auth__ProductName` | Name shown in authenticator apps |

Web pages the frontend must provide (links in the emails point at `App__WebBaseUrl`): `/verify-email?userId=&token=` (POST the two values to `/api/auth/confirm-email`) and `/reset-password?userId=&token=` (POST them with the new password to `/api/auth/reset-password`). Neither page exists yet in `web/`.

Data Protection keys are persisted in the database (`DataProtectionKeys`): email and reset tokens, login cookies and preview links depend on them, so never truncate that table in production.

Mobile flow: `POST /api/auth/token` -> `{accessToken, refreshToken, ...}`; send `Authorization: Bearer <accessToken>`; when it expires `POST /api/auth/token/refresh` with the refresh token and store the NEW refresh token it returns (the old one stops working; using it again ends the session). On logout call `/api/auth/token/revoke`.

## 8. Six Labors license for ImageSharp 4.x (build-time)

ImageSharp 4.x (the first line patched for the October 2026 advisories) refuses to build in Release without a license.
Free for for-profit companies under USD 1M annual revenue: apply at https://licensing.sixlabors.com/ (you receive a `sixlabors.lic` file; licenses are time-limited, so renew before expiry).

- **Local:** put `sixlabors.lic` in `backend/src/Tailoring.Infrastructure/` (git-ignored), or set `SIXLABORS_LICENSE_KEY` to the FULL file contents.
- **CI:** add a repository secret named `SIXLABORS_LICENSE_KEY` containing the full file contents. `ci.yml` exports it; the Infrastructure csproj reads it.
- **Docker/other builds:** pass the same env var (or `-p:SixLaborsLicenseKey=...`) to `dotnet build/publish`. Do not bake the key into an image layer; use a BuildKit secret.
- Only `Tailoring.Infrastructure` references ImageSharp directly, so only it needs the key.


## 9. PDF documents, CSV import/export, search (Step A, last slice)
**PDFs** (`GET /api/invoices/{id}/pdf`, `/api/invoices/{id}/credit-notes/{creditNoteId}/pdf`, `/api/receipts/{id}/pdf`, `/api/orders/{id}/quotation-pdf`, `/api/customers/{customerId}/measurement-sets/{setId}/pdf`, `/api/tasks/{id}/job-sheet-pdf`, `/api/my/tasks/{id}/job-sheet-pdf`) are drawn on demand from live records with QuestPDF and are never stored.
- **You must choose a QuestPDF license**: set `Pdf__QuestPdfLicense` to `community`, `professional` or `enterprise`. There is deliberately no default; until it is set every PDF endpoint answers 503 `pdf_license_not_configured`. Community is free only below USD 1M annual revenue; check the current terms at questpdf.com before choosing.
- On Linux containers QuestPDF needs the font/graphics libraries its docs list (for example `libfontconfig1`); add them to the API image if rendering fails.
- Branding is the business name and its first active location's address. Logos and brand colours are not drawn yet.
- Emailing or sharing a PDF by link needs a public, expiring, token-guarded route; it is deferred to the customer portal (Step B) so it is built once. Staff can download and send the file themselves.

**Customer CSV**: `GET /api/customers/export`; `POST /api/customers/import/preview` (file) changes nothing and returns counts, problems and a `fileHash`; `POST /api/customers/import/commit` (file, `expectedHash`, `onDuplicate=skip|update`, `confirmUpdateCount`, `skipInvalidRows`). Imports create only by default. Overwriting existing customers needs `confirmUpdateCount` equal to the number the preview reported. Limits: 5 MB, 2000 rows, UTF-8 only. Exports defuse spreadsheet formulas (a leading apostrophe) and the importer removes it again.

**Search**: `GET /api/search?q=` (2+ characters) returns up to 5 hits each of customers, orders, invoices, designs and inquiries, limited to what the caller's role may view. It uses `ILIKE` without a trigram index; add one (pg_trgm) if tables grow large.

## 10. Verification evidence and known environment blockers (2026-10-15)
- The supplied `backend-test-results.zip` TRX summary reports 478 tests: 478 passed, 0 failed, 0 errors. Its originating workflow run and commit are not identified in the artifact itself.
- The supplied `generated-migration.zip` contains migration `20261015172445_CiCheck`; static inspection found 181 index creations, filtered indexes, and database check constraints. The migration has not been applied to a PostgreSQL database as part of this review.
- This review workspace had Node.js 22 but no .NET SDK, Docker, or PostgreSQL client. `npm install` did not complete within the available execution window. No web build or test pass is claimed here.
- The source archive does not contain `.git`; the attempt to fetch `https://github.com/rexxwurld/Avzar.git` could not resolve `github.com`. Confirm the live `main` branch and commit ancestry in a network-enabled Git environment before applying changes.
- `SIXLABORS_LICENSE_KEY` is consumed by the build and checked by CI. Do not print its value. Its GitHub Actions secret state cannot be established from a source archive; inspect the repository's Actions secrets settings or rely on a specific CI run's explicit license-check step.

## Email delivery webhook (Resend) - code written 2026-10-16, UNVERIFIED until CI runs, never tested with real Resend
1. In Resend, add a webhook pointing at `https://<your-api-host>/api/webhooks/email/resend` and subscribe to `email.delivered`, `email.delivery_delayed`, `email.bounced`, `email.complained`.
2. Copy the signing secret (`whsec_...`) into `Email__Resend__WebhookSecret` (secret store, never source) and restart the API.
3. Requests are accepted only with a valid Svix signature (HMAC-SHA256 over `id.timestamp.body`) and a timestamp within 5 minutes. Bad or missing signature: 401. No secret configured: 404. Any signed event, including unknown messages and untracked event types, gets 200 so Resend stops retrying.
4. Reports only move a message's status up (Delayed < Delivered < Bounced/Complained); duplicates and out-of-order events are harmless. The recipient address is never stored in the report; the bounce reason is capped at 300 characters.
5. Check: send a test email, then `select "ToAddress","Status","DeliveryStatus","DeliveryDetail" from "EmailOutbox" order by "CreatedAtUtc" desc limit 5;`
6. Suppression: the address is copied inside the database from the outbox row, never from the payload. To lift a suppression, delete the row from `EmailSuppressions` (no admin UI yet). Not done: Resend IP allow-listing, other providers' webhooks (SMTP has none).

## Business ownership and the one-owner rule
- The database allows at most one owner per business (partial unique index `UX_Memberships_OneOwner`). An ownership transfer saves the outgoing owner first and the new owner second, inside one transaction.
- If the migration that adds the index fails with a duplicate-key error, some business has two owner memberships: fix that data by hand (set `IsOwner` to false on the wrong one and give them the Manager role), then restart.
- Migration `AddOneOwnerGuardAndLocationsAllBackfill` also gave `locations.all` to the system Manager and Accountant roles of older businesses, except roles whose permissions were ever edited through the API (an audit row `role.permissions_changed` exists). Without that permission a role's holders only see their assigned locations. An owner can add or remove it per role on the roles page.

## 11. Web dependencies and the lockfile (2026-10-16)
- `web/package-lock.json` is committed. CI and `web/Dockerfile` use `npm ci`; do not switch back to `npm install`.
- To change a web dependency: edit `web/package.json`, run the "Generate web lockfile" workflow (Actions tab). It installs the latest npm, deletes and regenerates the lockfile, and commits it to `main`. npm 10.9.x can crash on Vitest 4's optional peer dependencies (`Cannot read properties of null (reading 'edgesOut')`), which is why the workflow uses the latest npm. The committed lockfile installs fine with `npm ci` on npm 10.
- Commits pushed by that workflow do not start CI (default `GITHUB_TOKEN`). Run the CI workflow by hand afterwards and wait for all three jobs.
- CI runs `npm audit --audit-level=high` on the whole dependency tree and fails the `web` job on a high or critical finding. If a manual audit report is needed, run `npm audit` locally in `web/`.
- `web/package.json` has `overrides: { "postcss": ">=8.5.23 <9" }` to lift the postcss bundled with `next` past a high-severity advisory. Remove it only after upgrading to a `next` release that already bundles a patched postcss, and re-run the audit.
