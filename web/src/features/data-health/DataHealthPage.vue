<template>
  <BasePage title="Data health" subtitle="How up to date each source is">
    <template #actions>
      <v-chip size="x-small" color="green" variant="flat">
        <v-icon start size="x-small" icon="mdi-circle" />
        live
      </v-chip>
      <v-btn size="small" variant="text" @click="load">Refresh</v-btn>
    </template>

    <div v-if="integrationsError" class="text-error text-caption mb-2">{{ integrationsError }}</div>
    <div v-else-if="integrations.length === 0" class="text-caption text-medium-emphasis">
      No integrations configured yet.
    </div>

    <v-sheet v-for="i in integrations" :key="i.id" rounded border class="pa-3 mb-2">
      <div class="d-flex align-center flex-wrap ga-2 mb-2">
        <v-icon :icon="typeIcon(i.type)" size="small" />
        <span class="font-weight-medium">{{ i.name }}</span>
        <v-chip size="x-small" variant="tonal" :color="runStatusColor(i)">
          <v-progress-circular v-if="isRunning(i)" indeterminate size="12" width="2" class="mr-1" />
          {{ isRunning(i) ? 'syncing' : i.lastRunStatus.toLowerCase() }}
        </v-chip>
        <v-spacer />
        <span class="text-caption text-medium-emphasis">{{ scheduleLabel(i) }}</span>
      </div>

      <div v-if="i.entities.length === 0" class="text-caption text-medium-emphasis">
        Push-based — no scheduled entities.
      </div>
      <v-table v-else density="compact" class="freshness-table">
        <thead>
          <tr>
            <th>Entity</th>
            <th class="text-right">Records</th>
            <th>Data through</th>
            <th>Last pulled</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="e in i.entities" :key="e">
            <td>
              <span class="stale-dot" :class="`stale-${staleness(i.entityFreshness[e])}`" />
              {{ e }}
              <v-progress-circular
                v-if="i.entityFreshness[e]?.running"
                indeterminate
                size="12"
                width="2"
                class="ml-1"
              />
            </td>
            <td class="text-right text-caption">{{ i.entityFreshness[e]?.count ?? 0 }}</td>
            <td class="text-caption" :title="fmt(i.entityFreshness[e]?.latest)">
              {{ formatRelative(i.entityFreshness[e]?.latest) }}
            </td>
            <td class="text-caption" :title="fmt(i.entityFreshness[e]?.lastFetched)">
              {{ formatRelative(i.entityFreshness[e]?.lastFetched) }}
            </td>
          </tr>
        </tbody>
      </v-table>
    </v-sheet>
  </BasePage>
</template>

<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue';
import BasePage from '../../components/BasePage.vue';
import type { Integration, EntityFreshness } from '../../types';
import { formatDateTime as fmt, formatRelative } from '../../utils/datetime';
import { api } from '../../api';

const integrations = ref<Integration[]>([]);
const integrationsError = ref<string | null>(null);
let timer: ReturnType<typeof setInterval> | undefined;

async function load() {
  try {
    integrations.value = await api.url('/integrations').get().json<Integration[]>();
    integrationsError.value = null;
  } catch (e) {
    integrationsError.value = String(e);
  }
}

onMounted(() => {
  load();
  timer = setInterval(load, 15000); // live: refresh every 15s
});
onUnmounted(() => clearInterval(timer));

const typeIcons: Record<string, string> = {
  github: 'mdi-github',
  jira: 'mdi-jira',
  tempo: 'mdi-clock-outline',
  'claude-code': 'mdi-creation'
};
const typeIcon = (type: string) => typeIcons[type] ?? 'mdi-transit-connection-variant';

// A sync is live if the integration reports Running, or any of its entities is mid-sync.
const isRunning = (i: Integration) =>
  i.lastRunStatus === 'Running' || Object.values(i.entityFreshness).some((f) => f.running);

const runStatusColor = (i: Integration) => {
  if (isRunning(i)) return 'blue';
  if (i.lastRunStatus === 'Failed') return 'orange';
  if (i.lastRunStatus === 'Success') return 'green';
  return 'grey';
};

function scheduleLabel(i: Integration): string {
  if (!i.enabled) return 'disabled';
  if (!i.nextRunAt) return 'no schedule';
  const ms = new Date(i.nextRunAt).getTime() - Date.now();
  if (ms <= 0) return 'due now';
  const m = Math.round(ms / 60000);
  if (m < 60) return `next in ${m}m`;
  const h = Math.round(m / 60);
  if (h < 24) return `next in ${h}h`;
  return `next in ${Math.round(h / 24)}d`;
}

// Freshness signal, based on when we last pulled (not how old the source data is): a source
// we checked recently is "up to date" even if the underlying data hasn't changed.
type Stale = 'running' | 'never' | 'fresh' | 'stale' | 'old';
function staleness(f: EntityFreshness | undefined): Stale {
  if (f?.running) return 'running';
  if (!f || f.count === 0 || !f.lastFetched) return 'never';
  const h = (Date.now() - new Date(f.lastFetched).getTime()) / 3.6e6;
  if (h < 24) return 'fresh';
  if (h < 24 * 7) return 'stale';
  return 'old';
}
</script>

<style scoped>
.freshness-table :deep(th),
.freshness-table :deep(td) {
  height: 34px !important;
}

.stale-dot {
  display: inline-block;
  width: 8px;
  height: 8px;
  border-radius: 50%;
  margin-right: 6px;
  vertical-align: middle;
  background: #898781;
}
.stale-fresh {
  background: #4caf50;
}
.stale-stale {
  background: #ffb300;
}
.stale-old {
  background: #ef5350;
}
.stale-running {
  background: #42a5f5;
}
.stale-never {
  background: #55534e;
}
</style>
