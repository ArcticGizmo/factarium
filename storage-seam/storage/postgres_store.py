"""Postgres adapter — STUB (opt-in, for when a scale signal fires).

Intentionally not implemented. Its job here is to prove the seam is thin: every
method has the SAME signature as DuckDBStore, and the comments mark exactly where
the dialect diverges. Fill it in the day write-contention, concurrent users, or
data size demands a server — not before.

When you implement it: pip install psycopg[binary]
"""

from __future__ import annotations

from collections.abc import Iterable, Sequence

from .engine import RawRecord, StorageEngine, SyncCursor

# The DDL is nearly identical to DuckDB's — the only real changes are:
#   JSON      -> JSONB          (Postgres's indexable binary JSON)
#   now()      is the same
# so schema portability is high; the divergence is in DML, below.
_DDL = """
CREATE TABLE IF NOT EXISTS raw_records (
    source       TEXT        NOT NULL,
    entity       TEXT        NOT NULL,
    natural_key  TEXT        NOT NULL,
    payload      JSONB       NOT NULL,
    fetched_at   TIMESTAMPTZ NOT NULL,
    ingested_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (source, entity, natural_key)
);
-- plus sync_state, identical shape ...
"""


class PostgresStore(StorageEngine):
    paramstyle = "%s"   # <-- divergence #1: psycopg uses %s, not ?

    def __init__(self, dsn: str):
        # import psycopg; self._con = psycopg.connect(dsn)
        raise NotImplementedError(
            "PostgresStore is a stub. Start on DuckDBStore; implement this only "
            "when a concrete scale signal fires (see duckdb-vs-postgres.md)."
        )

    def initialize(self) -> None:
        # self._con.execute(_DDL); self._con.commit()
        raise NotImplementedError

    def close(self) -> None:
        raise NotImplementedError

    def land(self, records: Iterable[RawRecord]) -> int:
        # Divergence #2: params are %s, and JSON is cast %s::jsonb (not CAST AS JSON).
        # INSERT ... VALUES (%s, %s, %s, %s::jsonb, %s, now())
        # ON CONFLICT (source, entity, natural_key) DO UPDATE SET ...
        # ^ ON CONFLICT syntax itself is identical — Postgres is the reference impl.
        raise NotImplementedError

    def execute(self, sql: str, params: Sequence | None = None) -> None:
        raise NotImplementedError

    def query(self, sql: str, params: Sequence | None = None) -> list[dict]:
        # Use psycopg.rows.dict_row so query() returns list[dict] like DuckDB's.
        raise NotImplementedError

    def get_cursor(self, source: str, connection: str, entity: str) -> str | None:
        raise NotImplementedError

    def set_cursor(self, c: SyncCursor) -> None:
        raise NotImplementedError

    @staticmethod
    def json_field(column: str, path: str) -> str:
        # Divergence #3: Postgres has no single dotted-path extractor for text.
        # Chain -> for every segment, then ->> for the last one.
        #   'a.b.c'  ->  column->'a'->'b'->>'c'
        segments = path.split(".")
        *heads, last = segments
        expr = column
        for h in heads:
            expr += f"->'{h}'"
        expr += f"->>'{last}'"
        return expr
