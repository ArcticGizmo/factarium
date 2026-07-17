/**
 * Shared test helpers: build raw GitHub-PR records without the payload boilerplate.
 */

import type { RawRecord } from "../src/core/storage.js";

export function githubPr(opts: {
  id: string;
  author: string;
  createdAt: string;
  mergedAt?: string | null;
  additions?: number;
}): RawRecord {
  return {
    source: "github",
    entity: "pull_request",
    naturalKey: opts.id,
    payload: {
      number: Number(opts.id),
      user: { login: opts.author },
      additions: opts.additions ?? 0,
      created_at: opts.createdAt,
      merged_at: opts.mergedAt ?? null,
    },
    fetchedAt: new Date("2026-07-03T08:00:00Z"),
  };
}
