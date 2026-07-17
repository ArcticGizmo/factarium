# DuckDB vs Postgres — and "why not both?"

You asked: *querying data out is going to be the bottleneck, so is it worth taking
Postgres as the one dependency because it's more performant long term? Or do we
make it work with either?*

Two honest answers, in order of importance.

## 1. Your premise is backwards for this workload

The instinct "Postgres will be faster at querying data out" is true for the
workload Postgres was built for — **OLTP**: many concurrent users, lots of small
transactional reads/writes, point lookups by primary key. That is *not* what a
metrics system does.

A metrics system does **OLAP**: scan lots of rows, `GROUP BY`, window functions,
aggregate over time. That is precisely what **DuckDB was built for and Postgres was
not.** DuckDB is columnar and vectorised; for "compute PR cycle time p50 by team by
week over the last year," it will typically **beat** a stock Postgres by a wide
margin at small-to-medium scale.

So the bottleneck you're picturing — reading data *out* for metrics — is DuckDB's
home turf, not its weakness.

### Then where does Postgres actually win?

Not at raw analytical speed at your scale. It wins on things that arrive **later**:

| Postgres wins when… | Why |
|---|---|
| Many users / processes hit it **at once** | It's a real client-server DB with MVCC concurrency; DuckDB is embedded (in-process). |
| You need concurrent writers | DuckDB is single-writer per database file. |
| Data outgrows one machine's working set | Mature indexing, partitioning, `TimescaleDB` for time-series. |
| You need battle-tested ops | Backups, replication, 30 years of tooling. |

Notice every one of those is a **concurrency / scale / operations** concern — i.e.
exactly the problems your federation scaling story defers by pushing aggregation
upstream. **DuckDB's weakness is the thing your architecture is designed to avoid
hitting early.**

### The DuckDB caveat you must respect

DuckDB is **embedded and single-writer**: one process holds the database file for
writing at a time (other processes can open it read-only). This directly collides
with sharp edge #2 from the README — a collector writing while dashboards read. The
mitigation is architectural, not a reason to reject it:
- Funnel all writes through the one collector worker (single writer — fine).
- Let dashboards read (concurrent readers — fine).
- If that contention ever bites, it's the signal to switch the storage adapter to
  Postgres — which is exactly what section 2 makes cheap.

## 2. "Make it work with either" — yes, possible, and it's the on-brand choice

Supporting DuckDB **or** Postgres is very doable, because both speak SQL, and it's
the same move you're already making for *sources*: **put the engine behind a seam.**
Storage becomes a swappable module, just like a data source is.

It is **not free**, though. SQL dialects diverge in exactly the spots this system
leans on hardest:

| Concern | DuckDB | Postgres |
|---|---|---|
| JSON extraction | `payload->>'field'`, `json_extract` | `payload::jsonb ->> 'field'` (mostly compatible, not identical) |
| Upsert | `INSERT ... ON CONFLICT` (supported) | `INSERT ... ON CONFLICT` (the reference impl) |
| Performance tuning | zone maps, columnar — *no indexes to manage* | indexes, `VACUUM`, partitioning — a different discipline |
| Types / date funcs | mostly Postgres-compatible on purpose | the reference |

DuckDB deliberately mirrors Postgres syntax, so the *common* surface is large. The
pain is concentrated at the two ends: **ingest/upsert** and **any engine-tuned
metric query**.

### How to make it portable without drowning in `if engine == …`

Three layers, cost rising as you go down:

1. **Transforms in dbt.** dbt has first-class adapters for **both** DuckDB and
   Postgres. If stages 2–3 (transform, metrics) are dbt models, they're portable
   for near-free, and reading dbt's compiled SQL teaches you the dialects. This is
   the single highest-leverage decision for portability.
2. **A thin storage adapter interface** for the parts dbt doesn't own — connect,
   land-raw-record (upsert), run-query. Two implementations: `DuckDBStore`,
   `PostgresStore`. Everything else in the app talks to the interface, never to the
   driver. (In Python, `SQLAlchemy` + the `duckdb-engine` dialect gives you one API
   over both; or hand-roll the interface — it's ~5 methods.)
3. **Confine engine-specific SQL** to that adapter and a small handful of tuned
   queries. Keep the middle (staging/core/metrics) in portable SQL / dbt.

```
   app code ──► StorageEngine (interface) ──►┬── DuckDBStore   (default, embedded)
                                             └── PostgresStore (opt-in, at scale)
              transforms/metrics ──► dbt ────► duckdb adapter  |  postgres adapter
```

### The twist that half-dissolves the dichotomy

It may not even be strictly either/or:

- **DuckDB can read Postgres directly** via its `postgres` extension — `ATTACH` a
  Postgres database and query it as if local. That's literally your federation
  escape hatch built into the engine: keep the embedded DuckDB as the query/render
  brain, move heavy storage to Postgres, and point DuckDB at it.
- **Postgres can run DuckDB inside it** via the emerging `pg_duckdb` extension —
  columnar analytics execution within a Postgres server. (Newer / less battle-worn;
  note it, don't bet the build on it yet.)

## Recommendation

1. **Start on DuckDB.** It matches the north star (embedded, zero setup) *and* it's
   the faster engine for the read-out workload you're worried about, at the scale
   you're starting at. The premise that pushed you toward Postgres doesn't hold
   here.
2. **Build the storage seam now, but only one adapter.** Ship `DuckDBStore`; define
   the interface so `PostgresStore` is a later drop-in, not a rewrite. Don't build
   both today — build the *seam* today and one engine.
3. **Keep transforms in dbt** so the expensive-to-port layer is portable by
   construction.
4. **Switch to Postgres when a concrete signal fires** — write contention, multiple
   concurrent users, or data outgrowing the machine — not on a hunch about "long
   term." When it fires, you add an adapter (and can even have DuckDB `ATTACH` the
   Postgres instance during the transition).

In short: taking your own advice **is** the right call — make storage a swappable
module — but the default behind that seam should be DuckDB, not Postgres. You get
the zero-setup speed now and the industrial-strength option the day you actually
need it.

---
*Claims worth re-verifying as versions move: DuckDB's concurrency model, the
`postgres` scanner extension, and `pg_duckdb` maturity all evolve quickly — check
current docs before committing to any of the three "twist" paths.*
