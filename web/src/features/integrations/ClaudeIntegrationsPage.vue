<template>
  <BasePage title="Claude" subtitle="Claude Code usage (OpenTelemetry)">
    <template #actions>
      <v-btn size="small" variant="text" @click="load">Refresh</v-btn>
    </template>

    <div v-if="error" class="text-error mb-2">{{ error }}</div>

    <div class="text-body-2 text-medium-emphasis mb-3">
      Claude is a <strong>push</strong> integration: there is nothing to poll and no credential to store. Point Claude
      Code's OpenTelemetry exporter at Factarium and usage metrics stream in on their own. A connection row appears the
      first time metrics arrive.
    </div>

    <div class="section-title">Setup</div>
    <p class="text-body-2 text-medium-emphasis mb-2">
      Set these environment variables where Claude Code runs (e.g. your shell profile):
    </p>
    <pre class="setup-block mb-4">{{ setupSnippet }}</pre>

    <div class="section-title">Connection status</div>
    <v-table density="comfortable">
      <thead>
        <tr>
          <th>Name</th>
          <th>Enabled</th>
          <th>Last received</th>
          <th>Records</th>
          <th></th>
        </tr>
      </thead>
      <tbody>
        <tr v-if="integrations.length === 0">
          <td colspan="5" class="text-medium-emphasis text-caption py-4">
            No metrics received yet — complete the setup above and run Claude Code.
          </td>
        </tr>
        <tr v-for="i in integrations" :key="i.id">
          <td>{{ i.name }}</td>
          <td><v-switch v-model="i.enabled" density="compact" hide-details color="primary" @change="save(i)" /></td>
          <td class="text-caption">{{ fmt(i.lastRunCompletedAt) }}</td>
          <td class="text-caption">{{ i.lastRunRecordsWritten }}</td>
          <td>
            <v-btn size="x-small" variant="text" color="red" @click="remove(i.id)">Delete</v-btn>
          </td>
        </tr>
      </tbody>
    </v-table>
  </BasePage>
</template>

<script setup lang="ts">
import { computed, onMounted } from 'vue';
import BasePage from '../../components/BasePage.vue';
import type { Integration } from '../../types';
import { useIntegrations } from './useIntegrations';

const { integrations, error, load } = useIntegrations('claude-code');

const setupSnippet = computed(() => {
  const endpoint = window.location.origin;
  return [
    'export CLAUDE_CODE_ENABLE_TELEMETRY=1',
    'export OTEL_METRICS_EXPORTER=otlp',
    'export OTEL_EXPORTER_OTLP_PROTOCOL=http/json',
    `export OTEL_EXPORTER_OTLP_ENDPOINT=${endpoint}`
  ].join('\n');
});

async function save(row: Integration) {
  await fetch(`/api/integrations/${row.id}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ enabled: row.enabled, cron: null })
  });
  await load();
}

async function remove(id: string) {
  await fetch(`/api/integrations/${id}`, { method: 'DELETE' });
  await load();
}

function fmt(ts: string | null | undefined) {
  return ts ? new Date(ts).toLocaleString() : '—';
}

onMounted(load);
</script>

<style scoped>
.section-title {
  font-size: 0.9rem;
  font-weight: 600;
  margin: 4px 0 8px;
}
.setup-block {
  background: rgba(255, 255, 255, 0.06);
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 8px;
  padding: 12px;
  font-size: 0.8rem;
  overflow-x: auto;
  white-space: pre;
}
</style>
