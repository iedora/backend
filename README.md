# iedora backend

.NET 10 backend for [iedora](https://github.com/iedora), built on [.NET Aspire](https://learn.microsoft.com/dotnet/aspire/) with vertical-slice architecture and first-class OpenTelemetry.

## Services

| Project | Purpose |
|---|---|
| `src/Iedora.Auth` | Auth service — ASP.NET Core Identity over EF Core/Postgres, ES256 (ECDSA P-256) JWTs, JWKS, minimal-API vertical slices (`Features/*`). |
| `src/Iedora.Auth.Data` | The EF Core model (Identity + refresh sessions) + migrations, shared by the auth service and the migration worker. |
| `src/Iedora.Outbox` | Reusable transactional-outbox infrastructure (entity, dispatcher, `IOutboxHandler`) — DbContext-agnostic, no broker. Intended for reuse across iedora .NET services. |
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
| `POST` | `/auth/login` | Authenticate → ES256 access token + refresh cookie. |
| `POST` | `/auth/refresh` | Rotate the refresh cookie (reuse detection burns the family). |
| `POST` | `/auth/logout` / `/auth/logout-all` | Revoke this device's session / all of them. |
| `POST` | `/auth/change-password` | Change password (authorized); revokes other sessions. |
| `POST` | `/auth/forgot-password` | Request a reset email (always 200 — no enumeration). |
| `POST` | `/auth/reset-password` | Set a new password from the emailed token; revokes sessions. |
| `GET`  | `/auth/whoami` | Identity from the bearer token (authorized). |
| `GET`  | `/auth/.well-known/jwks.json` | Public keys for offline token verification. |

## OpenAPI contract

The OpenAPI document is the source of truth for the frontend's generated client. It is emitted
at **build time** to [`openapi/Iedora.Auth.json`](openapi/) (no running server or database needed)
and served at `/openapi/v1.json` at runtime.

## Conventions

- **Central Package Management** — all NuGet versions live in [`Directory.Packages.props`](Directory.Packages.props) (with transitive pinning); projects reference packages by name only.
- **Security auditing** — [`Directory.Build.props`](Directory.Build.props) promotes vulnerability advisories `NU1901`–`NU1904` to build errors (net10 audits transitively by default), so a known-vulnerable package fails the build.
- **Result pattern** — expected failures are [`ErrorOr`](https://github.com/amantinband/error-or) values from an [error catalog](src/Iedora.Auth/Common/AuthErrors.cs) (no exceptions/null), mapped to RFC 9457 `ProblemDetails` by [`ProblemResults`](src/Iedora.Auth/Common/ProblemResults.cs) — the error `code` rides in the problem body as a machine-readable discriminator.
- **Transactional outbox** — the reusable [`Iedora.Outbox`](src/Iedora.Outbox) library: `db.EnqueueOutbox(...)` stages a message in the same `SaveChangesAsync` as the request, and a background dispatcher routes it to an [`IOutboxHandler`](src/Iedora.Outbox/IOutboxHandler.cs) with retry/backoff — so a crash after commit can't drop the effect. No message broker. Auth registers it with `AddOutbox<AuthDbContext>()` and a [`PasswordResetEmailHandler`](src/Iedora.Auth/Features/ForgotPassword/PasswordResetEmailHandler.cs) that sends via MailKit SMTP. Configure `Smtp:*` + `PasswordReset:ResetUrlBase` to deliver; single-instance dispatcher (add `FOR UPDATE SKIP LOCKED` for multiple replicas).
