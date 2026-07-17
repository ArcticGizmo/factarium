/**
 * Live end-to-end: sync real PRs from GitHub through the whole pipeline.
 *
 *   collect  -> GitHubPullRequestsSource pulls PRs from the live API
 *   aggregate-> the same transform builds core_pull_request
 *   shape    -> the tested pr_cycle_time metric
 *   (render) -> printed
 *
 * Runs unauthenticated against a public repo (fine for a demo). Set
 * FACTARIUM_GITHUB_DEFAULT_TOKEN in the environment to raise the rate limit or
 * reach private repos — the credential seam picks it up automatically.
 *
 * Run:  npm run demo:github
 */

import { EnvCredentialProvider } from "./adapters/credentials/env-credential-provider.js";
import { GitHubPullRequestsSource } from "./adapters/source/github-pull-requests-source.js";
import { collect } from "./collect.js";
import { LocalOwnerProvider } from "./core/auth.js";
import { AllowAll } from "./core/authz.js";
import { runMetric } from "./core/metric.js";
import type { RequestContext } from "./core/principal.js";
import { type CycleTimeRow, prCycleTime } from "./metrics/pr-cycle-time.js";
import { buildCorePullRequests, printCycleTime } from "./pipeline.js";
import { getStore } from "./store.js";

// The configured target. In a real deployment this comes from config (WS7).
const OWNER = "ArcticGizmo";
const REPO = "perch";

async function main(): Promise<void> {
  const auth = new LocalOwnerProvider();
  const authz = new AllowAll();
  const ctx: RequestContext = { principal: await auth.authenticate() };

  const store = getStore("duckdb", { path: ":memory:" });
  await store.initialize();
  try {
    // --- collect: live GitHub API, through the source seam ----------------
    const source = new GitHubPullRequestsSource({ owner: OWNER, repo: REPO });
    const credentials = new EnvCredentialProvider();

    const first = await collect(store, source, credentials);
    console.log(
      `synced ${first.landed} PRs from ${OWNER}/${REPO} (live GitHub API); ` +
        `watermark now = ${first.cursor}`,
    );

    // Incremental: a second sync fetches only what changed since the watermark.
    const second = await collect(store, source, credentials);
    console.log(`re-synced: ${second.landed} new/changed PRs (incremental)`);

    // --- aggregate + shape + render: identical to the other demos ---------
    await buildCorePullRequests(store);
    const rows = await runMetric<CycleTimeRow>(store, ctx, authz, prCycleTime);
    printCycleTime(rows, ctx.principal.displayName);
  } finally {
    await store.close();
  }
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
