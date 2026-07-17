/**
 * The collector — source-agnostic incremental sync, in one place.
 *
 * This is the ENTIRE "collect" verb: read the watermark, ask the source for new
 * records, land them, advance the watermark. It never imports a concrete source
 * or engine — only the `Source`, `StorageEngine`, and `CredentialProvider`
 * seams. Swap the source (live API ↔ pre-aggregated file) and not a line here
 * changes; that is the federation escape hatch working as designed.
 *
 * Collection is an owner-initiated write path, so it does not run through the
 * authorization checkpoint (which guards reads/render). See ADR 0001.
 */

import type { CredentialProvider, Source } from "./core/source.js";
import type { StorageEngine } from "./core/storage.js";

export interface CollectResult {
  source: string;
  entity: string;
  connection: string;
  landed: number;
  cursor: string | null;
}

export async function collect(
  store: StorageEngine,
  source: Source,
  credentials: CredentialProvider,
  connection = "default",
): Promise<CollectResult> {
  const cursor = await store.getCursor(source.name, connection, source.entity);
  const creds = await credentials.for(source.name, connection);

  let nextCursor = cursor;
  let status: "ok" | "error" = "ok";
  let error: string | null = null;
  let landed = 0;

  try {
    const batch = await source.fetch({ connection, cursor, credentials: creds });
    landed = await store.land(batch.records);
    nextCursor = batch.nextCursor ?? cursor;
  } catch (err) {
    status = "error";
    error = err instanceof Error ? err.message : String(err);
    throw err;
  } finally {
    // Record the run either way — watermark only advances on success.
    await store.setCursor({
      source: source.name,
      connection,
      entity: source.entity,
      cursor: nextCursor,
      lastStatus: status,
      error,
      lastRunAt: new Date(),
    });
  }

  return { source: source.name, entity: source.entity, connection, landed, cursor: nextCursor };
}
