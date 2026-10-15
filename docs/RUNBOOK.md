# Avzar: first run, test and credentials

Status: Phases 1-2 backend are WRITTEN but have never been compiled or run by the author. Expect a first
round of compile errors. Fix them before building anything else on top. Paste the errors back and they get fixed first.

## 1. Prerequisites
- .NET 10 SDK (`backend/global.json` pins 10.0.100) and `dotnet tool install -g dotnet-ef`
- Docker (PostgreSQL for the app, and Testcontainers for the integration tests)

## 2. First build (do these in order)
```bash
cp .env.example .env            # set POSTGRES_PASSWORD
./scripts/bootstrap-backend.sh  # creates the solution, restores, builds, generates ONE migration "InitialSchema"
```
If it fails to build: copy the compiler errors back. If package restore fails on `SixLabors.ImageSharp 3.1.7`, bump
it in `backend/Directory.Packages.props` to the latest 3.1.x.

Delete any old `Persistence/Migrations` folder from earlier attempts before running the script: the schema changed in
every step, so one fresh migration is the right thing.

## 3. Tests
```bash
cd backend && dotnet test       # needs Docker running
```
~190 tests: pure rules (pricing, invoices, entitlements, images, slugs) and PostgreSQL integration tests
(tenant isolation, concurrency, money rules). Send back any failing test names with their messages.

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
| Cache / rate limits / queues | Redis connection string | 5+ | not used yet |
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
- Persist ASP.NET Data Protection keys (preview links) when running more than one instance or restarting: configure a key ring location (volume or database) before production.
- Optional: Cloudflare Turnstile. Site key (`TURNSTILE_SITE_KEY`, public) + secret (`Storefront__CaptchaSecretKey`, server only).
- Web: `cd web && npm install && npm run typecheck && npm test`.

## 6. Known gaps in what exists now (be aware when testing)
- The only web UI is the PUBLIC site renderer. There is no dashboard or builder editor UI yet (use Swagger to drive the builder API).
- No PDF generation: invoices/receipts are JSON documents.
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
| `Email__DispatcherEnabled` | Set `false` on hosts that must not deliver |
| `Email__MaxAttempts`, `__BatchSize`, `__PollSeconds`, `__LeaseMinutes` | Tuning |

Notes: "Sent" means the provider accepted the message, not that it reached an inbox (bounce/delivery webhooks are not built). Bodies are erased from the table once a message is Sent or Failed because they can contain one-time links. A timeout can leave delivery ambiguous; Resend is protected by an idempotency key, SMTP is not, so a rare duplicate is possible. Several API replicas can run the worker safely (rows are claimed with `FOR UPDATE SKIP LOCKED`).


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

Data Protection keys must be persisted in production: email and reset tokens are signed with them, so losing the key ring invalidates every outstanding link.

Mobile flow: `POST /api/auth/token` -> `{accessToken, refreshToken, ...}`; send `Authorization: Bearer <accessToken>`; when it expires `POST /api/auth/token/refresh` with the refresh token and store the NEW refresh token it returns (the old one stops working; using it again ends the session). On logout call `/api/auth/token/revoke`.

## 8. Six Labors license for ImageSharp 4.x (build-time)

ImageSharp 4.x (the first line patched for the October 2026 advisories) refuses to build in Release without a license.
Free for for-profit companies under USD 1M annual revenue: apply at https://licensing.sixlabors.com/ (you receive a `sixlabors.lic` file; licenses are time-limited, so renew before expiry).

- **Local:** put `sixlabors.lic` in `backend/src/Tailoring.Infrastructure/` (git-ignored), or set `SIXLABORS_LICENSE_KEY` to the FULL file contents.
- **CI:** add a repository secret named `SIXLABORS_LICENSE_KEY` containing the full file contents. `ci.yml` exports it; the Infrastructure csproj reads it.
- **Docker/other builds:** pass the same env var (or `-p:SixLaborsLicenseKey=...`) to `dotnet build/publish`. Do not bake the key into an image layer; use a BuildKit secret.
- Only `Tailoring.Infrastructure` references ImageSharp directly, so only it needs the key.
