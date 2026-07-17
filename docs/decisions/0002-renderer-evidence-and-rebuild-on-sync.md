# ADR 0002 — Renderer: Evidence.dev, rebuild-on-sync, reading materialized metrics

Status: **accepted** · Date: 2026-07-17 · Follows: [ADR 0001](./0001-typescript-backend-and-remote-readiness.md)

## Context

The "display" verb is the last unbuilt verb (collect/aggregate/shape run on real
data as of M1). The README is emphatic here — sharp edge #1: **embed a renderer,
don't build one**, or you quietly rebuild Grafana and the project dies. So this
is a "pick and integrate", not a "design a charting UI".

Constraints from the north star + ADR 0001: browser-served, single TS artifact,
data lives in embedded DuckDB behind the storage seam, metrics are tested SQL,
and the renderer must be *dumb* (all logic one layer down, where it is tested).

## The decision has two layers

### Layer 1 — rendering model: **rebuild-on-sync static** (not live-per-request)

The candidate tools (Evidence, Observable Framework) are both **static-site
generators**: they run queries at *build* time and emit static HTML/JS, rather
than querying the database per request the way Grafana/Metabase/Rill do. Rather
than fight that, we adopt it, because it fits Factarium:

- Engineering metrics are fresh enough at **sync cadence** (minutes/hours), not
  real-time. "Rebuild the dashboards after each sync" is an honest, simpler model
  than a live query server.
- It keeps the artifact simple: serve static files, no per-request query engine
  in the render path.

Accepted tradeoff: static dashboards can't run our `authorize()` per view, so
**per-user authorization is coarse** (the serving layer gates access to whole
dashboards, not rows). Fine for single-owner-per-instance (ADR 0001); a documented
limit for any future multi-user remote instance.

### Layer 2 — tool: **Evidence.dev**

| | Evidence.dev | Observable Framework |
|---|---|---|
| Model | SQL-in-markdown → components | JS-first + data loaders (polyglot) |
| Fit to "metrics as tested SQL" | **direct** — dashboards are SQL results | indirect — bespoke JS viz |
| DuckDB | native data source | native (loaders + file format) |
| License | MIT | MIT |
| Maintenance (2026) | **very active** (v40.x, Feb 2026) | stalled (last release Mar 2025) |
| Rebuild-Grafana risk | low (declarative) | higher (you write the app) |
| Embedded layout | first-class (hide chrome) | via custom layout |

Evidence wins on the axis that matters most: our thesis is *metrics = SQL*, and
Evidence renders SQL results into dashboards with minimal glue. Observable is more
powerful for bespoke, code-driven visualisation, but that power is exactly the
pull toward "build your own renderer" the README warns against — and its stalled
release cadence is a longevity risk for a long-lived tool.

### Layer 3 — the seam: **the renderer reads materialized metric outputs**

To keep the renderer dumb and metrics authoritative, Evidence does **not**
re-derive metrics. The pipeline materialises the tested metric outputs (from the
metric registry) into DuckDB tables/views (or parquet); Evidence reads *those*.
So metric logic stays in tested SQL (WS4), and the renderer only formats
pre-computed numbers. This materialisation is the stable seam: a future
alternative renderer (a custom SPA hitting an HTTP API) would read the same
materialised outputs, so the renderer stays swappable.

## The main risk (to validate in the spike)

Evidence needs a **Node build step** (`sources` + `build`). That sits in tension
with the single-binary goal (WS7): a shipped binary can't easily run Evidence's
toolchain to refresh dashboards at runtime. Options, in order of preference:
1. **Dev/deploy-time build** — build dashboards where Node exists (dev, CI, a
   container), serve the static output. Refresh = re-run build after sync.
2. Ship pre-built dashboards inside the binary; "refresh data" = redeploy.
3. If neither suffices later, revisit a live custom SPA (the escape hatch).

For M2 (dashboards you can look at, in dev) option 1 is fine. The binary-refresh
story is explicitly a WS7 problem, flagged here, not solved here.

## Decision
- Renderer: **Evidence.dev**, embedded-layout mode, reading materialised metric
  outputs from DuckDB. Rendering model: **rebuild-on-sync static**.
- Rejected now: Observable Framework (longevity + bespoke-JS pull), and embedding
  a live BI server like Grafana/Metabase (separate process, not one TS artifact).

## Consequences / next
- WS5 spike: materialise `pr_cycle_time` to a table, point a minimal Evidence
  project at the DuckDB file, render one dashboard, confirm the dev flow.
- The serving layer (WS5-P1) serves Evidence's static output; per-dashboard authz
  gating lands with real auth (WS6).
- Add a `metrics → materialise` step to the pipeline (small extension of WS3/WS4).
