/**
 * Default credential provider: read outbound secrets from the environment.
 *
 * Convention: FACTARIUM_<SOURCE>_<CONNECTION>_TOKEN
 *   e.g. FACTARIUM_GITHUB_DEFAULT_TOKEN=ghp_xxx
 *
 * Returns an empty credential set if nothing is configured (fine for sources
 * that need no auth, like reading a public pre-aggregated file). The remote
 * path swaps this for a secrets-manager-backed provider; nothing else changes.
 */

import type { CredentialProvider, SourceCredentials } from "../../core/source.js";

export class EnvCredentialProvider implements CredentialProvider {
  constructor(private readonly env: NodeJS.ProcessEnv = process.env) {}

  async for(sourceName: string, connection: string): Promise<SourceCredentials> {
    const key = `FACTARIUM_${sourceName}_${connection}_TOKEN`
      .toUpperCase()
      .replace(/[^A-Z0-9_]/g, "_");
    const token = this.env[key];
    return token ? { token } : {};
  }
}
