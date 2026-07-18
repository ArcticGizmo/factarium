import { ref } from 'vue';
import type { Integration } from '../../types';

// Loads all integrations and narrows them to a single type for a per-type page.
export function useIntegrations(type: string) {
  const integrations = ref<Integration[]>([]);
  const error = ref<string | null>(null);

  async function load() {
    try {
      const all: Integration[] = await fetch('/api/integrations').then((r) => r.json());
      integrations.value = all.filter((i) => i.type === type);
      error.value = null;
    } catch (e) {
      error.value = String(e);
    }
  }

  return { integrations, error, load };
}

// "acme/api, acme/web" -> ["acme/api", "acme/web"]
export function splitCsv(value: string): string[] {
  return value
    .split(',')
    .map((s) => s.trim())
    .filter((s) => s.length > 0);
}
