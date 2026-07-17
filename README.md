# Factarium

A design north star for a self-contained engineering-metrics system — one that is
**collector, aggregator, shaper, and renderer all at once**, with zero external
setup, fast at small scale, and a clean escape hatch for when it grows.

This folder captures the *why* and the *shape*. It is deliberately opinionated.

---

## The north star

> **I should not have to set up _anything_ else if I don't want to.**
> If I want to ingest Claude OTEL data, it does that. If I want to sync GitHub on
> command, it does that. One artifact. Four verbs. No stack to assemble.

The frustration this answers: today, trying anything with data means spinning up
system A to *get* it, system B to *shape* it, and system C to *draw* it. For
someone without a data background that tax is fatal to experimentation. The fix is
to collapse the stack into one thing.

This is not a beginner's shortcut. It's a real, respected tradition —
single-artifact, local-first tools: SQLite, DuckDB, Grafana (one binary),
Metabase (one jar). Someone has already built almost exactly this vision:
**[Rill Data](https://www.rilldata.com/)** — one binary, DuckDB underneath,
`source → model (SQL) → metrics view → dashboard`, no external anything. Study it
as a reference for how the sausage is made; you don't have to adopt it.

## The four verbs (one pipeline, one artifact)

```
 sync facts ──► define transformations ──► create metrics ──► display dashboards
  (collect)          (aggregate/shape)         (shape)            (render)
      │                    │                      │                   │
      └──── all four are internal modules that happen to ship in ONE process ────┘
```

- **Sync facts** — connectors do exactly one thing: fetch and dump raw records
  into a source-agnostic landing zone. Boring on purpose.
- **Define transformations** — re-runnable SQL turns raw → a conformed,
  source-independent model. Enrichment/identity/tagging happen here, over data you
  already hold (never re-fetching the source).
- **Create metrics** — each metric is named, versioned, **tested** SQL with an
  explicit grain. Transparency = metrics are code with fixtures, not settings in a
  BI tool.
- **Display dashboards** — a renderer reads the conformed model + metrics. Dumb by
  design; all logic lives one layer down where it can be tested.

## The scaling story is the best part (and it's real)

Most people assume *bigger = the system does more*. The north star says the
opposite: **at scale the system does _less_, because you push aggregation upstream
and point at the result as just another source.**

That is **federation**, and it's how serious systems actually scale:
- Prometheus **recording rules** pre-aggregate so dashboards read cheap rollups.
- Prometheus **remote-read / Thanos / Mimir** let a small local instance query a
  big upstream as if it were local.
- Every warehouse's "materialise a rollup, then query the rollup" pattern.

**The rule this imposes on day one:** a *source* must be so uniform that a live
GitHub API and your own pre-aggregated rollup server are indistinguishable to
everything downstream. Prove it immediately by shipping **two** source
implementations from the start — a live API puller *and* a "read already-aggregated
rows from a file/URL." If the substitution works on day one, the escape hatch stays
open forever. If GitHub-ness leaks into the pipeline, it welds shut.

## The honest sharp edges

1. **The renderer is a whole product by itself.** Collector/aggregator/shaper are
   tractable solo. A good dashboard renderer is where projects like this quietly
   die — you end up rebuilding Grafana. **Embed an existing renderer; don't build
   one.** Keep the one-artifact UX without spending a year on a charting library.
2. **Sync and query fight for the same CPU.** A long-running collector and an
   interactive dashboard query competing in one process is the first thing that
   feels slow (this is the OTEL/CPU worry — a correct instinct). Make the collector
   a separable worker *inside* the process now, so "pop it out later" is a
   deployment change, not a redesign.
3. **The metric/semantic layer is the hard part** — no substrate saves you here.
   Metrics-as-tested-code is what makes the system trustworthy; budget for it.

## The principle that keeps the star reachable

**One artifact in deployment, four clean seams in architecture.** Collect /
aggregate / shape / render are modules behind stable interfaces that merely
*happen* to ship together today. That is exactly what lets you later lift the
collector out to an external aggregator (the scaling story) without touching the
shaper or renderer. A monolith with real internal seams gives you the zero-setup UX
*and* the escape hatch. A tangled monolith gives you neither.

## The three decisions to make NOW (most people defer these and regret it)

1. **Pick an embedded substrate.** "Don't set up anything" argues against running a
   server. See [`duckdb-vs-postgres.md`](./duckdb-vs-postgres.md) — and note the
   storage engine should itself be a swappable module behind a seam.
2. **Make `source` a uniform interface** with a pre-aggregated implementation from
   day one, so the scaling path is exercised, not merely hoped for.
3. **Embed the renderer** rather than build it.

---

### Files in this folder
- [`duckdb-vs-postgres.md`](./duckdb-vs-postgres.md) — the storage-engine question,
  including whether to support **both** and whether that's even possible.
- [`storage-seam/`](./storage-seam/) — a runnable spike of the swappable storage
  interface: DuckDB adapter (working), Postgres adapter (stub), and a demo that
  runs collect→aggregate→shape→render through the seam.
- See also [`../metrics-example/`](../metrics-example/) — the tape-together stack
  (DevLake + OTel + VictoriaMetrics + Grafana) that this approach is the
  from-scratch alternative to.
