# iedora backend

.NET 10 backend for [iedora](https://github.com/iedora), built on [.NET Aspire](https://learn.microsoft.com/dotnet/aspire/) with vertical-slice architecture and first-class OpenTelemetry.

## Services

| Project | Purpose |
|---|---|
| `src/Iedora.Auth` | Auth service — ASP.NET Core Identity over EF Core/Postgres, ES256 (ECDSA P-256) JWTs, JWKS, minimal-API vertical slices (`Features/*`). |
| `Iedora.ServiceDefaults` | Shared Aspire defaults — OpenTelemetry (traces/metrics/logs), health checks, HTTP resilience, service discovery. |
| `Iedora.AppHost` | Aspire orchestration — spins up Postgres + the auth service for local dev and wires telemetry to the OTLP collector. |

## Prerequisites

- .NET SDK 10.0.x (see [`global.json`](global.json))
- Docker (for the Postgres container the AppHost starts)

## Run

```bash
# Full Aspire orchestration (Postgres + auth service)
dotnet run --project Iedora.AppHost

# …or just the auth service against your own Postgres:
ConnectionStrings__authdb='Host=localhost;Port=5432;Database=authdb;Username=postgres;Password=…' \
  dotnet run --project src/Iedora.Auth
```

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
