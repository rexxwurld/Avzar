# Avzar Mobile (Expo)

This is the first React Native/Expo implementation pass for Avzar. It is kept as a reference only: the product direction is the vanilla-JavaScript PWA in `pwa/`, and the MAUI project it was written beside has been removed. It uses the API's bearer-token flow.

## Included in this pass

- Expo Router application structure for Android and iOS.
- SecureStore-backed bearer token storage.
- Sign-in using the existing `POST /api/auth/token` contract, including a business-selection response.
- Refresh-token use and local sign-out/revocation.
- Dashboard shell.
- Customer search/list, customer creation, customer details, and a measurement profile/history view using the current API routes.
- Assigned task list read from `/api/my/tasks`.
- Actual API error messages shown to the user instead of a generic success/failure placeholder.

## Not yet complete

- Creating/editing measurement sets and measurement revisions.
- Orders, invoices, payments, staff, schedule editing, notifications, subscriptions, AI studio and full storefront administration.
- SQLite-backed offline records and durable sync/conflict resolution. The removed MAUI app's offline task queue was never replaced here.
- Node.js API. This first mobile pass deliberately uses the existing ASP.NET API to avoid mixing a backend rewrite with the first mobile UI implementation.
- End-to-end tests and Android/iOS builds. They have not been run in the authoring environment.

## Configure and run

Use Node.js 22.13 or newer. Install dependencies, then align Expo native packages to the SDK before building:

```bash
npm install
npx expo install --fix
npx expo-doctor
npm run typecheck
npx expo start
```

Set `EXPO_PUBLIC_API_URL` in the local Expo environment to the API base URL if it differs from `https://avzar-api.onrender.com`. This value is public client configuration, not a secret. Never put signing keys or provider secrets in Expo variables prefixed with `EXPO_PUBLIC_`.

Mobile sign-in requires the server's mobile bearer authentication to be enabled, including a valid server-side JWT signing key. A 503 `mobile_auth_not_configured` response means deployment configuration must be corrected on the server; do not disable authentication to work around it.

## API compatibility note

This app is currently an API client for the existing ASP.NET API. Do not change production traffic to a new Node.js service until the Node implementation passes route/DTO, authorization, tenant-isolation, migration, and regression tests. The target Node API should retain the same mobile contract during the transition where practical.
