<template>
  <BasePage title="Overview">
    <template #actions>
      <v-btn size="small" variant="text" prepend-icon="mdi-target" @click="targetsOpen = true">Targets</v-btn>
      <v-chip size="x-small" color="green" variant="flat">
        <v-icon start size="x-small" icon="mdi-circle" />
        live
      </v-chip>
      <span class="text-caption text-medium-emphasis">updated {{ generated }}</span>
    </template>

    <div v-if="error" class="text-error mb-2">{{ error }}</div>

    <v-row dense class="mb-2">
      <v-col v-for="tile in tiles" :key="tile.label" cols="6" sm="6" md="4" lg="3">
        <KpiTile
          :label="tile.label"
          :value="tile.value"
          :unit="tile.unit"
          :previous="tile.previous"
          :spark="tile.spark"
          :target="tile.target"
        />
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

    <OverviewTargetsDialog v-model="targetsOpen" :targets="data?.targets ?? null" @saved="onTargetsSaved" />
  </BasePage>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue';
import BasePage from '../../components/BasePage.vue';
import KpiTile from '../../components/KpiTile.vue';
import OverviewTargetsDialog from './OverviewTargetsDialog.vue';
import type { LiveSummary, OverviewTargets } from '../../types';
import { formatDateTime as fmt } from '../../utils/datetime';
import { api } from '../../api';

const data = ref<LiveSummary | null>(null);
const error = ref<string | null>(null);
const targetsOpen = ref(false);
let timer: ReturnType<typeof setInterval> | undefined;

function onTargetsSaved(saved: OverviewTargets) {
  if (data.value) data.value.targets = saved;
}

async function load() {
  try {
    data.value = await api.url('/live/summary').get().json<LiveSummary>();
    error.value = null;
  } catch (e) {
    error.value = String(e);
  }
}

onMounted(() => {
  load();
  timer = setInterval(load, 15000); // live: refresh every 15s
});
onUnmounted(() => clearInterval(timer));

const tiles = computed(() => {
  const d = data.value;
  if (!d) return [];
  const completionMin = d.targets?.issueCompletionPctMin;
  return [
    { label: 'Open PRs', value: d.pullRequests.open, unit: '', previous: null, spark: undefined, target: null },
    {
      label: 'Merged (7d)',
      value: d.pullRequests.mergedLast7d,
      unit: '',
      previous: d.pullRequests.mergedPrev7d,
      spark: d.spark?.merged,
      target: null
    },
    {
      label: 'Commits (7d)',
      value: d.commits.last7d,
      unit: '',
      previous: d.commits.prev7d,
      spark: d.spark?.commits,
      target: null
    },
    {
      label: 'Issues resolved (7d)',
      value: d.issues.resolvedLast7d,
      unit: '',
      previous: d.issues.resolvedPrev7d,
      spark: d.spark?.resolved,
      target: null
    },
    {
      label: 'Issue completion',
      value: d.issues.completionPct,
      unit: '%',
      previous: null,
      spark: undefined,
      target:
        completionMin != null ? { value: completionMin, direction: 'gte' as const, label: `≥ ${completionMin}%` } : null
    }
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
</style>
