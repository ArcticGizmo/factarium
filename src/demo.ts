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
import { getStore } from "./store.js";

// --- 1. collect: pretend a GitHub connector fetched these -------------------
const FAKE_PRS: RawRecord[] = [
  {
    source: "github",
    entity: "pull_request",
    naturalKey: "101",
    payload: {
      number: 101, user: { login: "jane" }, additions: 40,
      created_at: "2026-07-01T09:00:00Z", merged_at: "2026-07-01T15:00:00Z",
    },
    fetchedAt: new Date("2026-07-03T08:00:00Z"),
  },
  {
    source: "github",
    entity: "pull_request",
    naturalKey: "102",
    payload: {
      number: 102, user: { login: "sam" }, additions: 120,
      created_at: "2026-07-02T10:00:00Z", merged_at: "2026-07-04T10:00:00Z",
    },
    fetchedAt: new Date("2026-07-03T08:00:00Z"),
  },
  {
    source: "github",
    entity: "pull_request",
    naturalKey: "103",
    payload: {
      number: 103, user: { login: "jane" }, additions: 12,
      created_at: "2026-07-03T08:00:00Z", merged_at: null, // still open
    },
    fetchedAt: new Date("2026-07-03T08:00:00Z"),
  },
];

interface CycleTimeRow {
  author: string;
  merged_prs: number;
  avg_cycle_hours: number;
}

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
      source: "github", connection: "default", entity: "pull_request",
      cursor: "2026-07-03T08:00:00Z",
    });
    const cursor = await store.getCursor("github", "default", "pull_request");
    console.log(`landed ${n} raw records; cursor now = ${cursor}`);

    // --- aggregate: raw -> source-agnostic core -----------------------
    // Dialect divergence lives ONLY in jsonField(); the rest is portable SQL.
    const jf = (path: string) => store.jsonField("payload", path);
    await store.execute(`
      CREATE OR REPLACE TABLE core_pull_request AS
      SELECT
          natural_key                              AS pr_id,
          ${jf("user.login")}                      AS author,
          CAST(${jf("additions")} AS INTEGER)      AS additions,
          CAST(${jf("created_at")} AS TIMESTAMP)   AS created_at,
          TRY_CAST(${jf("merged_at")} AS TIMESTAMP) AS merged_at
      FROM raw_records
      WHERE source = 'github' AND entity = 'pull_request'
    `);

    // --- shape: a metric, read behind the authorization checkpoint ----
    authz.authorize(ctx, "read", { kind: "metric", name: "pr_cycle_time" });
    const rows = await store.query<CycleTimeRow>(`
      SELECT
          author,
          COUNT(*)                                              AS merged_prs,
          ROUND(AVG(date_diff('hour', created_at, merged_at)), 1) AS avg_cycle_hours
      FROM core_pull_request
      WHERE merged_at IS NOT NULL
      GROUP BY author
      ORDER BY avg_cycle_hours
    `);

    // --- render (stand-in) --------------------------------------------
    console.log(`\nmetric: PR cycle time by author  (requested by ${ctx.principal.displayName})`);
    console.log(`${"author".padEnd(8)} ${"merged_prs".padStart(10)} ${"avg_cycle_hours".padStart(16)}`);
    for (const r of rows) {
      console.log(
        `${r.author.padEnd(8)} ${String(r.merged_prs).padStart(10)} ${String(r.avg_cycle_hours).padStart(16)}`,
      );
    }
  } finally {
    await store.close();
  }
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
