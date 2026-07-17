"""Storage seam package.

The whole point: the app calls get_store(...) and then only ever touches the
StorageEngine interface. Swapping engines is a one-line config change.
"""

from __future__ import annotations

from .engine import RawRecord, StorageEngine, SyncCursor

__all__ = ["RawRecord", "SyncCursor", "StorageEngine", "get_store"]


def get_store(engine: str = "duckdb", **kwargs) -> StorageEngine:
    """Factory. `engine` is the one knob that picks the substrate.

        get_store("duckdb", path="sausage.duckdb")   # default, embedded
        get_store("postgres", dsn="postgresql://…")  # opt-in, at scale
    """
    engine = engine.lower()
    if engine == "duckdb":
        from .duckdb_store import DuckDBStore
        return DuckDBStore(**kwargs)
    if engine in ("postgres", "postgresql", "pg"):
        from .postgres_store import PostgresStore
        return PostgresStore(**kwargs)
    raise ValueError(f"unknown engine: {engine!r} (expected 'duckdb' or 'postgres')")
