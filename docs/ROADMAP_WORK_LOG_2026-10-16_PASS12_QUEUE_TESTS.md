# Roadmap work log — Pass 12 Redis queue tests

## Change

Added `RedisJobQueueTests` using Testcontainers Redis 7. The tests target the real Redis Lua enqueue script and Redis Streams, covering unkeyed jobs, repeated idempotency keys, distinct keys, and validation before writes. Added `Testcontainers.Redis` 4.8.1 to central package management and the integration-test project.

## Verification status

- Source and project XML were reviewed locally.
- Not run: .NET restore/build/tests and container startup. The current environment has no .NET SDK or Docker.
- Required next check: CI must restore and compile the package, start Redis, and execute these tests. A source-only change is not proof of runtime behavior.
