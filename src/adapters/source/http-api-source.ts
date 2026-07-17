/**
 * HttpApiSource — the "live API puller" day-one implementation.
 *
 * A generic REST puller: hit a URL (optionally with a bearer token), map the
 * JSON response into raw records, and compute the next watermark. Real,
 * runnable code (uses global `fetch`); a concrete connector like GitHub is just
 * this configured with a URL + a mapping function.
 *
 * Not exercised in the offline demo (it needs a live endpoint/credentials), but
 * it proves the seam: it implements the SAME `Source` interface as
 * `PreAggregatedSource`, so the collector cannot tell them apart.
 */

import type { Source, SourceBatch, SourceContext } from "../../core/source.js";
import type { RawRecord } from "../../core/storage.js";

export interface HttpApiSourceOptions {
  name: string;
  entity: string;
  /** build the request URL for this fetch (may use the incoming cursor) */
  url: (ctx: SourceContext) => string;
  /** map the parsed JSON body into (naturalKey, payload) pairs */
  map: (body: unknown) => Array<{ naturalKey: string; payload: Record<string, unknown> }>;
  /** derive the next watermark from the records just fetched (default: unchanged) */
  nextCursor?: (records: RawRecord[], ctx: SourceContext) => string | null;
  /** extra static headers (auth is added from ctx.credentials.token) */
  headers?: Record<string, string>;
}

export class HttpApiSource implements Source {
  readonly name: string;
  readonly entity: string;

  constructor(private readonly opts: HttpApiSourceOptions) {
    this.name = opts.name;
    this.entity = opts.entity;
  }

  async fetch(ctx: SourceContext): Promise<SourceBatch> {
    const headers: Record<string, string> = { accept: "application/json", ...this.opts.headers };
    if (ctx.credentials.token) headers.authorization = `Bearer ${ctx.credentials.token}`;

    const res = await fetch(this.opts.url(ctx), { headers });
    if (!res.ok) {
      throw new Error(`${this.name} fetch failed: ${res.status} ${res.statusText}`);
    }
    const body = await res.json();

    const fetchedAt = new Date();
    const records: RawRecord[] = this.opts.map(body).map((item) => ({
      source: this.name,
      entity: this.entity,
      naturalKey: item.naturalKey,
      payload: item.payload,
      fetchedAt,
    }));

    const nextCursor = this.opts.nextCursor?.(records, ctx) ?? ctx.cursor;
    return { records, nextCursor };
  }
}
