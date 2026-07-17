/**
 * The authorization checkpoint — one place, consulted before any read/render.
 *
 * The value here is not the default policy (which is "allow everything"); it is
 * that the checkpoint *exists and is wired in*. Adding RBAC later means writing a
 * policy, not threading a new decision point through the query and render paths
 * after the fact — the expensive retrofit this seam is designed to avoid.
 */

import type { RequestContext } from "./principal.js";

/** What someone is trying to do, and to which named thing. */
export type Action = "read" | "write" | "admin";

export interface Resource {
  /** e.g. 'metric', 'source', 'dashboard', 'raw' */
  kind: string;
  /** e.g. 'pr_cycle_time', 'github' — omit for a whole-kind check */
  name?: string;
}

export interface Authorizer {
  /** Throw `AuthorizationError` if `ctx.principal` may not perform `action`. */
  authorize(ctx: RequestContext, action: Action, resource: Resource): void;
}

export class AuthorizationError extends Error {
  constructor(action: Action, resource: Resource, principalId: string) {
    super(
      `principal '${principalId}' is not permitted to ${action} ` +
        `${resource.kind}${resource.name ? `:${resource.name}` : ""}`,
    );
    this.name = "AuthorizationError";
  }
}

/**
 * Default policy for a local, single-owner instance: everything is allowed. The
 * remote path swaps this for a role/permission-based implementation.
 */
export class AllowAll implements Authorizer {
  authorize(_ctx: RequestContext, _action: Action, _resource: Resource): void {
    // intentionally permissive — see class docstring
  }
}
