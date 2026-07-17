# ADR 0001 — TypeScript backend, browser-served, remote-ready by seams

Status: **accepted** · Date: 2026-07-17

## Context

Factarium is a single-artifact, local-first engineering-metrics tool (see
[`../../README.md`](../../README.md)). Two questions had to be settled before any
implementation began:

1. **What language?** — the artifact must be a binary that other people download
   and run (single-owner *per instance*), with a nice modern UI.
2. **Does that choice foreclose remote hosting later** — including authentication
   and a permission model?

## Decision

### Language & shape
- **Backend: TypeScript**, developed on **Node** today. The UI will be a
  **browser-served** web app (Grafana/Rill style): the binary serves a compiled
  SPA and answers queries. One language spans UI and backend.
- **Substrate: embedded DuckDB** via `@duckdb/node-api` (first-party binding),
  behind the storage seam so Postgres can drop in when a scale signal fires.
- **Renderer: embed, don't build** — Evidence.dev / Observable Framework are the
  leading candidates (both TS- and DuckDB-native). Decided in a later ADR.

### Packaging (deliberately deferred)
The single-binary step is a *distribution* concern, not an architectural one, so
it does not block development. Target packager is **Bun** (`bun build --compile`),
which handles native addons in a single executable better than Node's SEA. Code
stays runtime-agnostic (web-standard APIs, no Node-only server primitives) so the
Node→Bun packaging swap is friction-free. Bun gets installed when we reach
packaging — not before.

### Remote-hosting readiness (the reason this ADR exists)
The language does **not** break remote hosting — TS/Node is a server language and
browser-served is already client-server. Two *assumptions* would break it, so we
neutralise them now with inert seams (no auth is built today):

1. **Identity is threaded through the four verbs from day one.** Every request
   carries a `Principal`; today it is a hardcoded local owner. Real auth later =
   swap the identity source, not re-plumb queries.
2. **Auth is a seam** (`AuthProvider`) with a `LocalOwnerProvider` default and room
   for an `OidcProvider`. No-op locally.
3. **A single authorization checkpoint** (`Authorizer`) sits at the query/render
   layer with an `AllowAll` default policy. The checkpoint *existing* means RBAC
   later is filling in a policy, not inserting a layer.
4. **Connector secrets go behind a credential seam** — arrives with the source
   seam (connectors don't exist yet), noted here so it isn't forgotten.

### Multi-tenancy (item 5)
Distribution is **single-owner per instance** — each person runs their own copy,
even on a remote box. So the data model is **single-workspace: no tenant column**.
Per-user visibility on a shared instance is handled by the authz seam (#3), not by
data partitioning. A real tenant dimension is a **documented future migration** if
Factarium ever becomes one instance serving many isolated customers.

## Consequences
- The Python `storage-seam/` spike remains as a **design reference** only; it is
  not the implementation.
- Transforms are plain DuckDB SQL in the app for now; a portability layer (dbt /
  SQLMesh) can return as a **dev-time** tool if a signal calls for it.
- Going remote becomes: bind to a real address behind TLS + swap
  `LocalOwnerProvider`→`OidcProvider` and `AllowAll`→a real policy. No rewrite.
