/**
 * Metric: PR cycle time by author.
 *
 * Mean hours from PR creation to merge, per author, over merged PRs only. Reads
 * the conformed `core_pull_request` model, so it is independent of whether the
 * data arrived from the live GitHub API or a pre-aggregated file.
 *
 * Tested in `test/metrics/pr-cycle-time.test.ts`.
 */

import type { Metric } from "../core/metric.js";

/** One row of the metric, at its declared grain (author). */
export interface CycleTimeRow {
  author: string;
  merged_prs: number;
  avg_cycle_hours: number;
}

export const prCycleTime: Metric = {
  name: "pr_cycle_time",
  version: 1,
  grain: "author",
  description: "Mean hours from PR creation to merge, per author (merged PRs only).",
  sql: `
    SELECT
        author,
        COUNT(*)                                                AS merged_prs,
        ROUND(AVG(date_diff('hour', created_at, merged_at)), 1) AS avg_cycle_hours
    FROM core_pull_request
    WHERE merged_at IS NOT NULL
    GROUP BY author
    ORDER BY avg_cycle_hours, author
  `,
};
