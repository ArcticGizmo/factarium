/**
 * Connector tests with an injected fake fetch — no network, deterministic.
 * Covers mapping, incremental watermark stop, and pagination.
 */

import assert from "node:assert/strict";
import { test } from "node:test";

import { GitHubPullRequestsSource } from "../../src/adapters/source/github-pull-requests-source.js";
import type { SourceContext } from "../../src/core/source.js";

function pull(number: number, updated: string) {
  return { number, updated_at: updated, user: { login: "octocat" } };
}

/** A fake fetch that serves canned pages keyed by the `page` query param. */
function fakeFetch(pagesByNumber: Record<number, unknown[]>): typeof fetch {
  return (async (input: string | URL) => {
    const url = new URL(String(input));
    const page = Number(url.searchParams.get("page") ?? "1");
    const body = pagesByNumber[page] ?? [];
    return new Response(JSON.stringify(body), {
      status: 200,
      headers: { "content-type": "application/json" },
    });
  }) as typeof fetch;
}

const ctx = (cursor: string | null): SourceContext => ({
  connection: "default",
  cursor,
  credentials: {},
});

test("first sync maps PRs and sets the watermark to the newest updated_at", async () => {
  const source = new GitHubPullRequestsSource({
    owner: "o",
    repo: "r",
    fetchImpl: fakeFetch({
      1: [pull(3, "2026-07-03T00:00:00Z"), pull(2, "2026-07-02T00:00:00Z")],
    }),
  });

  const batch = await source.fetch(ctx(null));

  assert.equal(batch.records.length, 2);
  assert.deepEqual(
    batch.records.map((r) => r.naturalKey),
    ["3", "2"],
  );
  assert.equal(batch.records[0]?.source, "github");
  assert.equal(batch.records[0]?.entity, "pull_request");
  assert.equal(batch.nextCursor, "2026-07-03T00:00:00Z");
});

test("incremental: stops at the watermark and returns nothing when unchanged", async () => {
  const source = new GitHubPullRequestsSource({
    owner: "o",
    repo: "r",
    fetchImpl: fakeFetch({
      1: [pull(3, "2026-07-03T00:00:00Z"), pull(2, "2026-07-02T00:00:00Z")],
    }),
  });

  const batch = await source.fetch(ctx("2026-07-03T00:00:00Z"));

  assert.equal(batch.records.length, 0);
  // Watermark unchanged (still the newest we know about).
  assert.equal(batch.nextCursor, "2026-07-03T00:00:00Z");
});

test("incremental: returns only PRs newer than the watermark", async () => {
  const source = new GitHubPullRequestsSource({
    owner: "o",
    repo: "r",
    fetchImpl: fakeFetch({
      1: [pull(4, "2026-07-04T00:00:00Z"), pull(3, "2026-07-03T00:00:00Z")],
    }),
  });

  const batch = await source.fetch(ctx("2026-07-03T00:00:00Z"));

  assert.deepEqual(
    batch.records.map((r) => r.naturalKey),
    ["4"],
  );
  assert.equal(batch.nextCursor, "2026-07-04T00:00:00Z");
});

test("paginates until a short page ends the run", async () => {
  const source = new GitHubPullRequestsSource({
    owner: "o",
    repo: "r",
    perPage: 2,
    fetchImpl: fakeFetch({
      1: [pull(5, "2026-07-05T00:00:00Z"), pull(4, "2026-07-04T00:00:00Z")],
      2: [pull(3, "2026-07-03T00:00:00Z")], // short page -> stop
    }),
  });

  const batch = await source.fetch(ctx(null));

  assert.deepEqual(
    batch.records.map((r) => r.naturalKey),
    ["5", "4", "3"],
  );
  assert.equal(batch.nextCursor, "2026-07-05T00:00:00Z");
});

test("throws a clear error on a non-OK response", async () => {
  const source = new GitHubPullRequestsSource({
    owner: "o",
    repo: "r",
    fetchImpl: (async () =>
      new Response("rate limited", { status: 403, statusText: "Forbidden" })) as typeof fetch,
  });

  await assert.rejects(() => source.fetch(ctx(null)), /403/);
});
