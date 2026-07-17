/**
 * Storage adapter suite. Written against the `StorageEngine` seam via getStore()
 * so the same assertions can later run against a Postgres adapter (WS1 P3 / WS9).
 */

import assert from "node:assert/strict";
import { test } from "node:test";

import type { StorageEngine } from "../../src/core/storage.js";
import { getStore } from "../../src/store.js";
import { githubPr } from "../helpers.js";

async function freshStore(): Promise<StorageEngine> {
  const store = getStore("duckdb", { path: ":memory:" });
  await store.initialize();
  return store;
}

test("land() writes raw records that query() reads back", async () => {
  const store = await freshStore();
  try {
    const written = await store.land([
      githubPr({ id: "1", author: "jane", createdAt: "2026-07-01T00:00:00Z" }),
      githubPr({ id: "2", author: "sam", createdAt: "2026-07-02T00:00:00Z" }),
    ]);
    assert.equal(written, 2);

    const [{ n }] = await store.query<{ n: number }>("SELECT COUNT(*) AS n FROM raw_records");
    assert.equal(n, 2);
  } finally {
    await store.close();
  }
});

test("land() is idempotent — re-landing the same natural key upserts, not duplicates", async () => {
  const store = await freshStore();
  try {
    await store.land([
      githubPr({ id: "1", author: "jane", createdAt: "2026-07-01T00:00:00Z", additions: 10 }),
    ]);
    // Same natural key, changed payload -> one row, updated.
    await store.land([
      githubPr({ id: "1", author: "jane", createdAt: "2026-07-01T00:00:00Z", additions: 99 }),
    ]);

    const rows = await store.query<{ n: number; additions: number }>(
      "SELECT COUNT(*) AS n, MAX(CAST(payload->>'additions' AS INTEGER)) AS additions FROM raw_records",
    );
    assert.equal(rows[0]?.n, 1);
    assert.equal(rows[0]?.additions, 99);
  } finally {
    await store.close();
  }
});

test("cursor round-trips; unknown stream reads back null", async () => {
  const store = await freshStore();
  try {
    assert.equal(await store.getCursor("github", "default", "pull_request"), null);

    await store.setCursor({
      source: "github",
      connection: "default",
      entity: "pull_request",
      cursor: "2026-07-03T08:00:00Z",
    });
    assert.equal(
      await store.getCursor("github", "default", "pull_request"),
      "2026-07-03T08:00:00Z",
    );

    // A different stream is still null (keys are (source, connection, entity)).
    assert.equal(await store.getCursor("github", "default", "issue"), null);
  } finally {
    await store.close();
  }
});

test("jsonField() extracts nested dotted paths", async () => {
  const store = await freshStore();
  try {
    await store.land([githubPr({ id: "1", author: "jane", createdAt: "2026-07-01T00:00:00Z" })]);
    const rows = await store.query<{ author: string }>(
      `SELECT ${store.jsonField("payload", "user.login")} AS author FROM raw_records`,
    );
    assert.equal(rows[0]?.author, "jane");
  } finally {
    await store.close();
  }
});
