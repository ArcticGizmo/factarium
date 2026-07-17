# Factarium Roadmap

A living checklist. We review and re-scope as we go — check items off, add/split
tasks, and let the phases drift as reality teaches us things.

- **What this is:** the *what* (workstreams) and the *order* (release milestones).
- **Ground rules we're building to:** see [`README.md`](../README.md) (north star,
  four verbs, sharp edges) and [`decisions/0001-*.md`](./decisions/0001-typescript-backend-and-remote-readiness.md)
  (TS backend, embedded DuckDB, remote-readiness by seams, single-owner-per-instance).

**Legend:** `- [x]` done · `- [ ]` todo · 🟡 = partially done / in progress.

---

## Release milestones (the cross-cut)

Each milestone is a *capability* you can demo, assembled from workstream phases.
This is the primary "are we making progress" view.

- [ ] **M0 — Foundations** 🟡 · local single-user skeleton, seams in place, one loop runs.
  Pulls: WS0 all · WS1 P1 · WS6 P1. *(nearly complete — see WS0/WS1/WS6)*
- [ ] **M1 — Real data, end to end** · one live connector → transform → one *tested* metric, run from the CLI.
  Pulls: WS2 P1–P2 · WS3 P1 · WS4 P1 · WS9 P1.
- [ ] **M2 — Dashboards you can look at** · serving layer + embedded renderer showing real metrics in a browser.
  Pulls: WS5 P1–P3 · WS3 P2 · WS4 P2.
- [ ] **M3 — A shippable artifact** · single self-contained binary, config, first-run UX.
  Pulls: WS7 P1–P3 · WS9 P2.
- [ ] **M4 — Remote-ready** · real auth provider + real permission policy, hardened serving.
  Pulls: WS6 P2–P3 · WS5 P4 · WS9 P3.
- [ ] **M5 — Scale & federation** · Postgres adapter and/or pre-aggregated upstream source.
  Pulls: WS1 P2–P3 · WS8 P1–P3 · WS2 P4.

---

## Workstreams (roadmap items)

### WS0 — Foundations & project hygiene 🟡
The skeleton everything hangs off.
- **P1 — Scaffold** 
  - [x] Language/architecture decisions recorded (ADR 0001)
  - [x] TS/Node project (`package.json`, `tsconfig`, scripts: `demo`/`typecheck`/`test`)
  - [x] Core seams: storage, principal, auth, authz
  - [x] Runnable end-to-end `demo.ts` through the seams (verified)
- **P2 — Hygiene**
  - [ ] `.gitattributes` to settle LF/CRLF normalisation
  - [ ] Linter + formatter (eslint/biome), wired into `npm run` and CI
  - [ ] `CONTRIBUTING`/dev-setup notes; decide Bun-vs-Node dev story is documented
  - [ ] Keep the Python `storage-seam/` clearly labelled "reference only"

### WS1 — Storage substrate & portability 🟡
The swappable engine behind the seam.
- **P1 — DuckDB default**
  - [x] `StorageEngine` interface (collect/aggregate/shape + cursor + `jsonField`)
  - [x] `DuckDBStore` adapter via `@duckdb/node-api` (verified)
  - [x] `getStore()` factory (the one knob)
  - [ ] Persistence/lifecycle: file DB path, migrations story, safe open/close
- **P2 — Portability layer**
  - [ ] Decide transform portability approach (plain SQL now; dbt/SQLMesh as *dev-time* later)
  - [ ] Confine any engine-specific SQL to the adapter; document the divergence surface
- **P3 — Postgres adapter** *(only when a scale signal fires)*
  - [ ] Implement `PostgresStore` against the same interface
  - [ ] Parity test suite runs both engines against the same fixtures

### WS2 — Collect: sources & connectors
Fetch raw facts; stay dumb. **The make-or-break seam** (README rule).
- **P1 — Source seam**
  - [ ] Uniform `Source` interface (fetch → `RawRecord[]`, blind to what's downstream)
  - [ ] **Two** day-one implementations: a live API puller *and* a read-pre-aggregated-rows source (proves the federation escape hatch)
  - [ ] Credential seam (`CredentialProvider`): local file/env default; secrets-manager later *(remote-readiness item 4)*
- **P2 — First real connector**
  - [ ] GitHub connector (pull requests) → `land()`
  - [ ] Incremental sync using `getCursor`/`setCursor`; idempotent re-sync verified
- **P3 — Collector as separable worker** *(sharp edge #2)*
  - [ ] Run collection off the request path (in-process worker/queue) so "pop it out later" is a deployment change
  - [ ] Scheduling (manual "sync now" + interval)
- **P4 — More connectors**
  - [ ] Jira (issues) · [ ] Claude Code OTEL (llm usage)

### WS3 — Aggregate: transformations
Raw → a conformed, source-independent model.
- **P1 — Core model**
  - [ ] Re-runnable `raw_records` → `core_*` transforms (build on the demo's pattern)
  - [ ] Identity/enrichment/tagging over data already held (never re-fetch)
- **P2 — Transform runner**
  - [ ] A small runner that orders/executes transforms deterministically & idempotently
  - [ ] Portable SQL via `jsonField()`; engine specifics stay in the adapter

### WS4 — Shape: metrics as tested code
The trust layer. No substrate saves you here.
- **P1 — Metric primitive**
  - [ ] A metric = named, versioned SQL with explicit grain
  - [ ] Fixture-based tests (`node --test`): metric SQL over known input → known output
- **P2 — Metric registry**
  - [ ] Register/discover metrics; expose them to the serving layer behind the authz checkpoint
  - [ ] A couple of real metrics (PR cycle time, throughput) with tests

### WS5 — Display: serving layer & embedded renderer
Dumb by design; all logic one layer down. **Embed, don't build** (sharp edge #1).
- **P1 — Serving layer**
  - [ ] HTTP server (runtime-agnostic APIs so Node→Bun stays clean)
  - [ ] Query/metrics API that flows `RequestContext` and calls `Authorizer.authorize()` before returning data
- **P2 — Serve the SPA**
  - [ ] Serve a compiled web UI from the same origin as the API (avoids CORS/cookie pain)
- **P3 — Embed a renderer**
  - [ ] Evaluate Evidence.dev vs Observable Framework against the seam; pick one (new ADR)
  - [ ] First dashboard rendering a real metric
- **P4 — Hardening for remote**
  - [ ] TLS/reverse-proxy story, bind-address config, security headers

### WS6 — Auth & permissions (remote readiness) 🟡
Inert locally; the seams exist so remote is a swap, not a retrofit.
- **P1 — Seams in place**
  - [x] `Principal`/`RequestContext` threaded through the verbs
  - [x] `AuthProvider` + `LocalOwnerProvider` (no-op login)
  - [x] `Authorizer` checkpoint + `AllowAll` default
- **P2 — Real authentication** *(when going remote)*
  - [ ] `OidcProvider` (or session-based) implementation
  - [ ] Transport wiring: token/cookie → `authenticate()` → `Principal`
- **P3 — Real authorization**
  - [ ] Role/permission policy replacing `AllowAll`
  - [ ] Decide resource granularity (per-source / per-metric / per-dashboard visibility)

### WS7 — Packaging & distribution
The "one artifact, zero setup" promise, made real. Target packager: **Bun** (`bun build --compile`).
- **P1 — Runtime config**
  - [ ] Config file + env resolution (DB path, port, connector creds, engine choice)
  - [ ] First-run UX (initialise DB, sensible defaults, no manual setup)
- **P2 — Single binary**
  - [ ] Introduce Bun for packaging; produce one executable bundling JS + web assets + DuckDB
  - [ ] Confirm native DuckDB addon embeds cleanly
- **P3 — Cross-platform release**
  - [ ] Build matrix (win/mac/linux) + versioned releases
  - [ ] Smoke test: download-and-run on a clean machine

### WS8 — Federation & scale
At scale the system does *less* — point at pre-aggregated results as just another source.
- **P1 — Pre-aggregated source** (dependency: WS2 P1)
  - [ ] Prove a rollup server / file is indistinguishable from a live source downstream
- **P2 — External storage** (dependency: WS1 P3)
  - [ ] Postgres-backed deployment; or DuckDB `ATTACH` Postgres during transition
- **P3 — Recording-rule pattern**
  - [ ] Materialise rollups; dashboards read cheap pre-aggregated tables

### WS9 — Quality, testing & DX (cross-cutting)
- **P1 — Test foundation**
  - [ ] `node --test` harness + fixtures pattern for metrics/transforms
  - [ ] Storage adapter test suite (reusable across engines)
- **P2 — CI**
  - [ ] CI runs typecheck + lint + tests on push
  - [ ] Release automation hook (feeds WS7 P3)
- **P3 — Observability & ops**
  - [ ] Structured logging (with `principal` in context)
  - [ ] Backup/restore story for the DuckDB file; graceful shutdown

---

## Parking lot (decide later)
- Transform portability via dbt/SQLMesh as a dev-time tool (compile → ship SQL).
- `pg_duckdb` / MotherDuck as alternative scale paths.
- Multi-tenant data model (tenant column) — only if the single-owner-per-instance
  assumption is ever dropped (would be a schema migration; see ADR 0001).
