# Factarium

Quick, transparent, dev-centric metrics: **sync → transform → aggregate → render**,
on a single Postgres. See [`docs/objective.md`](docs/objective.md) for the vision and
[`docs/implementation-plan.md`](docs/implementation-plan.md) for the phased roadmap.

> Status: **Phase 1 — sync foundation.** On top of the Phase 0 skeleton: the
> bronze-tier raw store, the pull/push sync abstraction, a GitHub connector, a
> deterministic fake-data generator, snapshot/restore tooling, and Quartz-scheduled
> syncs with a manage/trigger API. Transforms + dashboards arrive in Phase 2.

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

# 3a. Run the API (serves built SPA + JSON API) at http://localhost:8080
dotnet run --project src/Factarium.Api

# 3b. For frontend hot-reload, run the Vite dev server at http://localhost:5173
cd web && npm install && npm run dev
```

Or run the whole stack (api + web + postgres) in containers:

```bash
docker compose -f deploy/docker-compose.yml up
```

- API + built SPA: http://localhost:8080
- Vite dev server (hot reload, proxies `/api`): http://localhost:5173
- Health: http://localhost:8080/api/health

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
