/**
 * The storage seam — TypeScript port of the Python spike's `engine.py`.
 *
 * One interface every part of the system talks to. The rest of the app never
 * imports `@duckdb/node-api` (or, later, a Postgres driver) directly — it only
 * knows `StorageEngine`. That is what makes the engine swappable, exactly like a
 * data source is.
 *
 * The four verbs need only this:
 *   collect   -> land()                    (write raw facts, idempotently)
 *   aggregate -> execute() + jsonField()   (re-runnable transforms over raw)
 *   shape     -> query()                   (read metrics out)
 *   render    -> query()                   (dashboards read the same way)
 *   plus       getCursor()/setCursor()     (incremental-sync bookkeeping)
 */

/** One verbatim record from a source, headed for the landing zone. */
export interface RawRecord {
  source: string; // 'github', 'jira', 'claude_code', ...
  entity: string; // 'pull_request', 'issue', 'llm_usage', ...
  /** the source's own id — makes re-syncing idempotent (upsert key) */
  naturalKey: string;
  payload: Record<string, unknown>; // the untouched API response
  fetchedAt: Date;
}

/** Incremental-sync watermark for one (source, connection, entity). */
export interface SyncCursor {
  source: string;
  connection: string;
  entity: string;
  cursor: string | null; // e.g. an ISO timestamp or opaque page token
  lastStatus?: "ok" | "error";
  error?: string | null;
  lastRunAt?: Date;
}

/** The swappable substrate. Implemented by DuckDBStore (and later PostgresStore). */
export interface StorageEngine {
  // --- lifecycle ---------------------------------------------------------
  /** Create the landing zone + sync-state tables if absent. Idempotent. */
  initialize(): Promise<void>;
  close(): Promise<void>;

  // --- collect -----------------------------------------------------------
  /**
   * Upsert raw records into the landing zone. Returns rows written. This is the
   * ONLY write path a connector uses: fetch JSON, build RawRecords, call land().
   */
  land(records: Iterable<RawRecord>): Promise<number>;

  // --- aggregate / shape / render ---------------------------------------
  /** Run DDL / a transformation step. No result set expected. */
  execute(sql: string, params?: readonly unknown[]): Promise<void>;
  /** Run a read query and return rows as plain objects (metrics, dashboards). */
  query<T = Record<string, unknown>>(sql: string, params?: readonly unknown[]): Promise<T[]>;

  // --- incremental sync bookkeeping -------------------------------------
  getCursor(source: string, connection: string, entity: string): Promise<string | null>;
  setCursor(cursor: SyncCursor): Promise<void>;

  // --- dialect surface (where engines genuinely differ) ------------------
  /** parameter placeholder style, e.g. "?" (DuckDB) or "$n"/"%s" (Postgres) */
  readonly paramstyle: string;
  /**
   * SQL fragment extracting a (possibly dotted) JSON path as text. Portable
   * transform SQL calls this instead of hard-coding `->>`, so a staging model
   * reads the same regardless of engine.
   */
  jsonField(column: string, path: string): string;
}
