/**
 * The whole loop, through the seams, in one file — the TS counterpart of the
 * Python spike's demo.py, plus identity threaded through and the authorization
 * checkpoint consulted before the metric is read.
 *
 *   collect  -> land() a couple of fake GitHub PRs into the raw landing zone
 *   aggregate-> a transform builds a source-agnostic core table
 *   shape    -> a metric query reads a number out (behind the authz checkpoint)
 *   (render) -> printed here; a real renderer runs the same query()
 *
 * Run:  npm run demo
 */

import { LocalOwnerProvider } from "./core/auth.js";
import { AllowAll } from "./core/authz.js";
import type { RequestContext } from "./core/principal.js";
import type { RawRecord } from "./core/storage.js";
import { buildCorePullRequests, prCycleTimeByAuthor, printCycleTime } from "./pipeline.js";
import { getStore } from "./store.js";

// --- 1. collect: pretend a GitHub connector fetched these -------------------
const FAKE_PRS: RawRecord[] = [
  {
    source: "github",
    entity: "pull_request",
    naturalKey: "101",
    payload: {
      number: 101,
      user: { login: "jane" },
      additions: 40,
      created_at: "2026-07-01T09:00:00Z",
      merged_at: "2026-07-01T15:00:00Z",
    },
    fetchedAt: new Date("2026-07-03T08:00:00Z"),
  },
  {
    source: "github",
    entity: "pull_request",
    naturalKey: "102",
    payload: {
      number: 102,
      user: { login: "sam" },
      additions: 120,
      created_at: "2026-07-02T10:00:00Z",
      merged_at: "2026-07-04T10:00:00Z",
    },
    fetchedAt: new Date("2026-07-03T08:00:00Z"),
  },
  {
    source: "github",
    entity: "pull_request",
    naturalKey: "103",
    payload: {
      number: 103,
      user: { login: "jane" },
      additions: 12,
      created_at: "2026-07-03T08:00:00Z",
      merged_at: null, // still open
    },
    fetchedAt: new Date("2026-07-03T08:00:00Z"),
  },
];

async function main(): Promise<void> {
  // Who is asking. Locally this is always the owner; remotely the AuthProvider
  // would resolve a real Principal from a token/cookie. Nothing below changes.
  const auth = new LocalOwnerProvider();
  const authz = new AllowAll();
  const ctx: RequestContext = { principal: await auth.authenticate() };

  // One knob picks the substrate. Change to "postgres" later — nothing else moves.
  const store = getStore("duckdb", { path: ":memory:" });
  await store.initialize();
  try {
    // --- collect -------------------------------------------------------
    const n = await store.land(FAKE_PRS);
    await store.setCursor({
      source: "github",
      connection: "default",
      entity: "pull_request",
      cursor: "2026-07-03T08:00:00Z",
    });
    const cursor = await store.getCursor("github", "default", "pull_request");
    console.log(`landed ${n} raw records; cursor now = ${cursor}`);

    // --- aggregate + shape + render: shared with demo-collect.ts -------
    // demo-collect.ts runs these SAME functions over data that arrived through
    // the source seam — identical output there is the point of the seam.
    await buildCorePullRequests(store);
    const rows = await prCycleTimeByAuthor(store, ctx, authz);
    printCycleTime(rows, ctx.principal.displayName);
  } finally {
    await store.close();
  }
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
