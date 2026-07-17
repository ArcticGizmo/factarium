/**
 * PreAggregatedSource — the "read already-aggregated rows from a file/URL"
 * day-one implementation, and the federation escape hatch made concrete.
 *
 * It reads newline-delimited JSON records (one object per line) from a local
 * file or an http(s) URL and emits them as raw records. Downstream — collector,
 * transforms, metrics, renderer — cannot tell this apart from a live API: same
 * `Source` interface, same `RawRecord`s. That is the whole point (README rule:
 * a live API and a pre-aggregated rollup must be indistinguishable).
 *
 * Line format (fields other than `payload` are optional):
 *   {"naturalKey":"101","fetchedAt":"2026-07-03T08:00:00Z","payload":{...}}
 */

import { readFile } from "node:fs/promises";
import type { Source, SourceBatch, SourceContext } from "../../core/source.js";
import type { RawRecord } from "../../core/storage.js";

interface Line {
  naturalKey: string;
  payload: Record<string, unknown>;
  fetchedAt?: string;
}

export class PreAggregatedSource implements Source {
  constructor(
    readonly name: string,
    readonly entity: string,
    /** local path, or an http(s):// URL */
    private readonly location: string,
  ) {}

  private async readRaw(): Promise<string> {
    if (/^https?:\/\//i.test(this.location)) {
      const res = await fetch(this.location);
      if (!res.ok) {
        throw new Error(`${this.name} fetch failed: ${res.status} ${res.statusText}`);
      }
      return res.text();
    }
    return readFile(this.location, "utf8");
  }

  async fetch(_ctx: SourceContext): Promise<SourceBatch> {
    const text = await this.readRaw();
    const lines = text.split(/\r?\n/).filter((l) => l.trim().length > 0);

    const records: RawRecord[] = lines.map((line) => {
      const row = JSON.parse(line) as Line;
      return {
        source: this.name,
        entity: this.entity,
        naturalKey: row.naturalKey,
        payload: row.payload,
        fetchedAt: row.fetchedAt ? new Date(row.fetchedAt) : new Date(),
      };
    });

    // Watermark: the newest fetchedAt we saw (a real rollup would carry its own).
    const nextCursor = records.reduce<string | null>((max, r) => {
      const iso = r.fetchedAt.toISOString();
      return max === null || iso > max ? iso : max;
    }, null);

    return { records, nextCursor };
  }
}
