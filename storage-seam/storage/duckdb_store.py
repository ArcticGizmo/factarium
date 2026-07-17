"""DuckDB adapter — the default engine.

Embedded (in-process), zero setup, columnar/OLAP — fast at the read-out workload
a metrics system actually runs. This is a real, runnable implementation.

Install: pip install duckdb
"""

from __future__ import annotations

import json
from collections.abc import Iterable, Sequence

import duckdb

from .engine import RawRecord, StorageEngine, SyncCursor

_DDL = """
CREATE TABLE IF NOT EXISTS raw_records (
    source       VARCHAR   NOT NULL,
    entity       VARCHAR   NOT NULL,
    natural_key  VARCHAR   NOT NULL,
    payload      JSON      NOT NULL,
    fetched_at   TIMESTAMP NOT NULL,
    ingested_at  TIMESTAMP NOT NULL DEFAULT now(),
    PRIMARY KEY (source, entity, natural_key)
);

CREATE TABLE IF NOT EXISTS sync_state (
    source       VARCHAR NOT NULL,
    connection   VARCHAR NOT NULL,
    entity       VARCHAR NOT NULL,
    cursor       VARCHAR,
    last_status  VARCHAR,
    error        VARCHAR,
    last_run_at  TIMESTAMP,
    PRIMARY KEY (source, connection, entity)
);
"""


class DuckDBStore(StorageEngine):
    paramstyle = "?"

    def __init__(self, path: str = "sausage.duckdb"):
        # ":memory:" for tests/demo; a file path for persistence.
        self._con = duckdb.connect(path)

    # --- lifecycle ---------------------------------------------------------
    def initialize(self) -> None:
        self._con.execute(_DDL)

    def close(self) -> None:
        self._con.close()

    # --- collect -----------------------------------------------------------
    def land(self, records: Iterable[RawRecord]) -> int:
        # DuckDB dialect: qmark params, CAST(? AS JSON), ON CONFLICT DO UPDATE.
        sql = """
            INSERT INTO raw_records
                (source, entity, natural_key, payload, fetched_at, ingested_at)
            VALUES (?, ?, ?, CAST(? AS JSON), ?, now())
            ON CONFLICT (source, entity, natural_key) DO UPDATE SET
                payload     = EXCLUDED.payload,
                fetched_at  = EXCLUDED.fetched_at,
                ingested_at = now();
        """
        n = 0
        for r in records:
            self._con.execute(
                sql,
                [r.source, r.entity, r.natural_key, json.dumps(r.payload), r.fetched_at],
            )
            n += 1
        return n

    # --- aggregate / shape / render ---------------------------------------
    def execute(self, sql: str, params: Sequence | None = None) -> None:
        self._con.execute(sql, list(params) if params else None)

    def query(self, sql: str, params: Sequence | None = None) -> list[dict]:
        cur = self._con.execute(sql, list(params) if params else None)
        cols = [d[0] for d in cur.description]
        return [dict(zip(cols, row)) for row in cur.fetchall()]

    # --- incremental sync bookkeeping -------------------------------------
    def get_cursor(self, source: str, connection: str, entity: str) -> str | None:
        rows = self.query(
            "SELECT cursor FROM sync_state "
            "WHERE source = ? AND connection = ? AND entity = ?",
            [source, connection, entity],
        )
        return rows[0]["cursor"] if rows else None

    def set_cursor(self, c: SyncCursor) -> None:
        self._con.execute(
            """
            INSERT INTO sync_state
                (source, connection, entity, cursor, last_status, error, last_run_at)
            VALUES (?, ?, ?, ?, ?, ?, ?)
            ON CONFLICT (source, connection, entity) DO UPDATE SET
                cursor      = EXCLUDED.cursor,
                last_status = EXCLUDED.last_status,
                error       = EXCLUDED.error,
                last_run_at = EXCLUDED.last_run_at;
            """,
            [c.source, c.connection, c.entity, c.cursor,
             c.last_status, c.error, c.last_run_at],
        )

    # --- dialect surface ---------------------------------------------------
    @staticmethod
    def json_field(column: str, path: str) -> str:
        # DuckDB: one JSON-path extractor handles nested paths.
        return f"json_extract_string({column}, '$.{path}')"
