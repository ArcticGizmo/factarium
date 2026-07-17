/**
 * The authentication seam — same "put it behind an interface" move as storage.
 *
 * `AuthProvider.authenticate()` turns whatever a transport hands it (nothing at
 * all, locally; a bearer token or session cookie, remotely) into a `Principal`.
 * The default `LocalOwnerProvider` ignores its input and always returns the
 * single local owner — no login, no accounts. Going remote means registering an
 * `OidcProvider` (or similar) instead; nothing downstream changes because
 * everything downstream only ever sees a `Principal`.
 */

import type { Principal } from "./principal.js";
import { LOCAL_OWNER } from "./principal.js";

/** Opaque credential material from the transport (token, cookie, header, ...). */
export interface Credentials {
  token?: string;
  [k: string]: unknown;
}

export interface AuthProvider {
  /** Resolve a Principal, or throw if the credentials are invalid. */
  authenticate(creds?: Credentials): Promise<Principal>;
}

/**
 * Default provider for a local, single-owner instance: there is no auth, and the
 * caller is always the owner. Inert today; the seam is the point.
 */
export class LocalOwnerProvider implements AuthProvider {
  async authenticate(_creds?: Credentials): Promise<Principal> {
    return LOCAL_OWNER;
  }
}
