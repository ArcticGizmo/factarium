<template>
  <BasePage title="Delivery" subtitle="DORA-ish delivery metrics">
    <template #actions>
      <v-btn size="small" variant="text" :loading="loading" @click="load">Refresh</v-btn>
    </template>

    <div v-if="error" class="text-error mb-2">{{ error }}</div>

    <v-alert type="info" variant="tonal" density="compact" class="mb-3">
      Deployment frequency &amp; change lead time use a proxy:
      <strong
        >merged PRs targeting <code>{{ branch }}</code></strong
      >. Wire a real deploy/incident source later for full DORA (change-failure rate, MTTR).
    </v-alert>

    <v-row dense class="mb-2">
      <v-col v-for="tile in tiles" :key="tile.label" cols="6" sm="4" md="2">
        <div class="tile">
          <div class="tile-value">{{ tile.value }}</div>
          <div class="tile-label">{{ tile.label }}</div>
        </div>
      </v-col>
    </v-row>

    <v-row>
      <v-col cols="12" md="6">
        <div class="chart-title">Deployment frequency (merges to {{ branch }} / day)</div>
        <v-chart class="chart" :option="deploysOption" autoresize />
      </v-col>
      <v-col cols="12" md="6">
        <div class="chart-title">Change lead time (avg hours PR open → merge)</div>
        <v-chart class="chart" :option="leadTimeOption" autoresize />
      </v-col>
      <v-col cols="12" md="6">
        <div class="chart-title">Throughput (PRs merged / day)</div>
        <v-chart class="chart" :option="throughputOption" autoresize />
      </v-col>
      <v-col cols="12" md="6">
        <div class="chart-title">Issue cycle time (avg hours created → resolved)</div>
        <v-chart class="chart" :option="cycleOption" autoresize />
      </v-col>
      <v-col cols="12" md="6">
        <div class="chart-title">Issues resolved / day</div>
        <v-chart class="chart" :option="issuesOption" autoresize />
      </v-col>
    </v-row>
  </BasePage>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import VChart from 'vue-echarts';
import BasePage from '../../components/BasePage.vue';
import { lineOption } from '../../charts';
import type { DeliveryData } from '../../types';

const data = ref<DeliveryData | null>(null);
const error = ref<string | null>(null);
const loading = ref(false);

async function load() {
  loading.value = true;
  error.value = null;
  try {
    data.value = await fetch('/api/dashboards/delivery').then((r) => r.json());
  } catch (e) {
    error.value = String(e);
  } finally {
    loading.value = false;
  }
}

onMounted(load);

const branch = computed(() => data.value?.deployProxyBranch ?? 'main');

const tiles = computed(() => {
  const t = data.value?.totals;
  if (!t) return [];
  return [
    { label: 'Deployments', value: t.deploys },
    { label: 'Avg lead time (h)', value: t.avgLeadTimeHours },
    { label: 'PRs merged', value: t.prsMerged },
    { label: 'Issues resolved', value: t.issuesResolved },
    { label: 'Avg cycle time (h)', value: t.avgIssueCycleHours },
    { label: 'Avg review wait (h)', value: t.avgReviewLatencyHours }
  ];
});

const deploysOption = computed(() => lineOption([{ name: 'Deployments', points: data.value?.deploysByDay ?? [] }]));
const leadTimeOption = computed(() => lineOption([{ name: 'Lead time (h)', points: data.value?.leadTimeByDay ?? [] }]));
const throughputOption = computed(() => lineOption([{ name: 'PRs merged', points: data.value?.prsMergedByDay ?? [] }]));
const issuesOption = computed(() =>
  lineOption([{ name: 'Issues resolved', points: data.value?.issuesResolvedByDay ?? [] }])
);
const cycleOption = computed(() => lineOption([{ name: 'Cycle time (h)', points: data.value?.cycleTimeByDay ?? [] }]));
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
.chart-title {
  font-size: 0.85rem;
  color: #c3c2b7;
  margin: 8px 0 4px;
}
.chart {
  height: 240px;
}
</style>
