<template>
  <BasePage title="Repositories" :subtitle="`Repo / PR activity · last ${windowDays} days vs the ${windowDays} before`">
    <template #actions>
      <v-btn size="small" variant="text" prepend-icon="mdi-target" @click="targetsOpen = true">Targets</v-btn>
      <v-btn size="small" variant="text" :loading="loading" @click="load">Refresh</v-btn>
    </template>

    <div v-if="error" class="text-error mb-2">{{ error }}</div>

    <v-row dense class="mb-2">
      <v-col v-for="tile in tiles" :key="tile.label" cols="6" sm="6" md="4">
        <KpiTile
          :label="tile.label"
          :value="tile.value"
          :previous="tile.previous"
          :spark="tile.spark"
          :target="tile.target"
        />
      </v-col>
    </v-row>

    <v-row>
      <v-col cols="12" md="6">
        <div class="chart-title">Commits over time</div>
        <v-chart class="chart" :option="commitsOption" autoresize />
      </v-col>
      <v-col cols="12" md="6">
        <div class="chart-title">Pull requests over time</div>
        <v-chart class="chart" :option="prOption" autoresize />
      </v-col>
      <v-col cols="12" md="6">
        <div class="chart-title">Commits by person</div>
        <v-chart class="chart" :option="byActorOption" autoresize />
      </v-col>
      <v-col cols="12" md="6">
        <div class="chart-title">Time to first review (avg hours/day)</div>
        <v-chart class="chart" :option="latencyOption" autoresize />
      </v-col>
    </v-row>

    <RepoTargetsDialog v-model="targetsOpen" :targets="data?.targets ?? null" @saved="onTargetsSaved" />
  </BasePage>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import VChart from 'vue-echarts';
import BasePage from '../../components/BasePage.vue';
import KpiTile from '../../components/KpiTile.vue';
import RepoTargetsDialog from './RepoTargetsDialog.vue';
import { lineOption, barOption } from '../../charts';
import type { RepoActivityData, RepoActivityTargets } from '../../types';
import { api } from '../../api';

const data = ref<RepoActivityData | null>(null);
const error = ref<string | null>(null);
const loading = ref(false);
const targetsOpen = ref(false);

async function load() {
  loading.value = true;
  error.value = null;
  try {
    data.value = await api.url('/dashboards/repo-activity').get().json<RepoActivityData>();
  } catch (e) {
    error.value = String(e);
  } finally {
    loading.value = false;
  }
}

onMounted(load);

function onTargetsSaved(saved: RepoActivityTargets) {
  if (data.value) data.value.targets = saved;
}

const windowDays = computed(() => data.value?.windowDays ?? 30);
const vals = (points: { value: number }[] | undefined) => (points ?? []).map((p) => p.value);

const tiles = computed(() => {
  const d = data.value;
  if (!d) return [];
  const t = d.totals;
  const p = d.previous;
  const unmappedMax = d.targets?.unmappedIdentitiesMax;
  return [
    { label: 'Commits', value: t.commits, previous: p.commits, spark: vals(d.commitsByDay), target: null },
    { label: 'PRs opened', value: t.prsOpened, previous: p.prsOpened, spark: vals(d.prsOpenedByDay), target: null },
    { label: 'PRs merged', value: t.prsMerged, previous: p.prsMerged, spark: vals(d.prsMergedByDay), target: null },
    { label: 'Repositories', value: t.repositories, previous: null, spark: undefined, target: null },
    { label: 'People', value: t.people, previous: null, spark: undefined, target: null },
    {
      label: 'Unmapped identities',
      value: t.unmappedIdentities,
      previous: null,
      spark: undefined,
      // A data-quality target: unmapped identities aren't attributed to anyone.
      target: unmappedMax != null
        ? { value: unmappedMax, direction: 'lte' as const, label: `≤ ${unmappedMax}` }
        : null
    }
  ];
});

const commitsOption = computed(() => lineOption([{ name: 'Commits', points: data.value?.commitsByDay ?? [] }]));

const prOption = computed(() =>
  lineOption(
    [
      { name: 'Opened', points: data.value?.prsOpenedByDay ?? [] },
      { name: 'Merged', points: data.value?.prsMergedByDay ?? [] }
    ],
    { legend: true }
  )
);

const latencyOption = computed(() =>
  lineOption([{ name: 'Hours to first review', points: data.value?.reviewLatencyByDay ?? [] }])
);

const byActorOption = computed(() => barOption(data.value?.commitsByActor ?? []));
</script>

<style scoped>
.chart-title {
  font-size: 0.85rem;
  color: #c3c2b7;
  margin: 8px 0 4px;
}
.chart {
  height: 260px;
}
</style>
