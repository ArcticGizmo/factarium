/**
 * The collect verb through the source seam — proof that a source is a source.
 *
 * This runs the SAME downstream pipeline as demo.ts (buildCorePullRequests +
 * prCycleTimeByAuthor from pipeline.ts), but the raw records arrive via the
 * `Source` seam — here a `PreAggregatedSource` reading an NDJSON fixture — driven
 * by the source-agnostic `collect()` orchestrator. Swap in `HttpApiSource` and
 * nothing below the source changes. Identical numbers to demo.ts is the whole
 * demonstration.
 *
 * Run:  npm run demo:collect
 */

import { fileURLToPath } from "node:url";

import { EnvCredentialProvider } from "./adapters/credentials/env-credential-provider.js";
import { PreAggregatedSource } from "./adapters/source/pre-aggregated-source.js";
import { collect } from "./collect.js";
import { LocalOwnerProvider } from "./core/auth.js";
import { AllowAll } from "./core/authz.js";
import type { RequestContext } from "./core/principal.js";
import { buildCorePullRequests, prCycleTimeByAuthor, printCycleTime } from "./pipeline.js";
import { getStore } from "./store.js";

const FIXTURE = fileURLToPath(new URL("../fixtures/github-pull-requests.ndjson", import.meta.url));

async function main(): Promise<void> {
  const auth = new LocalOwnerProvider();
  const authz = new AllowAll();
  const ctx: RequestContext = { principal: await auth.authenticate() };

  const store = getStore("duckdb", { path: ":memory:" });
  await store.initialize();
  try {
    // --- collect: through the SOURCE SEAM (pre-aggregated file today) ------
    // Swapping this line for `new HttpApiSource({...})` changes nothing else.
    const source = new PreAggregatedSource("github", "pull_request", FIXTURE);
    const credentials = new EnvCredentialProvider();

    const result = await collect(store, source, credentials);
    console.log(
      `collected ${result.landed} records from '${result.source}' ` +
        `(via pre-aggregated source); cursor now = ${result.cursor}`,
    );

    // Idempotency: re-collecting lands the same rows (upsert), no duplicates.
    await collect(store, source, credentials);
    const [{ n }] = await store.query<{ n: number }>("SELECT COUNT(*) AS n FROM raw_records");
    console.log(`re-collected; raw_records still holds ${n} rows (idempotent)`);

    // --- aggregate + shape + render: IDENTICAL to demo.ts ------------------
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
