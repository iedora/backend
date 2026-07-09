# Working in this repo

Architecture, module layout, and conventions are in [README.md](README.md) — read it, don't restate it here. This file is only the delta: commands, hard rules, and gotchas that aren't obvious from the code.

## Commands

- Build: `dotnet build Iedora.slnx` — treat any warning as a failure (0 warnings is the bar).
- Test: `dotnet test --solution Iedora.slnx` (MSTest 4 on the Microsoft Testing Platform, wired via `global.json` — not legacy VSTest). Integration + e2e tests need Docker (Testcontainers Postgres).
- Migration: `dotnet ef migrations add <Name> --project src/Iedora.Data --context <Identity|Tenancy>DbContext --output-dir <Identity|Tenancy>/Migrations`. The API never self-migrates — `Iedora.MigrationService` owns the schema.

## Rules

- Every change lands on a branch off `main`, builds clean, passes the **full** suite, then a conventional commit + a PR. Never commit to `main`.
- No `Co-Authored-By: Claude` trailer in commits.
- Newest stable package versions only (check the live registry). NuGet audit is a build error, so a vulnerable dep fails the build.
- Research the current idiomatic approach before building anything non-trivial — don't assume from memory.
- The Bun service at `~/Documents/iedora/app/services/auth/` is an MVP **reference, not a spec**: port the intent, design the shape for .NET.

## The one boundary to never break

Modules don't touch each other's tables, errors, or internals. A cross-module call goes through the target module's `Contracts/` namespace **only** (`Iedora.Api.<Module>.Contracts`) — add the interface there, implement it `internal` in the owning module, resolve via DI. Each module owns its error catalog; cross-cutting errors/helpers live in `Iedora.Api/Shared`.

## Writes — one pattern, every domain write

Every domain write is asynchronous and identical in shape (`Framework.Commands`):
1. **Validate synchronously first** — shape + auth + anything checkable via a sync read → immediate `4xx`. The client always gets validation errors up front.
2. `db.SubmitCommand(commandId, type, payload, clock)` stages a `Pending` command **+** an outbox message in one transaction; the endpoint returns `202 { commandId, statusUrl }`.
3. A `CommandHandler<TContext, TPayload>` runs the work off the outbox and records `Succeeded`(resultUrl) / `Failed`(code) on the command (expected failure = `ErrorOr`; exception = transient → outbox retries).
4. Client polls `GET /<module>/commands/{id}` (SSE later).

The only exception: **login/refresh stay synchronous** — they're an auth token exchange, not a domain command. Cross-module *reads* also stay synchronous (via `Contracts`).

## Background maintenance — retention sweeps

Unbounded tables (view-dedup markers, processed outbox/inbox rows) are pruned by `Framework.Maintenance`: an `IRetentionSweep` is one idempotent `WHERE`-bounded delete, and a single hosted service runs every registered sweep on a fixed interval, isolating per-sweep failures. Wire sweeps **only in `Iedora.Worker`** (`AddRetentionSweeper()` + `AddRetentionSweep<T>()`, or `AddOutboxRetention<T>()` / `AddInboxRetention<T>()`) — never in the API host, so pruning doesn't run on every web replica. Each retention window is its own options type (defaults are safe); a new unbounded table gets a new sweep, not a new timer.

## Gotchas (each already cost a debugging cycle)

- Tests run on Testcontainers Postgres, never SQLite (SQLite can't translate `DateTimeOffset`).
- EF Core can't compose `.Where(...)` over a `select new Record(...)` projection — filter/order on the entities first, project last.
- For an enum column with a DB default, make the intended default the enum's **zero** value — EF reads a 0-valued enum as "unset" and silently applies the DB default.
- Aspire e2e over HTTP is flaky (`CreateHttpClient` hangs) — assert boot + healthy there, and cover HTTP behaviour in the integration tests instead.
