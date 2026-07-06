# iedora backend

.NET 10 backend for [iedora](https://github.com/iedora), built on [.NET Aspire](https://learn.microsoft.com/dotnet/aspire/) with vertical-slice architecture and first-class OpenTelemetry.

## Services

| Project | Purpose |
|---|---|
| `src/Iedora.Auth` | Auth service — ASP.NET Core Identity over EF Core/Postgres, ES256 (ECDSA P-256) JWTs, JWKS, minimal-API vertical slices (`Features/*`). |
| `src/Iedora.Auth.Data` | The EF Core model (Identity + refresh sessions) + migrations, shared by the auth service and the migration worker. |
| `framework/Outbox/Framework.Outbox` | Reusable transactional-outbox library (entity, dispatcher, `IOutboxHandler`) — reliable message *production*. `FOR UPDATE SKIP LOCKED` claim (multi-replica-safe), retry/backoff, no broker. |
| `framework/Inbox/Framework.Inbox` | Reusable idempotent-consumer library (`InboxProcessor`, `IInboxHandler`) — reliable message *consumption*: dedup + handler in one transaction, so at-least-once redelivery is safe. Transport-agnostic. |
| `framework/Web/Framework.Web` | Reusable ASP.NET Core minimal-API helpers: `ProblemResults` maps `ErrorOr` results → RFC 9457 `ProblemDetails`; `RequestMeta` reads client user-agent/IP from `HttpContext`. |
| `src/Iedora.MigrationService` | Worker that applies EF migrations on startup then exits; the AppHost gates the auth service on its completion (`WaitForCompletion`). |
| `src/Iedora.Auth.Messaging` | Non-web module: auth's outbox handlers + email sender + a self-contained `AddAuthOutboxDispatch()` registration — so the worker composes auth's dispatch without referencing the auth *web* project. |
| `src/Iedora.Worker` | The single, generic app-wide background worker. Composes each service's `*.Messaging` module and runs its outbox dispatcher (one per `DbContext`); scales to N replicas via `FOR UPDATE SKIP LOCKED`. |
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

- **Project layout** — no loose `.cs` at a project root (only `Program.cs` on host projects). Types live in role folders: `Abstractions/` (interfaces + options), `Persistence/` (EF entities, `DbContext`, model mapping), `Processing/` (processors + hosted services), `Extensions/` (DI / `ModelBuilder` / usage extension methods) — plus concern folders where a lib doesn't fit that shape (e.g. `Framework.Web` uses `Results/` + `Http/`, `Iedora.Auth.Messaging` uses `Email/` + `Handlers/`). Namespaces stay flat per project (`Framework.Outbox`, not `Framework.Outbox.Abstractions`) so consumers keep a single `using`.
- **Central Package Management** — all NuGet versions live in [`Directory.Packages.props`](Directory.Packages.props) (with transitive pinning); projects reference packages by name only.
- **Security auditing** — [`Directory.Build.props`](Directory.Build.props) promotes vulnerability advisories `NU1901`–`NU1904` to build errors (net10 audits transitively by default), so a known-vulnerable package fails the build.
- **Result pattern** — expected failures are [`ErrorOr`](https://github.com/amantinband/error-or) values from an [error catalog](src/Iedora.Auth/Common/AuthErrors.cs) (no exceptions/null), mapped to RFC 9457 `ProblemDetails` by the reusable [`Framework.Web.ProblemResults`](framework/Web/Framework.Web/Results/ProblemResults.cs) — the error `code` rides in the problem body as a machine-readable discriminator.
- **Async / reliable messaging (Postgres-native, no broker)** — the [`Framework.Outbox`](framework/Outbox/Framework.Outbox) + [`Framework.Inbox`](framework/Inbox/Framework.Inbox) libraries (each with its own Testcontainers-Postgres test suite):
  - **Outbox** (production) — `db.EnqueueOutbox(...)` stages a message in the same `SaveChangesAsync` as the request; the single generic [`Iedora.Worker`](src/Iedora.Worker) runs a per-`DbContext` dispatcher (`OutboxProcessor<TContext>`) that claims batches with `FOR UPDATE SKIP LOCKED` (safe across N replicas) and routes each to an [`IOutboxHandler`](framework/Outbox/Framework.Outbox/Abstractions/IOutboxHandler.cs) with retry/backoff — so a crash after commit can't drop the effect. The API only enqueues; SMTP never touches the request path. Each service contributes a thin non-web `*.Messaging` module (`Add<Service>OutboxDispatch()`) the worker composes — it never references a service's web project. Configure `Smtp:*` + `PasswordReset:ResetUrlBase`.
  - **Inbox** (consumption) — `InboxProcessor.ProcessOnceAsync(...)` dedups a received message (via its id) and runs the [`IInboxHandler`](framework/Inbox/Framework.Inbox/Abstractions/IInboxHandler.cs) in one transaction, so at-least-once redelivery is safe. Transport-agnostic; the foundation for background command processing (accept → 202 → worker → result) as product services land. No consumer wired yet.
