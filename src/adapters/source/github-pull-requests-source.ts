/**
 * GitHubPullRequestsSource — the first real connector (WS2-P2).
 *
 * Pulls pull requests for one repo from the GitHub REST API and emits them as
 * raw records. It implements the same `Source` seam as PreAggregatedSource, so
 * the collector treats live GitHub and a pre-aggregated file identically.
 *
 * - Works unauthenticated for public repos (60 req/hr); a token from the
 *   credential seam (ctx.credentials.token) raises the limit and unlocks private
 *   repos. No token is stored anywhere — it only ever arrives via ctx.
 * - Incremental: PRs are fetched newest-updated first; we stop once we dip to or
 *   below the stored `updated_at` watermark, so a re-sync fetches only changes.
 * - Bounded: `maxPages` caps a single run and we WARN (never silently truncate)
 *   if the cap is hit.
 *
 * The list endpoint does not include additions/deletions (those are detail-only);
 * that's fine — the pr_cycle_time metric uses created_at/merged_at/author.
 */

import type { Source, SourceBatch, SourceContext } from "../../core/source.js";
import type { RawRecord } from "../../core/storage.js";

interface GitHubPull {
  number: number;
  updated_at: string;
  [k: string]: unknown;
}

export interface GitHubPullRequestsOptions {
  owner: string;
  repo: string;
  /** page size (GitHub max 100) */
  perPage?: number;
  /** safety cap on pages fetched per run */
  maxPages?: number;
  /** API root; override for GitHub Enterprise or tests */
  baseUrl?: string;
  /** injectable fetch, for deterministic tests */
  fetchImpl?: typeof fetch;
}

export class GitHubPullRequestsSource implements Source {
  readonly name = "github";
  readonly entity = "pull_request";

  constructor(private readonly opts: GitHubPullRequestsOptions) {}

  async fetch(ctx: SourceContext): Promise<SourceBatch> {
    const perPage = this.opts.perPage ?? 100;
    const maxPages = this.opts.maxPages ?? 10;
    const baseUrl = this.opts.baseUrl ?? "https://api.github.com";
    const doFetch = this.opts.fetchImpl ?? fetch;
    const { owner, repo } = this.opts;
    const fetchedAt = new Date();

    const records: RawRecord[] = [];
    let newestCursor = ctx.cursor;
    let reachedWatermark = false;
    let page = 1;

    for (; page <= maxPages; page++) {
      const url =
        `${baseUrl}/repos/${owner}/${repo}/pulls` +
        `?state=all&sort=updated&direction=desc&per_page=${perPage}&page=${page}`;

      const headers: Record<string, string> = {
        accept: "application/vnd.github+json",
        "user-agent": "factarium", // GitHub rejects requests without a User-Agent
        "x-github-api-version": "2022-11-28",
      };
      if (ctx.credentials.token) headers.authorization = `Bearer ${ctx.credentials.token}`;

      const res = await doFetch(url, { headers });
      if (!res.ok) {
        throw new Error(
          `github pulls fetch failed: ${res.status} ${res.statusText} ` +
            `(${owner}/${repo}, page ${page})`,
        );
      }

      const batch = (await res.json()) as GitHubPull[];
      if (batch.length === 0) break;

      for (const pr of batch) {
        // Sorted by updated desc: at/under the watermark means we've seen the rest.
        if (ctx.cursor && pr.updated_at <= ctx.cursor) {
          reachedWatermark = true;
          break;
        }
        records.push({
          source: this.name,
          entity: this.entity,
          naturalKey: String(pr.number),
          payload: pr as Record<string, unknown>,
          fetchedAt,
        });
        if (!newestCursor || pr.updated_at > newestCursor) newestCursor = pr.updated_at;
      }

      if (reachedWatermark || batch.length < perPage) break;
    }

    if (page > maxPages && !reachedWatermark) {
      console.warn(
        `[github] hit maxPages=${maxPages} for ${owner}/${repo}; ` +
          "older PRs beyond the cap were not fetched this run.",
      );
    }

    return { records, nextCursor: newestCursor };
  }
}
