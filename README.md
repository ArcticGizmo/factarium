<h1 align="center">Factarium</h1>
<p align="center">
  <img src="docs/factarium.png" alt="Factarium" width="128" height="128" />
</p>

<p align="center">
<strong>Quick, transparent, dev-centric metrics.</strong>
</p>

# Factarium
**Sync → transform → aggregate → render**, on a single Postgres. 
See [`docs/objective.md`](docs/objective.md) for the vision and
[`docs/implementation-plan.md`](docs/implementation-plan.md) for the phased roadmap.

> Status: **Phase 7 — Claude Code OTEL (push) ingestion.** Adds a push source: an
> OTLP/HTTP JSON receiver at `POST /v1/metrics` ingests Claude Code telemetry
> (cost, tokens, lines of code, sessions) through the same transform → aggregate →
> render pipeline, rendered as a Claude Code usage dashboard. Person attribution
> now spans **three sources** — a GitHub login, a Jira account, and a Claude Code
> user can all map to one Person. (Phases 5 annotations/snapshots and 6 advanced
> pipeline profiles were skipped for now.)

## Try the full loop (with sample data)

```bash
docker compose -f deploy/docker-compose.yml up -d postgres
dotnet run --project src/Factarium.Cli -- seed          # deterministic GitHub-shaped data
dotnet run --project src/Factarium.Cli -- pipeline run  # transform + aggregate
dotnet run --project src/Factarium.Api                  # dashboard at http://localhost:4600
```

## Stack

- **Backend:** .NET 10, EF Core, PostgreSQL (Quartz.NET scheduling lands in Phase 1)
- **Frontend:** Vue 3 + Vuetify 4 + vue-echarts
- **Layout:** clean-architecture projects under `src/`, SPA under `web/`

## Prerequisites

- .NET SDK 10
- Node.js 22+
- Docker (for Postgres and the container ship target)

## Run it (dev-mode)

```bash
# 1. Start Postgres (host port 6880)
docker compose -f deploy/docker-compose.yml up -d postgres

# 2. Apply migrations + seed the local user
dotnet run --project src/Factarium.Cli -- db migrate

# (optional) Load deterministic sample GitHub data so dashboards have something to show
dotnet run --project src/Factarium.Cli -- seed

# 3a. Run the API (serves built SPA + JSON API) at http://localhost:4600.
#     `dotnet watch` gives C# Hot Reload — most edits apply without a manual
#     restart (some structural changes still prompt/require one).
dotnet watch --project src/Factarium.Api    # or `dotnet run` for no hot reload

# 3b. For frontend hot-reload, run the Vite dev server at http://localhost:4601.
#     It proxies /api -> http://localhost:4600 (override with VITE_API_PROXY).
cd web && npm install && npm run dev
```

During front-end work, develop against **http://localhost:4601** — Vite HMR reflects
`.vue`/JS edits instantly, proxying API calls to the `dotnet watch` process on 4600.
Two long-running processes, both hot-reloading; you rarely restart either by hand.

Or run the whole stack (api + web + postgres) in containers:

```bash
docker compose -f deploy/docker-compose.yml up
```

- API + built SPA: http://localhost:4600
- Vite dev server (hot reload, proxies `/api`): http://localhost:4601
- Health: http://localhost:4600/api/health

## Ship a single-exe

```bash
# Windows
./deploy/single-exe/publish.ps1                 # -> artifacts/single-exe/

# Linux/macOS
deploy/single-exe/publish.sh linux-x64
```

The published executable bundles the SPA, applies migrations on startup, and serves
the API + SPA on `ASPNETCORE_URLS`.

## Configuration

The Postgres connection string is read from `ConnectionStrings:Factarium`
(see `appsettings.json`) or the `FACTARIUM_DB` environment variable.

## Tests

```bash
dotnet test
```
