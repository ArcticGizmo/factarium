/**
 * Fixture tests for the pr_cycle_time metric — this is what "metrics as tested
 * code" means: known raw input in, exact rows out. If the metric's SQL drifts,
 * these fail.
 */

import assert from "node:assert/strict";
import { test } from "node:test";
import { AllowAll, AuthorizationError, type Authorizer } from "../../src/core/authz.js";
import { runMetric } from "../../src/core/metric.js";
import { LOCAL_OWNER, type RequestContext } from "../../src/core/principal.js";
import type { RawRecord } from "../../src/core/storage.js";
import { type CycleTimeRow, prCycleTime } from "../../src/metrics/pr-cycle-time.js";
import { buildCorePullRequests } from "../../src/pipeline.js";
import { getStore } from "../../src/store.js";
import { githubPr } from "../helpers.js";

const CTX: RequestContext = { principal: LOCAL_OWNER };

async function loadedStore(records: RawRecord[]) {
  const store = getStore("duckdb", { path: ":memory:" });
  await store.initialize();
  await store.land(records);
  await buildCorePullRequests(store);
  return store;
}

test("computes mean create->merge hours per author, ordered by cycle time", async () => {
  const store = await loadedStore([
    githubPr({
      id: "101",
      author: "jane",
      createdAt: "2026-07-01T09:00:00Z",
      mergedAt: "2026-07-01T15:00:00Z",
    }), // 6h
    githubPr({
      id: "102",
      author: "sam",
      createdAt: "2026-07-02T10:00:00Z",
      mergedAt: "2026-07-04T10:00:00Z",
    }), // 48h
  ]);
  try {
    const rows = await runMetric<CycleTimeRow>(store, CTX, new AllowAll(), prCycleTime);
    assert.deepEqual(rows, [
      { author: "jane", merged_prs: 1, avg_cycle_hours: 6 },
      { author: "sam", merged_prs: 1, avg_cycle_hours: 48 },
    ]);
  } finally {
    await store.close();
  }
});

test("averages multiple PRs per author and excludes open (unmerged) PRs", async () => {
  const store = await loadedStore([
    githubPr({
      id: "1",
      author: "jane",
      createdAt: "2026-07-01T00:00:00Z",
      mergedAt: "2026-07-01T06:00:00Z",
    }), // 6h
    githubPr({
      id: "2",
      author: "jane",
      createdAt: "2026-07-02T00:00:00Z",
      mergedAt: "2026-07-02T10:00:00Z",
    }), // 10h
    githubPr({ id: "3", author: "jane", createdAt: "2026-07-03T00:00:00Z", mergedAt: null }), // open -> excluded
  ]);
  try {
    const rows = await runMetric<CycleTimeRow>(store, CTX, new AllowAll(), prCycleTime);
    // mean(6, 10) = 8 over 2 merged PRs; the open PR is not counted.
    assert.deepEqual(rows, [{ author: "jane", merged_prs: 2, avg_cycle_hours: 8 }]);
  } finally {
    await store.close();
  }
});

test("returns no rows when nothing is merged yet", async () => {
  const store = await loadedStore([
    githubPr({ id: "9", author: "sam", createdAt: "2026-07-03T00:00:00Z", mergedAt: null }),
  ]);
  try {
    const rows = await runMetric<CycleTimeRow>(store, CTX, new AllowAll(), prCycleTime);
    assert.deepEqual(rows, []);
  } finally {
    await store.close();
  }
});

test("is read through the authorization checkpoint (a deny policy blocks it)", async () => {
  const denyAll: Authorizer = {
    authorize(ctx, action, resource) {
      throw new AuthorizationError(action, resource, ctx.principal.id);
    },
  };
  const store = await loadedStore([
    githubPr({
      id: "1",
      author: "jane",
      createdAt: "2026-07-01T00:00:00Z",
      mergedAt: "2026-07-01T06:00:00Z",
    }),
  ]);
  try {
    await assert.rejects(
      () => runMetric<CycleTimeRow>(store, CTX, denyAll, prCycleTime),
      AuthorizationError,
    );
  } finally {
    await store.close();
  }
});
