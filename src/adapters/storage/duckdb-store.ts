/**
 * DuckDB adapter — the default engine (TS port of the Python spike's duckdb_store.py).
 *
 * Embedded (in-process), zero setup, columnar/OLAP — fast at the read-out
 * workload a metrics system actually runs. Uses the first-party binding
 * `@duckdb/node-api`. Nothing outside this file imports that package.
 */

import { type DuckDBConnection, DuckDBInstance, type DuckDBValue } from "@duckdb/node-api";

import type { RawRecord, StorageEngine, SyncCursor } from "../../core/storage.js";

const DDL: string[] = [
  `CREATE TABLE IF NOT EXISTS raw_records (
     source       VARCHAR   NOT NULL,
     entity       VARCHAR   NOT NULL,
     natural_key  VARCHAR   NOT NULL,
     payload      JSON      NOT NULL,
     fetched_at   TIMESTAMP NOT NULL,
     ingested_at  TIMESTAMP NOT NULL DEFAULT now(),
     PRIMARY KEY (source, entity, natural_key)
   )`,
  `CREATE TABLE IF NOT EXISTS sync_state (
     source       VARCHAR NOT NULL,
     connection   VARCHAR NOT NULL,
     entity       VARCHAR NOT NULL,
     cursor       VARCHAR,
     last_status  VARCHAR,
     error        VARCHAR,
     last_run_at  TIMESTAMP,
     PRIMARY KEY (source, connection, entity)
   )`,
];

/** Convert DuckDB scalars into plain JS (BIGINT arrives as bigint). */
function toPlain(value: unknown): unknown {
  return typeof value === "bigint" ? Number(value) : value;
}

export class DuckDBStore implements StorageEngine {
  readonly paramstyle = "?";
  private instance!: DuckDBInstance;
  private con!: DuckDBConnection;

  // ":memory:" for tests/demo; a file path for persistence.
  constructor(private readonly path: string = "factarium.duckdb") {}

  // --- lifecycle ---------------------------------------------------------
  async initialize(): Promise<void> {
    this.instance = await DuckDBInstance.create(this.path);
    this.con = await this.instance.connect();
    for (const stmt of DDL) await this.con.run(stmt);
  }

  async close(): Promise<void> {
    this.con?.closeSync();
    this.instance?.closeSync();
  }

  // --- collect -----------------------------------------------------------
  async land(records: Iterable<RawRecord>): Promise<number> {
    // DuckDB dialect: qmark params, CAST(? AS JSON), ON CONFLICT DO UPDATE.
    const sql = `
      INSERT INTO raw_records
          (source, entity, natural_key, payload, fetched_at, ingested_at)
      VALUES (?, ?, ?, CAST(? AS JSON), CAST(? AS TIMESTAMP), now())
      ON CONFLICT (source, entity, natural_key) DO UPDATE SET
          payload     = EXCLUDED.payload,
          fetched_at  = EXCLUDED.fetched_at,
          ingested_at = now()`;
    let n = 0;
    for (const r of records) {
      await this.con.run(sql, [
        r.source,
        r.entity,
        r.naturalKey,
        JSON.stringify(r.payload),
        r.fetchedAt.toISOString(),
      ]);
      n += 1;
    }
    return n;
  }

  // --- aggregate / shape / render ---------------------------------------
  async execute(sql: string, params?: readonly unknown[]): Promise<void> {
    await this.con.run(sql, params ? ([...params] as DuckDBValue[]) : undefined);
  }

  async query<T = Record<string, unknown>>(sql: string, params?: readonly unknown[]): Promise<T[]> {
    const reader = await this.con.runAndReadAll(
      sql,
      params ? ([...params] as DuckDBValue[]) : undefined,
    );
    return reader.getRowObjects().map((row) => {
      const out: Record<string, unknown> = {};
      for (const [k, v] of Object.entries(row)) out[k] = toPlain(v);
      return out as T;
    });
  }

  // --- incremental sync bookkeeping -------------------------------------
  async getCursor(source: string, connection: string, entity: string): Promise<string | null> {
    const rows = await this.query<{ cursor: string | null }>(
      "SELECT cursor FROM sync_state WHERE source = ? AND connection = ? AND entity = ?",
      [source, connection, entity],
    );
    const row = rows[0];
    return row ? row.cursor : null;
  }

  async setCursor(c: SyncCursor): Promise<void> {
    await this.con.run(
      `INSERT INTO sync_state
           (source, connection, entity, cursor, last_status, error, last_run_at)
       VALUES (?, ?, ?, ?, ?, ?, CAST(? AS TIMESTAMP))
       ON CONFLICT (source, connection, entity) DO UPDATE SET
           cursor      = EXCLUDED.cursor,
           last_status = EXCLUDED.last_status,
           error       = EXCLUDED.error,
           last_run_at = EXCLUDED.last_run_at`,
      [
        c.source,
        c.connection,
        c.entity,
        c.cursor,
        c.lastStatus ?? "ok",
        c.error ?? null,
        (c.lastRunAt ?? new Date()).toISOString(),
      ],
    );
  }

  // --- dialect surface ---------------------------------------------------
  jsonField(column: string, path: string): string {
    // DuckDB: one JSON-path extractor handles nested paths.
    return `json_extract_string(${column}, '$.${path}')`;
  }
}
