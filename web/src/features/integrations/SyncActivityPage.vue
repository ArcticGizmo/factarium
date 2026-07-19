<template>
  <BasePage title="Sync Activity" subtitle="Background & adhoc sync jobs">
    <template #actions>
      <v-btn size="small" variant="text" @click="reload">Refresh</v-btn>
    </template>

    <div v-if="error" class="text-error mb-2">{{ error }}</div>

    <div class="text-body-2 text-medium-emphasis mb-3">
      Every scheduled and manual integration sync is recorded here. Filter by connection or status to spot failures at a
      glance.
    </div>

    <!-- Filters -->
    <div class="d-flex align-center flex-wrap ga-3 mb-3">
      <v-select
        v-model="integrationId"
        :items="integrationOptions"
        item-title="label"
        item-value="value"
        label="Integration"
        density="compact"
        hide-details
        style="max-width: 260px"
      />
      <v-select
        v-model="status"
        :items="statusOptions"
        label="Status"
        density="compact"
        hide-details
        style="max-width: 160px"
      />
      <v-spacer />
      <v-select
        v-model="pageSize"
        :items="[25, 50, 100]"
        label="Per page"
        density="compact"
        hide-details
        style="max-width: 120px"
      />
    </div>

    <div v-if="loading" class="text-medium-emphasis text-caption py-2">Loading…</div>

    <template v-else>
      <v-table density="comfortable">
        <thead>
          <tr>
            <th>Integration</th>
            <th style="width: 110px">Trigger</th>
            <th style="width: 110px">Status</th>
            <th style="width: 170px">Started</th>
            <th style="width: 100px">Duration</th>
            <th style="width: 90px">Records</th>
            <th>Error</th>
          </tr>
        </thead>
        <tbody>
          <tr v-if="runs.length === 0">
            <td colspan="7" class="text-medium-emphasis text-caption py-4">No sync runs match these filters.</td>
          </tr>
          <tr v-for="r in runs" :key="r.id">
            <td>
              <span class="font-weight-medium">{{ r.integrationName }}</span>
              <v-chip size="x-small" variant="tonal" class="ml-2">{{ r.integrationType }}</v-chip>
            </td>
            <td>
              <v-chip size="x-small" variant="tonal" :prepend-icon="triggerIcon[r.trigger]">{{ r.trigger }}</v-chip>
            </td>
            <td>
              <v-chip size="small" variant="flat" :color="statusColor[r.status] || 'grey'">{{ r.status }}</v-chip>
            </td>
            <td class="text-caption">{{ fmt(r.startedAt) }}</td>
            <td class="text-caption">{{ duration(r) }}</td>
            <td>{{ r.recordsWritten }}</td>
            <td>
              <span v-if="r.error" class="text-error text-caption error-cell" :title="r.error">{{ r.error }}</span>
              <span v-else class="text-medium-emphasis">—</span>
            </td>
          </tr>
        </tbody>
      </v-table>

      <div class="d-flex align-center justify-space-between mt-3">
        <span class="text-caption text-medium-emphasis">{{ total }} total</span>
        <v-pagination
          v-if="pageCount > 1"
          :model-value="page"
          :length="pageCount"
          :total-visible="7"
          density="comfortable"
          @update:model-value="goToPage"
        />
      </div>
    </template>
  </BasePage>
</template>

<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue';
import BasePage from '../../components/BasePage.vue';
import type { Integration, SyncRun, SyncRunsPageResult } from '../../types';
import { formatDateTime as fmt } from '../../utils/datetime';
import { api } from '../../api';

const runs = ref<SyncRun[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = ref(25);
// null = all integrations; otherwise a specific integration id.
const integrationId = ref<string | null>(null);
// '' = all statuses; otherwise one of Running / Success / Failed.
const status = ref<string>('');
const integrations = ref<Integration[]>([]);
const error = ref<string | null>(null);
const loading = ref(false);

const statusOptions = [
  { title: 'All statuses', value: '' },
  { title: 'Running', value: 'Running' },
  { title: 'Success', value: 'Success' },
  { title: 'Failed', value: 'Failed' }
];

const integrationOptions = computed(() => [
  { label: 'All integrations', value: null },
  ...integrations.value.map((i) => ({ label: i.name, value: i.id }))
]);

const pageCount = computed(() => Math.max(1, Math.ceil(total.value / pageSize.value)));

const statusColor: Record<string, string> = {
  Success: 'green',
  Failed: 'red',
  Running: 'blue'
};

const triggerIcon: Record<string, string> = {
  Scheduled: 'mdi-clock-outline',
  Manual: 'mdi-gesture-tap'
};

async function loadIntegrations() {
  try {
    integrations.value = await api.url('/integrations').get().json<Integration[]>();
  } catch {
    // Non-fatal: the filter just falls back to "All integrations".
    integrations.value = [];
  }
}

async function loadRuns() {
  loading.value = true;
  try {
    const params = new URLSearchParams({
      page: String(page.value),
      pageSize: String(pageSize.value)
    });
    if (integrationId.value) params.set('integrationId', integrationId.value);
    if (status.value) params.set('status', status.value);

    const result = await api.url(`/sync-runs?${params}`).get().json<SyncRunsPageResult>();
    runs.value = result.runs;
    total.value = result.total;
    error.value = null;
  } catch (e) {
    error.value = String(e);
  } finally {
    loading.value = false;
  }
}

async function reload() {
  await loadIntegrations();
  await loadRuns();
}

function goToPage(p: number) {
  page.value = p;
  loadRuns();
}

// Elapsed time of a run; blank while still running.
function duration(r: SyncRun): string {
  if (!r.completedAt) return '—';
  const ms = new Date(r.completedAt).getTime() - new Date(r.startedAt).getTime();
  if (!Number.isFinite(ms) || ms < 0) return '—';
  if (ms < 1000) return `${ms} ms`;
  const s = ms / 1000;
  if (s < 60) return `${s.toFixed(1)} s`;
  const m = Math.floor(s / 60);
  return `${m}m ${Math.round(s % 60)}s`;
}

// Any filter / page-size change resets to page 1 and reloads.
watch([integrationId, status, pageSize], () => {
  page.value = 1;
  loadRuns();
});

onMounted(reload);
</script>

<style scoped>
/* Keep long error text on one line with an ellipsis; full text shows on hover. */
.error-cell {
  display: inline-block;
  max-width: 420px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  vertical-align: bottom;
}
</style>
