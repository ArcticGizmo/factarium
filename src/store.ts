/**
 * Storage factory — the one knob that picks the substrate (port of __init__.py).
 *
 * The app calls getStore(...) once and then only ever touches the StorageEngine
 * interface. Swapping engines is a one-line config change.
 */

import type { StorageEngine } from "./core/storage.js";
import { DuckDBStore } from "./adapters/storage/duckdb-store.js";

export type EngineName = "duckdb" | "postgres";

export function getStore(engine: EngineName = "duckdb", opts: { path?: string } = {}): StorageEngine {
  switch (engine) {
    case "duckdb":
      return new DuckDBStore(opts.path);
    case "postgres":
      // Opt-in, for when a scale signal fires (concurrent users, write
      // contention, data outgrowing the machine). Same interface, later drop-in.
      throw new Error(
        "PostgresStore is not implemented yet. Start on DuckDB; add it only when a " +
          "concrete scale signal fires (see docs/decisions/0001-*.md).",
      );
    default: {
      const _exhaustive: never = engine;
      throw new Error(`unknown engine: ${String(_exhaustive)}`);
    }
  }
}

export type { StorageEngine };
