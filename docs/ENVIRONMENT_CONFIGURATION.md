# Avzar environment configuration guide

`.env.example` is the canonical template for environment variable names. Do not commit `.env` or paste production secrets into issues, chat, logs, or source control. Local Compose reads `.env`; hosted deployments should set the same keys in the host's environment/secrets UI.

## Deployment profiles

### Local development with Docker Compose

1. Copy `.env.example` to `.env`.
2. Replace `POSTGRES_PASSWORD` with a strong local-only password.
3. Keep `WEB_BASE_URL=http://localhost:3000`, `BASE_DOMAIN=localhost`, and `API_PUBLIC_URL=http://localhost:8080` for the local Compose setup.
4. Optional services can remain disabled/empty while developing; `Email__Provider=log` avoids sending real email.
5. Never use production credentials in a developer `.env` file.

### Hosted API and web app

Set configuration in the hosting dashboard, not in a committed file. At minimum, configure the production database connection and public web URL. Set `Proxy__TrustForwardedHeaders=true` only when the API is reachable exclusively through a trusted TLS-terminating proxy. Configure email and payment provider credentials before enabling those workflows. Apply migrations through the configured deployment process and confirm the deployed health endpoint and logs after release.

## Configuration inventory

| Variable | Purpose | Guidance |
|---|---|---|
| `POSTGRES_USER`, `POSTGRES_PASSWORD` | Local Compose PostgreSQL credentials | Required for local Compose; use a unique strong password. |
| `WEB_BASE_URL`, `BASE_DOMAIN`, `API_PUBLIC_URL` | Local URLs and domain defaults | Keep localhost values for local development; use real public domains in hosted deployments where relevant. |
| `ConnectionStrings__Default` | Hosted PostgreSQL connection string | Required for hosted API. Store as a secret; URL-encode special characters in connection-string values as required by Npgsql. |
| `App__WebBaseUrl` | Base URL used for email links | Set to the public HTTPS web-app origin. |
| `Database__MigrateOnStartup` | Apply EF migrations when API starts | Enable only if startup migrations are the chosen deployment strategy. Review migration logs after deploy. |
| `Billing__SeedPlansOnStartup` | Seed missing subscription plans | Keep false in production except for an intentional seed run. Development seeds are enabled by code. |
| `Proxy__TrustForwardedHeaders` | Trust forwarded scheme/client IP headers | Only true behind a trusted proxy when the API cannot be reached directly. |
| `Auth__JwtSigningKey` | Enables mobile bearer sign-in | Optional; keep empty to disable. If enabled, use at least 32 random characters and rotate only with a planned token impact. |
| `Auth__RequireVerifiedEmail` | Require verified email for the relevant auth flow | Set according to the production signup policy and test email delivery first. |
| `Email__Provider` | Email transport (`log`, `resend`, or `smtp`) | `log` is development-only; select and configure a real provider for production notifications. |
| `Email__FromAddress`, `Email__FromName` | Sender identity | Use a verified sender/domain for the chosen provider. |
| `Email__Resend__ApiKey` | Resend API credential | Secret; required when Resend is selected. |
| `Email__Resend__WebhookSecret` | Verify Resend webhook events | Secret; configure to match the provider webhook signing secret. |
| `Email__Smtp__Host`, `Email__Smtp__Port`, `Email__Smtp__UseStartTls`, `Email__Smtp__Username`, `Email__Smtp__Password` | SMTP transport | Configure when SMTP is selected; keep the password secret. |
| `Redis__ConnectionString` | Shared Redis counters/cache and optional coordination | Optional for local/single-process use. When set, it enables shared rate-limit counters and the SignalR Redis backplane so hub broadcasts reach clients connected to other API replicas. It does not by itself enable Redis job queues. |
| `Storage__Provider` | Media storage provider selection | `local` is suitable for local development. Configure the provider's required settings before selecting cloud storage. |
| `Storefront__BaseDomain` | Custom storefront domain suffix/base | Set to the actual domain used for tenant storefronts in production. |
| `Storefront__CaptchaSecretKey` | Server-side Turnstile verification | Optional; keep secret. Set only when the frontend presents the matching Turnstile widget/site key. |
| `TURNSTILE_SITE_KEY` | Public Turnstile widget key | Optional public value; must match the secret configured on the API. |
| `Pdf__QuestPdfLicense` | QuestPDF license configuration | Set if required by the license terms for the deployment's use case. |
| `AI__OpenAI__ApiKey` | OpenAI API access | Optional; secret. AI features that require a provider won't work until configured and budget/rate limits are reviewed. |
| `AI__OpenAI__TextModel`, `AI__OpenAI__ImageModel` | Model identifiers | Defaults are examples; use model names supported by the configured account and feature implementation. |
| `Billing__Paystack__SecretKey` | Paystack checkout and webhook verification | Secret. Use Paystack test credentials for end-to-end test mode; never use a live key in testing. |
| `Billing__Paystack__CallbackUrl` | Paystack checkout return URL | Set to the real public billing callback route/origin for the deployed web app. |
| `PlatformAdmin__Emails__0` (and further indexed entries) | Platform-admin allowlist | Configure only explicitly trusted staff emails. Do not grant platform access through ordinary business-owner roles. |
| `Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command` | Reduce SQL log noise | Optional operational setting; `Warning` is a reasonable hosted default. |

## Important gaps to close before production

- `.env.example` currently does **not** define a complete, provider-specific S3 configuration contract or ClamAV connection contract. Confirm the storage and upload-scanning implementations before enabling those integrations; document every required endpoint, bucket, region, credential and timeout in this file once verified from code.
- Redis configuration currently does not prove that the SignalR backplane or a durable job queue is wired. Treat those as separate implementation/verification tasks.
- Real provider integrations (email, Paystack, AI, object storage, CAPTCHA and DNS/custom domains) need test-mode or staging verification. A variable being present is not evidence that the service works.
- `ConnectionStrings__Default`, all API keys, SMTP credentials, webhook secrets, JWT signing keys and storage credentials must be kept out of source control and redacted from logs.

## Deployment checklist

- [ ] Required hosted variables set in the platform's secret/configuration UI.
- [ ] Public URLs and allowed domains checked for the actual deployment.
- [ ] Database backup and migration strategy confirmed before schema changes.
- [ ] Health check succeeds and migration logs show the expected migration set.
- [ ] Email test message and webhook signature verification tested in staging.
- [ ] Paystack test-mode checkout and webhook idempotency tested before live mode.
- [ ] Upload storage and malware scanning tested with safe fixtures in staging.
- [ ] AI usage limits and billing alerts configured before enabling customer-facing generation.

### Ownership-transfer confirmation

Set `App__PublicBaseUrl` to the public HTTPS origin of the Avzar web application (for example, `https://app.example.com`). The ownership-transfer API refuses to create a confirmation link if the origin is absent or not HTTPS. `App__WebBaseUrl` is retained as a backward-compatible fallback. The confirmation token is single-use, stored only as a SHA-256 hash, expires after 30 minutes, and is bound to the current owner and business. The actual transfer happens only after the signed-in owner confirms the emailed link.
