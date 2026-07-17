"""The whole loop, through the seam, in one file.

    collect  -> land() a couple of fake GitHub PRs into the raw landing zone
    aggregate-> a transform builds a source-agnostic core table
    shape    -> a metric query reads a number out
    (render) -> printed here; a real renderer would run the same query()

Run:  pip install duckdb && python demo.py

Note: this has NOT been executed here — it's a reference spike. If a call
signature is off against your installed duckdb version, it'll surface on first run.
"""

from __future__ import annotations

from datetime import datetime, timezone

from storage import RawRecord, SyncCursor, get_store

UTC = timezone.utc

# --- 1. collect: pretend a GitHub connector fetched these -------------------
FAKE_PRS = [
    RawRecord("github", "pull_request", "101",
              {"number": 101, "user": {"login": "jane"}, "additions": 40,
               "created_at": "2026-07-01T09:00:00Z", "merged_at": "2026-07-01T15:00:00Z"}),
    RawRecord("github", "pull_request", "102",
              {"number": 102, "user": {"login": "sam"}, "additions": 120,
               "created_at": "2026-07-02T10:00:00Z", "merged_at": "2026-07-04T10:00:00Z"}),
    RawRecord("github", "pull_request", "103",
              {"number": 103, "user": {"login": "jane"}, "additions": 12,
               "created_at": "2026-07-03T08:00:00Z", "merged_at": None}),  # still open
]


def main() -> None:
    # One knob picks the substrate. Change to "postgres" later — nothing below moves.
    with get_store("duckdb", path=":memory:") as store:

        # --- collect -------------------------------------------------------
        n = store.land(FAKE_PRS)
        store.set_cursor(SyncCursor("github", "default", "pull_request",
                                    cursor="2026-07-03T08:00:00Z"))
        print(f"landed {n} raw records; cursor now = "
              f"{store.get_cursor('github', 'default', 'pull_request')}")

        # --- aggregate: raw -> source-agnostic core -----------------------
        # Dialect divergence lives ONLY in json_field(); the rest is portable SQL.
        jf = store.json_field
        store.execute(f"""
            CREATE OR REPLACE TABLE core_pull_request AS
            SELECT
                natural_key                              AS pr_id,
                {jf('payload', 'user.login')}            AS author,
                CAST({jf('payload', 'additions')} AS INTEGER) AS additions,
                CAST({jf('payload', 'created_at')} AS TIMESTAMP) AS created_at,
                TRY_CAST({jf('payload', 'merged_at')} AS TIMESTAMP) AS merged_at
            FROM raw_records
            WHERE source = 'github' AND entity = 'pull_request';
        """)

        # --- shape: a metric, as tested-able SQL --------------------------
        # PR cycle time (hours) for merged PRs — the kind of thing you'd register
        # as a named metric with a fixtures test.
        rows = store.query("""
            SELECT
                author,
                COUNT(*)                                              AS merged_prs,
                ROUND(AVG(date_diff('hour', created_at, merged_at)), 1)
                                                                      AS avg_cycle_hours
            FROM core_pull_request
            WHERE merged_at IS NOT NULL
            GROUP BY author
            ORDER BY avg_cycle_hours;
        """)

        # --- render (stand-in) --------------------------------------------
        print("\nmetric: PR cycle time by author")
        print(f"{'author':<8} {'merged_prs':>10} {'avg_cycle_hours':>16}")
        for r in rows:
            print(f"{r['author']:<8} {r['merged_prs']:>10} {r['avg_cycle_hours']:>16}")


if __name__ == "__main__":
    main()
