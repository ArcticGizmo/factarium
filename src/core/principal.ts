/**
 * Identity, threaded through the four verbs from day one.
 *
 * Locally there is exactly one implicit user — the owner of this instance. But
 * the *rest of the app never assumes that*: every operation that reads or writes
 * flows a `RequestContext` carrying a `Principal`. Today the principal is a
 * hardcoded local owner; the day this runs on a remote box, adding real auth is
 * swapping how the Principal is produced (see `auth.ts`), not re-plumbing every
 * call site. This is the cheapest insurance against the "single trusted user"
 * assumption that local-first tools bleed on when they later go multi-user.
 */

export interface Principal {
  /** stable id — 'local-owner' today; a subject claim from an IdP later */
  id: string;
  /** human label for logs/UI */
  displayName: string;
  /** coarse role; the authorizer maps roles→permissions (see authz.ts) */
  roles: readonly string[];
}

/** Everything an operation needs to know about *who* is asking. */
export interface RequestContext {
  principal: Principal;
}

/** The implicit owner of a local, single-user instance. */
export const LOCAL_OWNER: Principal = {
  id: "local-owner",
  displayName: "Local owner",
  roles: ["owner"],
};
