<template>
  <BasePage title="Repositories" :subtitle="`Code review & repo health · last ${windowDays} days vs the ${windowDays} before`">
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
          :unit="tile.unit"
          :previous="tile.previous"
          :lower-is-better="tile.lowerIsBetter"
          :target="tile.target"
        />
      </v-col>
    </v-row>

    <v-row>
      <v-col cols="12" md="6">
        <div class="chart-title">Review coverage over time (% of merged PRs reviewed)</div>
        <v-chart class="chart" :option="coverageOption" autoresize />
      </v-col>
      <v-col cols="12" md="6">
        <div class="chart-title">Time to first review (avg hours/day)</div>
        <v-chart class="chart" :option="latencyOption" autoresize />
      </v-col>
      <v-col cols="12" md="6">
        <div class="chart-title">PR size distribution (merged PRs, lines changed)</div>
        <v-chart class="chart" :option="sizeOption" autoresize />
      </v-col>
      <v-col cols="12" md="6">
        <div class="chart-title">Reviews by reviewer</div>
        <v-chart class="chart" :option="reviewerOption" autoresize />
      </v-col>
    </v-row>

    <div class="chart-title mt-2">
      By repository <span class="text-medium-emphasis">· {{ totals?.repositories ?? 0 }} tracked</span>
    </div>
    <v-table density="compact" class="repo-table">
      <thead>
        <tr>
          <th>Repository</th>
          <th class="text-right">Commits</th>
          <th class="text-right">PRs merged</th>
          <th class="text-right">Review coverage</th>
          <th class="text-right">Median PR size</th>
          <th class="text-right">Avg review latency</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="row in repoTable" :key="row.repo">
          <td class="repo-name">{{ row.repo }}</td>
          <td class="text-right">{{ row.commits }}</td>
          <td class="text-right">{{ row.prsMerged }}</td>
          <td class="text-right">{{ row.prsMerged ? `${row.reviewCoveragePct}%` : '—' }}</td>
          <td class="text-right">{{ row.medianPrLines ? `${row.medianPrLines} lines` : '—' }}</td>
          <td class="text-right">
            {{ row.avgReviewLatencyHours != null ? `${row.avgReviewLatencyHours} h` : '—' }}
          </td>
        </tr>
        <tr v-if="!repoTable.length">
          <td colspan="6" class="text-medium-emphasis text-center py-4">No repository activity in this window.</td>
        </tr>
      </tbody>
    </v-table>

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
const totals = computed(() => data.value?.totals ?? null);
const repoTable = computed(() => data.value?.repoTable ?? []);

const tiles = computed(() => {
  const d = data.value;
  if (!d) return [];
  const t = d.totals;
  const p = d.previous;
  const g = d.targets;
  const gte = (v: number | null | undefined, label: string) =>
    v != null ? { value: v, direction: 'gte' as const, label } : null;
  const lte = (v: number | null | undefined, label: string) =>
    v != null ? { value: v, direction: 'lte' as const, label } : null;
  return [
    {
      label: 'Review coverage',
      value: t.reviewCoveragePct,
      unit: '%',
      previous: p.reviewCoveragePct,
      lowerIsBetter: false,
      target: gte(g.reviewCoveragePctMin, `≥ ${g.reviewCoveragePctMin}%`)
    },
    {
      label: 'Median PR size',
      value: t.medianPrLines,
      unit: ' lines',
      previous: p.medianPrLines,
      lowerIsBetter: true,
      target: lte(g.medianPrLinesMax, `≤ ${g.medianPrLinesMax}`)
    },
    {
      label: 'Review depth (comments / PR)',
      value: t.reviewDepth,
      unit: '',
      previous: p.reviewDepth,
      lowerIsBetter: false,
      target: null
    },
    {
      label: 'PR abandon rate',
      value: t.abandonRatePct,
      unit: '%',
      previous: p.abandonRatePct,
      lowerIsBetter: true,
      target: lte(g.abandonRatePctMax, `≤ ${g.abandonRatePctMax}%`)
    },
    {
      label: 'Oldest open PR',
      value: t.oldestOpenPrDays,
      unit: ' days',
      previous: null,
      lowerIsBetter: true,
      target: lte(g.oldestOpenPrDaysMax, `≤ ${g.oldestOpenPrDaysMax}d`)
    },
    {
      label: 'Unmapped identities',
      value: t.unmappedIdentities,
      unit: '',
      previous: null,
      lowerIsBetter: true,
      // A data-quality target: unmapped identities aren't attributed to anyone.
      target: lte(g.unmappedIdentitiesMax, `≤ ${g.unmappedIdentitiesMax}`)
    }
  ];
});

const coverageOption = computed(() =>
  lineOption([{ name: 'Coverage %', points: data.value?.coverageByDay ?? [] }])
);

const latencyOption = computed(() =>
  lineOption([{ name: 'Hours to first review', points: data.value?.reviewLatencyByDay ?? [] }])
);

const sizeOption = computed(() => barOption(data.value?.prSizeDistribution ?? [], { sort: false }));

const reviewerOption = computed(() => barOption(data.value?.reviewsByReviewer ?? []));
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
.repo-table {
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 8px;
}
.repo-name {
  font-family: var(--v-font-monospace, monospace);
  font-size: 0.82rem;
}
</style>
