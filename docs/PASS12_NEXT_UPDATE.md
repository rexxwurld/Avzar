# Pass 12 next update — Redis enqueue idempotency

## Change

Fixed a Redis Lua enqueue bug in `RedisJobQueue`: previously the script read the empty idempotency-key slot even when the caller did not supply an idempotency key. The first ordinary job could therefore cause later ordinary jobs to return the first job ID without being appended to the stream. The script now reads and writes the idempotency key only when a non-empty key is provided.

## Verification

- Confirmed the enqueue script guards the `GET` with `ARGV[3] ~= ''` and still applies the 24-hour TTL only to non-empty keys.
- Confirmed the archive can be listed and integrity-tested after packaging.
- Not run: .NET build/tests and Redis integration tests. This environment has no .NET SDK, Docker, or `redis-server`; do not treat this source-level fix as runtime-verified.

## Still outstanding

- Add executable Redis integration tests for repeated keyed and unkeyed enqueues.
- Wire job handlers/producers into actual Avzar workflows and validate retries/dead-letter behavior.
- Complete remaining security/web tests, additional import/export workflows, dependency review, Paystack/email/MAUI/deployment integration tests.

## Next implementation — real Redis queue integration tests

Added `RedisJobQueueTests`, backed by a disposable Redis 7 container through Testcontainers. The tests exercise the actual Lua script and Redis Stream rather than mocking them:

- two jobs without idempotency keys receive different IDs and both appear in the stream;
- a repeated idempotency key returns the original ID and does not append a second entry;
- distinct idempotency keys append distinct jobs;
- malformed job types/payloads are rejected before a stream write.

Added the centrally pinned `Testcontainers.Redis` 4.8.1 dependency to the integration-test project. CI must have Docker available for these tests. The tests have **not** been compiled or run in this environment because the .NET SDK and Docker are unavailable; package restore/API compatibility and runtime behavior still require CI confirmation.

## Remaining queue work after these tests

- Add worker-level tests for successful ACK, transient retry, unknown handler/dead-letter, max-attempt behavior, and abandoned-consumer recovery.
- Wire producers into actual product workflows with stable idempotency keys and handlers that are safe under at-least-once delivery.
- Verify multi-instance SignalR fan-out against Redis and test Redis outage/recovery behavior.
