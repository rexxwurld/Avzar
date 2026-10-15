#!/usr/bin/env bash
# One-time setup. Needs the .NET 10 SDK and `dotnet tool install -g dotnet-ef`.
set -euo pipefail
cd "$(dirname "$0")/../backend"

[ -f Tailoring.sln ] || dotnet new sln -n Tailoring --format sln
dotnet sln Tailoring.sln add src/*/*.csproj tests/*/*.csproj

dotnet restore
dotnet build --no-restore

# Create the first migration (commit the generated Migrations folder).
dotnet ef migrations add InitialSchema \
  --project src/Tailoring.Infrastructure --startup-project src/Tailoring.Api \
  --output-dir Persistence/Migrations

echo "Next: docker compose up -d postgres ; dotnet run --project src/Tailoring.Api"
echo "Tests (need Docker): dotnet test"
