# Avzar .NET MAUI mobile starter

Targets Android and iOS on .NET 10. It uses the existing bearer-token endpoints (`POST /api/auth/token`, `POST /api/auth/token/refresh`, `GET /api/my/tasks`) and stores access/refresh tokens using platform SecureStorage. The first slice supports sign-in, assigned task listing and an offline action queue for start/pause/resume/complete.

## Configure and build

- Install the .NET 10 SDK plus the MAUI workload and Android/iOS platform tooling.
- The API base URL defaults to `https://avzar-api.onrender.com` in `AvzarApi.cs`; change that default for a different deployment. Runtime environment variables are not consistently available to mobile apps.
- Set `Auth__JwtSigningKey` (32+ characters), `Auth__JwtIssuer`, and `Auth__JwtAudience` on the API; mobile tokens are disabled without a signing key.
- Build Android with `dotnet build Avzar.Mobile.csproj -f net10.0-android`. iOS builds require macOS/Xcode.

## Current limits

This is a foundation, not the complete mobile roadmap: automatic retry/re-authentication on 401, offline conflict resolution, measurement/order offline workflows, push notifications and sync telemetry still need implementation and device testing. Assigned task snapshots and queued worker actions are stored in platform SecureStorage. Offline action sync is at-least-once from a local queue; task commands must be refreshed after connectivity returns. Do not rely on offline completion as a final production record until conflict handling is added.
