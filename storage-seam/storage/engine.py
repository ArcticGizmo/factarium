"""The storage seam.

One interface every part of the system talks to. The rest of the app never
imports `duckdb` or `psycopg` directly — it only knows `StorageEngine`. That is
what makes the engine swappable, exactly like a data source is.

The interface is deliberately tiny (~6 methods). Everything the four verbs need:

    collect   -> land()                  (write raw facts, idempotently)
    aggregate -> execute() + json_field  (re-runnable transforms over raw)
    shape     -> query()                 (read metrics out)
    render    -> query()                 (dashboards read the same way)
    plus       get_cursor()/set_cursor() (incremental-sync bookkeeping)

Dialect divergences (the "it's not free" part) are confined to the two concrete
adapters and surfaced through `json_field()` and `paramstyle`.
"""

from __future__ import annotations

import abc
from collections.abc import Iterable, Sequence
from dataclasses import dataclass, field
from datetime import datetime, timezone


def _utcnow() -> datetime:
    return datetime.now(timezone.utc)


@dataclass(frozen=True)
class RawRecord:
    """One verbatim record from a source, headed for the landing zone.

    `natural_key` is the source's own id — it makes re-syncing idempotent
    (upsert on (source, entity, natural_key)), so running a sync twice never
    duplicates a row and never re-hits the source for data you already have.
    """

    source: str            # 'github', 'jira', 'claude_code', ...
    entity: str            # 'pull_request', 'issue', 'llm_usage', ...
    natural_key: str       # the source's id for this record
    payload: dict          # the untouched API response
    fetched_at: datetime = field(default_factory=_utcnow)


@dataclass(frozen=True)
class SyncCursor:
    """Incremental-sync watermark for one (source, connection, entity)."""

    source: str
    connection: str
    entity: str
    cursor: str | None            # e.g. an ISO timestamp or opaque page token
    last_status: str = "ok"       # 'ok' | 'error'
    error: str | None = None
    last_run_at: datetime = field(default_factory=_utcnow)


class StorageEngine(abc.ABC):
    """The swappable substrate. Implemented by DuckDBStore and PostgresStore."""

    # --- lifecycle ---------------------------------------------------------
    @abc.abstractmethod
    def initialize(self) -> None:
        """Create the landing zone and sync-state tables if absent. Idempotent."""

    @abc.abstractmethod
    def close(self) -> None:
        ...

    def __enter__(self) -> "StorageEngine":
        self.initialize()
        return self

    def __exit__(self, *exc) -> None:
        self.close()

    # --- collect -----------------------------------------------------------
    @abc.abstractmethod
    def land(self, records: Iterable[RawRecord]) -> int:
        """Upsert raw records into the landing zone. Returns rows written.

        This is the ONLY write path a connector uses. Connectors stay dumb:
        fetch JSON, build RawRecords, call land(). Nothing else.
        """

    # --- aggregate / shape / render ---------------------------------------
    @abc.abstractmethod
    def execute(self, sql: str, params: Sequence | None = None) -> None:
        """Run DDL / a transformation step. No result set expected."""

    @abc.abstractmethod
    def query(self, sql: str, params: Sequence | None = None) -> list[dict]:
        """Run a read query and return rows as dicts (metrics, dashboards)."""

    # --- incremental sync bookkeeping -------------------------------------
    @abc.abstractmethod
    def get_cursor(self, source: str, connection: str, entity: str) -> str | None:
        ...

    @abc.abstractmethod
    def set_cursor(self, cursor: SyncCursor) -> None:
        ...

    # --- dialect surface (where the two engines genuinely differ) ----------
    #: parameter placeholder style, e.g. "?" (DuckDB) or "%s" (psycopg)
    paramstyle: str = "?"

    @staticmethod
    @abc.abstractmethod
    def json_field(column: str, path: str) -> str:
        """Return an SQL fragment extracting a (possibly dotted) JSON path as text.

        Portable transform SQL calls this instead of hard-coding `->>`, so a
        `staging` model reads the same regardless of engine:

            SELECT {json_field('payload', 'user.login')} AS author ...

        This one helper absorbs most of the JSON dialect divergence.
        """
