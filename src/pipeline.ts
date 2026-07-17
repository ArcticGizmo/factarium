/**
 * A slice of the aggregate + render verbs, shared by the demos.
 *
 * The *shape* verb now lives in `metrics/` as tested `Metric` values, run via
 * `runMetric()`. What remains here is the transform (raw -> conformed core) and
 * a render stand-in. Both demos call these so they run identical downstream code
 * over data that arrived by different means — the source-seam proof.
 *
 * (Thin starting point for WS3 in the roadmap — a real transform runner comes
 * later; this SQL will migrate into it.)
 */

import type { StorageEngine } from "./core/storage.js";
import type { CycleTimeRow } from "./metrics/pr-cycle-time.js";

/** aggregate: raw GitHub PRs -> a source-agnostic core table. Re-runnable. */
export async function buildCorePullRequests(store: StorageEngine): Promise<void> {
  const jf = (path: string) => store.jsonField("payload", path);
  await store.execute(`
    CREATE OR REPLACE TABLE core_pull_request AS
    SELECT
        natural_key                               AS pr_id,
        ${jf("user.login")}                       AS author,
        CAST(${jf("additions")} AS INTEGER)       AS additions,
        CAST(${jf("created_at")} AS TIMESTAMP)    AS created_at,
        TRY_CAST(${jf("merged_at")} AS TIMESTAMP) AS merged_at
    FROM raw_records
    WHERE source = 'github' AND entity = 'pull_request'
  `);
}

/** render stand-in: print a cycle-time table. A real renderer runs the query. */
export function printCycleTime(rows: CycleTimeRow[], requestedBy: string): void {
  console.log(`\nmetric: PR cycle time by author  (requested by ${requestedBy})`);
  console.log(
    `${"author".padEnd(8)} ${"merged_prs".padStart(10)} ${"avg_cycle_hours".padStart(16)}`,
  );
  for (const r of rows) {
    console.log(
      `${r.author.padEnd(8)} ${String(r.merged_prs).padStart(10)} ${String(r.avg_cycle_hours).padStart(16)}`,
    );
  }
}
