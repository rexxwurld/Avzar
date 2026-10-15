# Tailoring Platform

Multi-tenant tailoring business management + designer storefronts. See `docs/architecture.md` for design and `PROGRESS.md` for exactly what exists and what is verified.

## Run the backend (needs .NET 10 SDK, Docker, `dotnet tool install -g dotnet-ef`)
```bash
cp .env.example .env            # edit the password
docker compose up -d postgres
./scripts/bootstrap-backend.sh  # restore, build, create first migration
cd backend
ConnectionStrings__Default="Host=localhost;Database=tailoring;Username=tailoring;Password=<your password>" \
  ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Tailoring.Api
# Swagger: http://localhost:5000/swagger  (port is printed on startup)
dotnet test                     # tenant-isolation tests (starts its own PostgreSQL container)
```
Production: apply migrations explicitly (`dotnet ef database update` or a migration bundle); the API only auto-migrates in Development.
