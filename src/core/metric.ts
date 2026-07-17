/**
 * The metric primitive — the trust layer, as code rather than BI-tool settings.
 *
 * A metric is named, versioned SQL with an explicit grain. Because it is a plain
 * value (not a dashboard config), it can be unit-tested with fixtures: run the
 * SQL over known raw input and assert the exact rows out. That fixture test is
 * what makes a metric trustworthy. See `metrics/*.ts` for definitions and
 * `test/metrics/*.test.ts` for the fixtures.
 *
 * Metrics read from the conformed `core_*` model (built by the aggregate verb),
 * never from raw payloads — so a metric is independent of any one source.
 */

import type { Authorizer } from "./authz.js";
import type { RequestContext } from "./principal.js";
import type { StorageEngine } from "./storage.js";

export interface Metric {
  /** stable identifier, also the authz resource name, e.g. 'pr_cycle_time' */
  readonly name: string;
  /** bump when the definition changes in a way that moves the numbers */
  readonly version: number;
  /** the dimension(s) one row is produced per, e.g. 'author' or 'author, week' */
  readonly grain: string;
  readonly description?: string;
  /** re-runnable read over the core model; one row per grain value */
  readonly sql: string;
}

/**
 * Run a metric behind the authorization checkpoint. The row shape is supplied by
 * the caller (the metric's companion type), keeping `Metric` a plain value.
 */
export async function runMetric<Row>(
  store: StorageEngine,
  ctx: RequestContext,
  authz: Authorizer,
  metric: Metric,
): Promise<Row[]> {
  authz.authorize(ctx, "read", { kind: "metric", name: metric.name });
  return store.query<Row>(metric.sql);
}
