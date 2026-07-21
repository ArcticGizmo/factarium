<template>
  <BasePage title="Delivery" :subtitle="`DORA-ish delivery metrics · last ${windowDays} days vs the ${windowDays} before`">
    <template #actions>
      <v-btn size="small" variant="text" prepend-icon="mdi-target" @click="targetsOpen = true">Targets</v-btn>
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
      <v-col v-for="tile in tiles" :key="tile.label" cols="6" sm="6" md="4">
        <KpiTile
          :label="tile.label"
          :value="tile.value"
          :unit="tile.unit"
          :previous="tile.previous"
          :lower-is-better="tile.lowerIsBetter"
          :spark="tile.spark"
          :target="tile.target"
          :compare-value="tile.compareValue"
        />
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

    <DeliveryTargetsDialog v-model="targetsOpen" :targets="data?.targets ?? null" @saved="onTargetsSaved" />
  </BasePage>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import VChart from 'vue-echarts';
import BasePage from '../../components/BasePage.vue';
import KpiTile from '../../components/KpiTile.vue';
import DeliveryTargetsDialog from './DeliveryTargetsDialog.vue';
import { lineOption } from '../../charts';
import type { DeliveryData, DeliveryTargets } from '../../types';
import { api } from '../../api';

const data = ref<DeliveryData | null>(null);
const error = ref<string | null>(null);
const loading = ref(false);
const targetsOpen = ref(false);

async function load() {
  loading.value = true;
  error.value = null;
  try {
    data.value = await api.url('/dashboards/delivery').get().json<DeliveryData>();
  } catch (e) {
    error.value = String(e);
  } finally {
    loading.value = false;
  }
}

onMounted(load);

// Apply saved targets in place so bands/chips update without a full reload.
function onTargetsSaved(saved: DeliveryTargets) {
  if (data.value) data.value.targets = saved;
}

const branch = computed(() => data.value?.deployProxyBranch ?? 'main');
const windowDays = computed(() => data.value?.windowDays ?? 30);
const targets = computed(() => data.value?.targets);

const vals = (points: { value: number }[] | undefined) => (points ?? []).map((p) => p.value);

// Deploys are shown as a window total but the target is a weekly rate, so compare the
// window's deploys normalised to per-week against the target.
const deploysPerWeek = computed(() => {
  const t = data.value?.totals.deploys ?? 0;
  const weeks = windowDays.value / 7;
  return weeks > 0 ? Math.round((t / weeks) * 10) / 10 : 0;
});

const tiles = computed(() => {
  const d = data.value;
  if (!d) return [];
  const t = d.totals;
  const p = d.previous;
  return [
    {
      label: 'Deployments',
      value: t.deploys,
      unit: '',
      previous: p.deploys,
      lowerIsBetter: false,
      spark: vals(d.deploysByDay),
      compareValue: deploysPerWeek.value,
      target: targets.value?.deploysPerWeek != null
        ? { value: targets.value.deploysPerWeek, direction: 'gte' as const, label: `≥ ${targets.value.deploysPerWeek}/wk` }
        : null
    },
    {
      label: 'Avg lead time (h)',
      value: t.avgLeadTimeHours,
      unit: 'h',
      previous: p.avgLeadTimeHours,
      lowerIsBetter: true,
      spark: vals(d.leadTimeByDay),
      target: targets.value?.leadTimeHours != null
        ? { value: targets.value.leadTimeHours, direction: 'lte' as const, label: `< ${targets.value.leadTimeHours}h` }
        : null
    },
    {
      label: 'PRs merged',
      value: t.prsMerged,
      unit: '',
      previous: p.prsMerged,
      lowerIsBetter: false,
      spark: vals(d.prsMergedByDay),
      target: null
    },
    {
      label: 'Issues resolved',
      value: t.issuesResolved,
      unit: '',
      previous: p.issuesResolved,
      lowerIsBetter: false,
      spark: vals(d.issuesResolvedByDay),
      target: null
    },
    {
      label: 'Avg cycle time (h)',
      value: t.avgIssueCycleHours,
      unit: 'h',
      previous: p.avgIssueCycleHours,
      lowerIsBetter: true,
      spark: vals(d.cycleTimeByDay),
      target: targets.value?.cycleTimeHours != null
        ? { value: targets.value.cycleTimeHours, direction: 'lte' as const, label: `< ${targets.value.cycleTimeHours}h` }
        : null
    },
    {
      label: 'Avg review wait (h)',
      value: t.avgReviewLatencyHours,
      unit: 'h',
      previous: p.avgReviewLatencyHours,
      lowerIsBetter: true,
      spark: undefined,
      target: targets.value?.reviewLatencyHours != null
        ? { value: targets.value.reviewLatencyHours, direction: 'lte' as const, label: `< ${targets.value.reviewLatencyHours}h` }
        : null
    }
  ];
});

const deploysOption = computed(() => lineOption([{ name: 'Deployments', points: data.value?.deploysByDay ?? [] }]));
const leadTimeOption = computed(() =>
  lineOption([{ name: 'Lead time (h)', points: data.value?.leadTimeByDay ?? [] }], {
    threshold: targets.value?.leadTimeHours != null
      ? { value: targets.value.leadTimeHours, label: `target < ${targets.value.leadTimeHours}h` }
      : undefined
  })
);
const throughputOption = computed(() => lineOption([{ name: 'PRs merged', points: data.value?.prsMergedByDay ?? [] }]));
const issuesOption = computed(() =>
  lineOption([{ name: 'Issues resolved', points: data.value?.issuesResolvedByDay ?? [] }])
);
const cycleOption = computed(() =>
  lineOption([{ name: 'Cycle time (h)', points: data.value?.cycleTimeByDay ?? [] }], {
    threshold: targets.value?.cycleTimeHours != null
      ? { value: targets.value.cycleTimeHours, label: `target < ${targets.value.cycleTimeHours}h` }
      : undefined
  })
);
</script>

<style scoped>
.chart-title {
  font-size: 0.85rem;
  color: #c3c2b7;
  margin: 8px 0 4px;
}
.chart {
  height: 240px;
}
</style>
