# ADR 0003 — Build/materialization runner: minimal DAG, build-then-swap, separable worker

Status: **accepted** · Date: 2026-07-17 · Follows: [ADR 0002](./0002-renderer-evidence-and-rebuild-on-sync.md)

## Context

ADR 0002 chose a **rebuild-on-sync** render model: after data lands, we run
transforms and materialise metric outputs into tables that Evidence reads. That
raises the question — some aggregations will eventually be slow, so how do we
orchestrate building the materialised views without (a) running steps out of
order, (b) blocking dashboard reads on DuckDB's single writer, or (c) doing heavy
work on the serving path?

This is the README's sharp edge #2 ("sync and query fight for the same CPU")
applied to the render stage, plus the `duckdb-vs-postgres.md` single-writer
constraint. The risk is over-building: a real task queue / DAG engine now would be
premature (today every metric runs in milliseconds over a handful of rows) and
would drift toward "rebuilding Grafana one layer down".

## Decision

Build a **minimal build runner now, shaped to grow**, and defer the heavy parts
until a concrete signal (a genuinely slow metric) fires — the same discipline as
ADR 0001's storage seam.

### Build now (small)
1. **A build step + dependency DAG.** The pipeline is a set of named build steps
   (transforms and metric materialisations), each declaring the steps it depends
   on. The runner executes them in topological order. Tens of lines, not an
   engine. This makes "order matters" and "rebuild what's downstream" expressible.
2. **Build-then-swap, from day one.** Each materialisation writes to a staging
   name (e.g. `metric_x__building`) and then atomically replaces the live object
   (`metric_x`). Readers keep hitting the previous version until the swap, so a
   long build never blocks dashboard reads. Cheap now, painful to retrofit.
3. **A separable worker, off the serving path.** The build runs as an in-process
   worker triggered by: a **post-sync hook**, a **manual "refresh"**, or (later) a
   schedule. It never runs on a dashboard request. Structured so "pop it out to a
   separate process" is a deployment change, not a redesign.
4. **One writer at a time.** A build lock/queue serialises builds (DuckDB is
   single-writer); a second trigger while a build is running enqueues, it does not
   race.

### Defer until a metric is actually slow (the concrete signal)
- **Incremental recompute** — only rebuild affected grains instead of full
  rebuilds; recording-rule-style pre-rollups. This is federation territory (WS8).
- **A real scheduler / job queue** — retries, backoff, distributed workers,
  cross-process coordination.

### The escape hatch
When the DAG or incremental logic stops being trivial, that is the signal to
**adopt dbt or SQLMesh** as the dev-time transform + materialisation runner rather
than grow a bespoke DAG engine (ADR 0001 parking lot). Keeping metrics as plain,
tested SQL (WS4) keeps that door open. We do **not** build our own incremental
model engine.

## Consequences
- The Evidence spike's `materialise` step (WS5-P3) is written as **one build step**
  in this runner, with build-then-swap — deliberately minimal, but the right shape.
- WS3-P2 in the roadmap is recast from a vague "transform runner" into this
  build/materialisation runner.
- Metric materialisation output is the seam Evidence reads (per ADR 0002); the
  runner produces it, the renderer only formats it.
- Observability (which steps ran, timings, last-built-at) rides along with WS9-P3;
  for now the runner logs steps with the `principal`/run in context.
