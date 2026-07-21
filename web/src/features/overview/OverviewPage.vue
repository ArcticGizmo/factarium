<template>
  <BasePage title="Overview">
    <template #actions>
      <v-chip size="x-small" color="green" variant="flat">
        <v-icon start size="x-small" icon="mdi-circle" />
        live
      </v-chip>
      <span class="text-caption text-medium-emphasis">updated {{ generated }}</span>
    </template>

    <div v-if="error" class="text-error mb-2">{{ error }}</div>

    <v-row dense class="mb-2">
      <v-col v-for="tile in tiles" :key="tile.label" cols="6" sm="4" md="2">
        <div class="tile">
          <div class="tile-value">{{ tile.value }}</div>
          <div class="tile-label">{{ tile.label }}</div>
        </div>
      </v-col>
    </v-row>

    <div v-if="data" class="mt-2">
      <div class="text-caption text-medium-emphasis mb-1">
        Issues by status ({{ data.issues.done }}/{{ data.issues.total }} done)
      </div>
      <v-chip
        v-for="s in data.issues.byStatus"
        :key="s.status"
        :color="statusColors[s.status] || 'grey'"
        variant="flat"
        size="small"
        class="mr-2 mb-1"
      >
        {{ s.status }}: {{ s.count }}
      </v-chip>
    </div>

    <!-- Data freshness: how up to date each source/entity is. -->
    <div class="mt-6">
      <div class="d-flex align-center mb-2">
        <div class="text-subtitle-2">Data freshness</div>
        <v-spacer />
        <span class="text-caption text-medium-emphasis">how up to date each source is</span>
      </div>

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
    </div>
  </BasePage>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue';
import BasePage from '../../components/BasePage.vue';
import type { LiveSummary, Integration, EntityFreshness } from '../../types';
import { formatDateTime as fmt, formatRelative } from '../../utils/datetime';
import { api } from '../../api';

const data = ref<LiveSummary | null>(null);
const error = ref<string | null>(null);
const integrations = ref<Integration[]>([]);
const integrationsError = ref<string | null>(null);
let timer: ReturnType<typeof setInterval> | undefined;

async function load() {
  try {
    data.value = await api.url('/live/summary').get().json<LiveSummary>();
    error.value = null;
  } catch (e) {
    error.value = String(e);
  }
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

const tiles = computed(() => {
  const d = data.value;
  if (!d) return [];
  return [
    { label: 'Open PRs', value: d.pullRequests.open },
    { label: 'Merged (7d)', value: d.pullRequests.mergedLast7d },
    { label: 'Commits (7d)', value: d.commits.last7d },
    { label: 'Issues resolved (7d)', value: d.issues.resolvedLast7d },
    { label: 'Issue completion', value: `${d.issues.completionPct}%` }
  ];
});

const statusColors: Record<string, string> = {
  Done: 'green',
  'In Progress': 'blue',
  'In Review': 'orange',
  'To Do': 'grey'
};

const generated = computed(() => (data.value ? fmt(data.value.generatedAt) : ''));
</script>

<style scoped>
.tile {
  padding: 12px 16px;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 8px;
}
.tile-value {
  font-size: 1.6rem;
  font-weight: 600;
  line-height: 1.2;
}
.tile-label {
  font-size: 0.75rem;
  color: #898781;
}

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
