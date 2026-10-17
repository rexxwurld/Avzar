#!/usr/bin/env bash
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
FAILURES=0
BLOCKED=0

run() {
  local label="$1"; shift
  printf '\n== %s ==\n' "$label"
  if "$@"; then
    printf 'PASS: %s\n' "$label"
  else
    local status=$?
    printf 'FAIL (%s): %s\n' "$status" "$label"
    FAILURES=$((FAILURES + 1))
  fi
}

blocked() {
  printf '\nBLOCKED: %s\n' "$1"
  BLOCKED=$((BLOCKED + 1))
}

cd "$ROOT"

if command -v node >/dev/null 2>&1; then
  printf 'Node: '; node --version
  run 'Node API and PWA JavaScript syntax' bash -c '
    set -e
    node --check backend-node/src/config/env.js
    node --check backend-node/src/http/proxy.js
    node --check backend-node/src/http/readiness.js
    node --check backend-node/src/http/app.js
    node --check backend-node/src/server.js
    node --check pwa/app.js
    node --check pwa/sw.js
  '
  if [[ -f backend-node/node_modules/express/index.js ]]; then
    run 'Node API tests' npm --prefix backend-node test
  else
    blocked 'Node API tests need installed backend-node dependencies; run npm ci --prefix backend-node.'
  fi

  if [[ -d web/node_modules ]]; then
    run 'Web typecheck' npm --prefix web run typecheck
    run 'Web unit tests' npm --prefix web test
    run 'Web production build' npm --prefix web run build
  else
    blocked 'Web typecheck, unit tests, and production build need dependencies; run npm ci --prefix web.'
  fi
else
  blocked 'Node.js is not installed.'
fi

if command -v dotnet >/dev/null 2>&1; then
  run '.NET API Release build' dotnet build "$ROOT/backend/src/Tailoring.Api/Tailoring.Api.csproj" -c Release
  run '.NET integration tests' dotnet test "$ROOT/backend/tests/Tailoring.IntegrationTests/Tailoring.IntegrationTests.csproj" -c Release
else
  blocked '.NET 10 SDK is not installed; backend compilation and tests must run in CI or on a machine with the SDK.'
fi

printf '\nSummary: %s failed check(s), %s blocked check group(s).\n' "$FAILURES" "$BLOCKED"
if (( FAILURES > 0 )); then exit 1; fi
if (( BLOCKED > 0 )); then exit 2; fi
