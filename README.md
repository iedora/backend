# iedora backend

.NET 10 backend for [iedora](https://github.com/iedora), built on [.NET Aspire](https://learn.microsoft.com/dotnet/aspire/) with vertical-slice architecture and first-class OpenTelemetry.

## Services

| Project | Purpose |
|---|---|
| `src/Iedora.Auth` | Auth service — ASP.NET Core Identity over EF Core/Postgres, ES256 (ECDSA P-256) JWTs, JWKS, minimal-API vertical slices (`Features/*`). |
| `src/Iedora.Auth.Data` | The EF Core model (Identity + refresh sessions) + migrations, shared by the auth service and the migration worker. |
| `src/Iedora.MigrationService` | Worker that applies EF migrations on startup then exits; the AppHost gates the auth service on its completion (`WaitForCompletion`). |
| `Iedora.ServiceDefaults` | Shared Aspire defaults — OpenTelemetry (traces/metrics/logs), health checks, HTTP resilience, service discovery. |
| `Iedora.AppHost` | Aspire orchestration — Postgres + migration worker + the auth service for local dev, wiring telemetry to the OTLP collector. |

## Prerequisites

- .NET SDK 10.0.x (see [`global.json`](global.json))
- Docker (for the Postgres container the AppHost starts)

## Run

```bash
# Full Aspire orchestration (Postgres + migration worker + auth service)
dotnet run --project Iedora.AppHost

# …or run against your own Postgres: apply migrations, then start the service.
# (The service does NOT self-migrate — the migration worker owns the schema.)
export ConnectionStrings__authdb='Host=localhost;Port=5432;Database=authdb;Username=postgres;Password=…'
dotnet run --project src/Iedora.MigrationService   # applies migrations, then exits
dotnet run --project src/Iedora.Auth
```

Migrations live in `src/Iedora.Auth.Data`; add one with
`dotnet ef migrations add <Name> --project src/Iedora.Auth.Data`.

## Auth endpoints

| Method | Route | |
|---|---|---|
| `POST` | `/auth/register` | Create an account. |
| `POST` | `/auth/login` | Authenticate → ES256 access token. |
| `GET`  | `/auth/whoami` | Identity from the bearer token (authorized). |
| `GET`  | `/auth/.well-known/jwks.json` | Public keys for offline token verification. |

## OpenAPI contract

The OpenAPI document is the source of truth for the frontend's generated client. It is emitted
at **build time** to [`openapi/Iedora.Auth.json`](openapi/) (no running server or database needed)
and served at `/openapi/v1.json` at runtime.

## Conventions

- **Central Package Management** — all NuGet versions live in [`Directory.Packages.props`](Directory.Packages.props); projects reference packages by name only.
- **Security auditing** — [`Directory.Build.props`](Directory.Build.props) enables `NuGetAudit` (`all`) and promotes advisories `NU1901`–`NU1904` to build errors, so a known-vulnerable direct or transitive package fails the build.
