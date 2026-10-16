# Playwright storefront lifecycle tests

The E2E suite in `web/e2e/storefront-lifecycle.spec.ts` exercises a real authenticated session and the storefront API to create a disposable storefront, edit and save its draft, publish two versions, and restore the earlier version.

## Safety requirements

- Run against a **staging environment or localhost only**. The test creates a storefront and publishes test versions.
- Set `PLAYWRIGHT_ALLOW_MUTATIONS=true` only when the target is a disposable staging business.
- Set `PLAYWRIGHT_BASE_URL`, `PLAYWRIGHT_EMAIL`, and `PLAYWRIGHT_PASSWORD`. For users who belong to multiple businesses, set `PLAYWRIGHT_BUSINESS_ID`.
- Do not use production credentials or point the suite at the production site.

## Run locally

```bash
cd web
npm ci
npx playwright install chromium
PLAYWRIGHT_BASE_URL=https://your-avzar-staging-host.example \
PLAYWRIGHT_EMAIL=staging-owner@example.com \
PLAYWRIGHT_PASSWORD='use-a-staging-only-password' \
PLAYWRIGHT_ALLOW_MUTATIONS=true npm run test:e2e
```

The test is intentionally skipped unless credentials and the mutation opt-in are supplied. CI runs it only when the corresponding GitHub Actions variables/secrets are configured. The Playwright browser itself must be installed on the runner. A skipped test is not evidence of a passing E2E test.
