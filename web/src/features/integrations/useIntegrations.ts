import { ref } from 'vue';
import type { Integration } from '../../types';
import { api } from '../../api';

// Loads all integrations and narrows them to a single type for a per-type page.
export function useIntegrations(type: string) {
  const integrations = ref<Integration[]>([]);
  const error = ref<string | null>(null);

  async function load() {
    try {
      const all = await api.url('/integrations').get().json<Integration[]>();
      integrations.value = all.filter((i) => i.type === type);
      error.value = null;
    } catch (e) {
      error.value = String(e);
    }
  }

  return { integrations, error, load };
}
