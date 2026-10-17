# PWA offline behavior and synchronization

**Current status: shell-only offline fallback. Offline business mutations are not implemented.**

## What currently works in source

- `pwa/sw.js` caches the static application shell and icons under a versioned cache (`avzar-shell-v2`).
- On navigation fetch failure, it attempts to return the cached `index.html` shell.
- Requests under `/backend/` and `/api/` are explicitly excluded from service-worker caching.
- Cache activation removes older `avzar-shell-*` versions.

This does not make customer, measurement, order, finance, payment, AI, or staff workflows fully offline. A shell may load without a connection, but live API operations still need the network.

## Safe future design (not implemented)

Before queuing offline writes, specify an allowlist of supported low-risk operations. Use IndexedDB records containing a generated operation ID/idempotency key, entity type, minimal payload, creation time, base server version, retry count, and state (`pending`, `syncing`, `synced`, `failed`, `conflict`). Do not queue payments, authentication, password changes, AI generation, or other live-verification workflows by default.

The server must deduplicate idempotency keys within the correct tenant and operation scope. Client-side IDs are not authorization. Synchronization must re-check the authenticated business membership and permissions. Conflicts should be visible to the user and resolved against current server versions; never silently overwrite newer server data or silently discard a queued action.

On logout, clear tenant-specific IndexedDB records and any user-specific in-memory state. Keep private API responses out of the shared service-worker cache. Add bounded retries with backoff and a user-visible retry action; do not retry permanent validation or authorization errors indefinitely.

## Required verification before claiming offline writes

1. Define supported operations and conflict rules.
2. Test queue persistence across reloads and browser restarts.
3. Disconnect the network, create/update a supported record, reconnect, and verify exactly one server-side change.
4. Simulate duplicate submission, timeout after server commit, expired session, permission revocation, and a conflicting update from another device.
5. Verify tenant A's queued data is never submitted under tenant B's session.
6. Test Android Chrome and iOS Safari/Home Screen behavior on real devices.
