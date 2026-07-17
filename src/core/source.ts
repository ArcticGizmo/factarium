/**
 * The source seam — the make-or-break rule from the README.
 *
 * A source does exactly ONE thing: given a watermark and credentials, produce
 * raw records. It is blind to storage, transforms, metrics, and rendering. The
 * hard constraint that keeps the federation escape hatch open: a live GitHub API
 * puller and a "read already-aggregated rows from a file/URL" source must be
 * INDISTINGUISHABLE to everything downstream. Both implement this one interface;
 * the collector (`collect.ts`) treats them identically.
 *
 * One `Source` == one `(name, entity)` stream, which lines up with the
 * incremental-sync cursor keyed on (source, connection, entity).
 */

import type { RawRecord } from "./storage.js";

/** Outbound credentials a source uses to call its external system. */
export interface SourceCredentials {
  /** bearer token / PAT, when the source needs one */
  token?: string;
  [k: string]: string | undefined;
}

/** What the collector hands a source for one fetch. */
export interface SourceContext {
  /** which named connection (e.g. 'default'), so one source can have many */
  connection: string;
  /** last watermark persisted for this stream, or null on first sync */
  cursor: string | null;
  /** resolved outbound credentials (see CredentialProvider) */
  credentials: SourceCredentials;
}

/** The result of one fetch: records to land + the new watermark. */
export interface SourceBatch {
  records: RawRecord[];
  /** watermark to persist AFTER these records land; null = leave unchanged */
  nextCursor: string | null;
}

export interface Source {
  /** matches `RawRecord.source`, e.g. 'github' */
  readonly name: string;
  /** matches `RawRecord.entity` and the cursor key, e.g. 'pull_request' */
  readonly entity: string;
  /** fetch new records since `ctx.cursor`. Pagination, if any, is internal. */
  fetch(ctx: SourceContext): Promise<SourceBatch>;
}

/**
 * The credential seam (remote-readiness item 4 from ADR 0001). Locally, creds
 * come from env/config; on a shared remote instance this becomes a per-owner
 * secrets store — without any source having to change.
 */
export interface CredentialProvider {
  for(sourceName: string, connection: string): Promise<SourceCredentials>;
}
