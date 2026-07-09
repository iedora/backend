# iedora backend

.NET 10 backend for [iedora](https://github.com/iedora) — a **modular monolith** on [.NET Aspire](https://learn.microsoft.com/dotnet/aspire/): one web API (`Iedora.Api`) composed of feature modules, each a vertical slice owning its own Postgres schema, with first-class OpenTelemetry.

## Modules

`Iedora.Api` is a thin web-API host (`Program.cs` + host wiring) that composes the **feature-module
projects** under `src/`. Each module is its own class-library project owning a Postgres **schema** +
its own `DbContext`, self-registers (`Add<Module>Module()` + `Map<Module>Module()`), and never touches
another module's tables — cross-module reads go through a narrow in-process interface (e.g. login →
`ITenancyApi`).

| Module | Schema | Owns |
|---|---|---|
| **Identity** (`src/Iedora.Identity`) | `identity` | Users, roles, refresh sessions, password lifecycle, ES256 JWT/JWKS. |
| **Tenancy** (`src/Iedora.Tenancy`) | `tenancy` | Tenants + memberships; exposes `ITenancyApi` as its cross-module surface. |
| **Menus** (`src/Iedora.Menus`) | `menu` | Restaurants, the menu content tree, the guest render + view analytics, and the cross-tenant staff console (a BFF over Identity + Tenancy). |

Modules never reference each other's internals. A schema-owning module that exposes a cross-module surface ships a sibling **contracts project** (`src/Iedora.<Module>.Contracts`, namespace `Iedora.<Module>.Contracts`) holding its public interfaces + DTOs — everything there is external-use by definition. The rule: a module may import **another module's `.Contracts` project, and nothing else of it**; the implementation stays `internal` in the owning module and is resolved via DI. Both directions use it: Identity's login resolves the default tenant via `Iedora.Tenancy.Contracts.ITenancyApi`; the Menus staff console resolves owner users via `Iedora.Identity.Contracts.IIdentityApi`.

## Projects

| Project | Purpose |
|---|---|
| `src/Iedora.Api` | The web-API host — composes the feature-module projects (`src/Iedora.<Module>`), ES256 JWTs, JWKS. |
| `src/Iedora.<Module>` | Each feature module is its own project (`Iedora.Identity`, `Iedora.Tenancy`, `Iedora.Menus`, …): its vertical slices plus a `Data/` folder holding the EF model, `DbContext`, migrations (`Data/Migrations`), and DB registration (`Add<Module>Db`). Its worker-side dispatch is a self-contained `Add<Module>OutboxDispatch()` / `Add<Module>Messaging()` in the module's `Extensions/`. |
| `src/Iedora.<Module>.Contracts` | A module's public cross-module surface (namespace `Iedora.<Module>.Contracts`): interfaces (`IIdentityApi`, `ITenancyApi`) + DTOs + events. The only part of a module another module may reference. |
| `framework/Outbox/Framework.Outbox` | Reusable transactional-outbox library (entity, dispatcher, `IOutboxHandler`) — reliable message *production*. Dispatcher **woken by Postgres `NOTIFY`** (poll as the fallback), `FOR UPDATE SKIP LOCKED` claim (multi-replica-safe), schema-aware, retry/backoff, no broker. |
| `framework/Inbox/Framework.Inbox` | Reusable idempotent-consumer library (`InboxProcessor`, `IInboxHandler`) — reliable message *consumption*: dedup + handler in one transaction, so at-least-once redelivery is safe. Transport-agnostic. |
| `framework/Commands/Framework.Commands` | The async-write pipeline: `SubmitCommand` stages a `Pending` command + outbox message atomically; a `CommandHandler` runs the work off the outbox and records `Succeeded`/`Failed`, so a `202`-accepted write is pollable to completion. Every domain write uses it. |
| `framework/Web/Framework.Web` | Reusable ASP.NET Core minimal-API helpers: `ProblemResults` maps `ErrorOr` results → RFC 9457 `ProblemDetails`; `RequestMeta` reads client user-agent/IP from `HttpContext`; `Paging` clamps a request's `limit`/`offset` so no list endpoint returns an unbounded page. |
| `framework/Maintenance/Framework.Maintenance` | Reusable retention sweeper: register `IRetentionSweep`s (each an idempotent `WHERE`-bounded delete) and one hosted service prunes them all on a fixed interval, isolating per-sweep failures and counting removed rows via OTel (`Framework.Maintenance` meter). Storage-agnostic, multi-replica-safe. Prunes the menu view-dedup markers and processed outbox/inbox rows. |
| `src/Iedora.MigrationService` | Worker that applies every module's EF migrations on startup then exits; the AppHost gates the API on its completion (`WaitForCompletion`). |
| `src/Iedora.Worker` | The single, generic app-wide background worker. Composes each module's dispatch registration (`Add<Module>OutboxDispatch()` / `Add<Module>Messaging()`, defined in the module — never the API web project) and runs its outbox dispatcher (one per `DbContext`) plus the `Framework.Maintenance` retention sweeper (dedup-marker + processed-row pruning); scales to N replicas via `FOR UPDATE SKIP LOCKED`. |
| `Iedora.ServiceDefaults` | Shared Aspire defaults — OpenTelemetry (traces/metrics/logs), health checks, HTTP resilience, service discovery. |
| `Iedora.AppHost` | Aspire orchestration — Postgres + migration worker + the API + the worker for local dev, wiring telemetry to the OTLP collector. |

## Prerequisites

- .NET SDK 10.0.x (see [`global.json`](global.json))
- Docker (for the Postgres container the AppHost starts)

## Run

```bash
# Full Aspire orchestration (Postgres + migration worker + API + worker)
dotnet run --project Iedora.AppHost

# …or run against your own Postgres: apply migrations, then start the API.
# (The API does NOT self-migrate — the migration worker owns the schema.)
export ConnectionStrings__authdb='Host=localhost;Port=5432;Database=authdb;Username=postgres;Password=…'
dotnet run --project src/Iedora.MigrationService   # applies every module's migrations, then exits
dotnet run --project src/Iedora.Api
```

Migrations live per module under each module project's `Data/Migrations` (e.g. `src/Iedora.Menus/Data/Migrations`);
add one with `dotnet ef migrations add <Name> --project src/Iedora.<Module>/Iedora.<Module>.csproj --context <Module>DbContext --output-dir Data/Migrations`.

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
| `POST` | `/tenancy/tenants` | Create a tenant owned by the caller (authorized). **Async** — `202` + a status URL to poll. |
| `GET`  | `/tenancy/commands/{id}` | Poll an async write's outcome (`Pending`/`Succeeded`/`Failed`). |
| `GET`  | `/tenancy/admin/tenants` | List all tenants with their owner (**admin** role). |
| `GET`  | `/tenancy/admin/tenants/{id}` | Get a tenant with its owner (**admin** role). |
| `POST` | `/tenancy/admin/tenants` | Provision a tenant owned by an existing user (**admin** role). |
| `POST` | `/tenancy/admin/tenants/{id}/transfer` | Transfer a tenant to a brand-new owner (**admin** role). **Async saga** — `202`; a two-hop outbox→inbox choreography, poll the status URL. |

## OpenAPI contract

The OpenAPI document is the source of truth for the frontend's generated client. It is emitted
at **build time** to [`openapi/Iedora.Api.json`](openapi/) (no running server or database needed)
and served at `/openapi/v1.json` at runtime.

## Conventions

- **Project layout** — no loose `.cs` at a project root (only `Program.cs` on host projects). Types live in role folders: `Abstractions/` (interfaces + options), `Persistence/` or `Data/` (EF entities, `DbContext`, model mapping, migrations), `Processing/` (processors + hosted services), `Extensions/` (DI / `ModelBuilder` / usage extension methods) — plus concern folders where a lib doesn't fit that shape (e.g. `Framework.Web` uses `Results/` + `Http/`). Each feature module is its own project (`src/Iedora.<Module>`) holding its vertical slices under `Features/*` + module code + an `<Module>Module.cs` registration; `Iedora.Api` is just the host that composes them. Namespaces stay flat per project (`Framework.Outbox`, not `Framework.Outbox.Abstractions`) so consumers keep a single `using`.
- **Central Package Management** — all NuGet versions live in [`Directory.Packages.props`](Directory.Packages.props) (with transitive pinning); projects reference packages by name only.
- **Security auditing** — [`Directory.Build.props`](Directory.Build.props) promotes vulnerability advisories `NU1901`–`NU1904` to build errors (net10 audits transitively by default), so a known-vulnerable package fails the build.
- **Result pattern** — expected failures are [`ErrorOr`](https://github.com/amantinband/error-or) values from an error catalog (no exceptions/null), mapped to RFC 9457 `ProblemDetails` by the reusable [`Framework.Web.ProblemResults`](framework/Web/Framework.Web/Results/ProblemResults.cs) — the error `code` rides in the problem body as a machine-readable discriminator.
- **Error ownership (module boundary)** — each module keeps its domain errors **private** to itself ([`IdentityErrors`](src/Iedora.Identity/IdentityErrors.cs), [`MenuErrors`](src/Iedora.Menus/MenuErrors.cs)). A module must **never** reference another module's catalog. Genuinely cross-cutting errors (e.g. an unauthenticated request) live in [`Iedora.Identity.Contracts/Auth`](src/Iedora.Identity.Contracts/Auth/CommonErrors.cs) (`CommonErrors`) which every module may use — the *only* catalog a module touches besides its own. Cross-cutting claim reads go through the shared [`ClaimsPrincipalExtensions`](src/Iedora.Identity.Contracts/Auth/ClaimsPrincipalExtensions.cs), not raw claim lookups.
- **Async / reliable messaging (Postgres-native, no broker)** — the [`Framework.Outbox`](framework/Outbox/Framework.Outbox) + [`Framework.Inbox`](framework/Inbox/Framework.Inbox) libraries (each with its own Testcontainers-Postgres test suite). The dispatcher is **`LISTEN`/`NOTIFY`-woken** — a `SaveChanges` interceptor fires `pg_notify` in the same transaction as an outbox insert, so dispatch is near-instant; the periodic poll stays as the safety net for any notification missed while a listener was down (Postgres `NOTIFY` is a wake-up hint, never the durable queue):
  - **Outbox** (production) — `db.EnqueueOutbox(...)` stages a message in the same `SaveChangesAsync` as the request; the single generic [`Iedora.Worker`](src/Iedora.Worker) runs a per-`DbContext` dispatcher (`OutboxProcessor<TContext>`) that claims batches with `FOR UPDATE SKIP LOCKED` (safe across N replicas) and routes each to an [`IOutboxHandler`](framework/Outbox/Framework.Outbox/Abstractions/IOutboxHandler.cs) with retry/backoff — so a crash after commit can't drop the effect. The API only enqueues; SMTP never touches the request path. Each module contributes a self-contained dispatch registration (`Add<Module>OutboxDispatch()` / `Add<Module>Messaging()` in its `Extensions/`) that the worker composes — never referencing a module's web-facing endpoints. Configure `Smtp:*` + `PasswordReset:ResetUrlBase`.
  - **Inbox** (consumption) — `InboxProcessor.ProcessOnceAsync(...)` dedups a received message (via its id) and runs the [`IInboxHandler`](framework/Inbox/Framework.Inbox/Abstractions/IInboxHandler.cs) in one transaction, so at-least-once redelivery is safe. Transport-agnostic; the foundation for background command processing (accept → 202 → worker → result) as product services land. No consumer wired yet.
