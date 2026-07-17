/**
 * A slice of the aggregate + shape verbs, shared by the demos.
 *
 * Extracted so BOTH demos run the *identical* downstream code over data that
 * arrived by different means (hardcoded records vs. the source seam). That the
 * numbers come out the same is the demonstration that a source is a source.
 *
 * (This is a thin starting point for WS3/WS4 in the roadmap — a real transform
 * runner and metric registry come later; the SQL here will migrate into those.)
 */

import type { Authorizer } from "./core/authz.js";
import type { RequestContext } from "./core/principal.js";
import type { StorageEngine } from "./core/storage.js";

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

export interface CycleTimeRow {
  author: string;
  merged_prs: number;
  avg_cycle_hours: number;
}

/** shape: PR cycle time by author, read behind the authorization checkpoint. */
export async function prCycleTimeByAuthor(
  store: StorageEngine,
  ctx: RequestContext,
  authz: Authorizer,
): Promise<CycleTimeRow[]> {
  authz.authorize(ctx, "read", { kind: "metric", name: "pr_cycle_time" });
  return store.query<CycleTimeRow>(`
    SELECT
        author,
        COUNT(*)                                                AS merged_prs,
        ROUND(AVG(date_diff('hour', created_at, merged_at)), 1) AS avg_cycle_hours
    FROM core_pull_request
    WHERE merged_at IS NOT NULL
    GROUP BY author
    ORDER BY avg_cycle_hours
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
