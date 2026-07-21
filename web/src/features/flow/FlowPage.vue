<template>
  <BasePage
    title="Flow"
    subtitle="Where work time goes, per person — replayed from the Jira changelog (unassigned/pre-pickup time excluded)"
  >
    <template #actions>
      <v-btn size="small" variant="text" :loading="loading" @click="load">Refresh</v-btn>
    </template>

    <div v-if="error" class="text-error mb-2">{{ error }}</div>

    <div v-if="empty" class="text-medium-emphasis">
      No changelog flow data yet. Sync a Jira project's <code>issue_changelog</code> entity and run the pipeline, then
      come back.
    </div>

    <template v-else-if="data">
      <v-row dense class="mb-2">
        <v-col v-for="tile in tiles" :key="tile.label" cols="6" sm="3">
          <div class="tile">
            <div class="tile-value">{{ tile.value }}</div>
            <div class="tile-label">{{ tile.label }}</div>
          </div>
        </v-col>
      </v-row>

      <div class="chart-title">Time in status, per person (hours)</div>
      <v-chart class="chart" :style="{ height: flowHeight + 'px' }" :option="timeInStatusOption" autoresize />

      <div v-if="assignedBlocked.length" class="chart-title mt-4">Blocked time, per person (hours)</div>
      <v-chart
        v-if="assignedBlocked.length"
        class="chart"
        :style="{ height: blockedHeight + 'px' }"
        :option="blockedOption"
        autoresize
      />
    </template>
  </BasePage>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import VChart from 'vue-echarts';
import BasePage from '../../components/BasePage.vue';
import { stackedBarOption, barOption } from '../../charts';
import type { IssueFlowData } from '../../types';
import { api } from '../../api';

const data = ref<IssueFlowData | null>(null);
const error = ref<string | null>(null);
const loading = ref(false);

async function load() {
  loading.value = true;
  error.value = null;
  try {
    data.value = await api.url('/live/issue-flow').get().json<IssueFlowData>();
  } catch (e) {
    error.value = String(e);
  } finally {
    loading.value = false;
  }
}

onMounted(load);

// "unassigned" is the absence of a person (mostly pre-pickup backlog time); it swamps the
// per-person scale, so it's excluded from every per-person view on this page.
const UNASSIGNED = 'unassigned';

const empty = computed(() => data.value !== null && flow.value.categories.length === 0);

const assignedBlocked = computed(() =>
  (data.value?.blocked ?? []).filter((b) => b.assignee !== UNASSIGNED)
);

const tiles = computed(() => {
  const d = data.value;
  if (!d) return [];
  const blockedHours = round(assignedBlocked.value.reduce((sum, b) => sum + b.hours, 0));
  return [
    { label: 'Reopens', value: d.churn.reopens },
    { label: 'Reassignments', value: d.churn.reassignments },
    { label: 'Backflow', value: d.churn.backflow },
    { label: 'Blocked (h)', value: blockedHours }
  ];
});

// Order statuses by workflow category (To Do -> In Progress -> Done), then name, so the
// stack reads left-to-right through the workflow. Cap at 8 chromatic slots; fold the rest
// (by smallest total time) into a neutral "Other".
const CATEGORY_CAP = 8;
const categoryRank = (c: string | null) =>
  c === 'To Do' ? 0 : c === 'In Progress' ? 1 : c === 'Done' ? 2 : 3;

const flow = computed<{ categories: string[]; rows: { label: string; values: number[] }[] }>(() => {
  const d = data.value;
  const items = d?.timeInStatus.filter((t) => t.assignee !== UNASSIGNED) ?? [];
  if (!d || items.length === 0) return { categories: [], rows: [] };

  const rankOf = new Map<string, number>();
  const totalOf = new Map<string, number>();
  for (const t of items) {
    rankOf.set(t.status, categoryRank(t.category));
    totalOf.set(t.status, (totalOf.get(t.status) ?? 0) + t.hours);
  }

  const ordered = [...totalOf.keys()].sort(
    (a, b) => (rankOf.get(a)! - rankOf.get(b)!) || a.localeCompare(b)
  );

  // Which statuses fold into "Other" when there are more than the colour cap allows.
  const folded = new Set<string>();
  let categories = ordered;
  if (ordered.length > CATEGORY_CAP) {
    const keep = new Set(
      [...totalOf.entries()].sort((a, b) => b[1] - a[1]).slice(0, CATEGORY_CAP - 1).map(([s]) => s)
    );
    categories = ordered.filter((s) => keep.has(s));
    ordered.filter((s) => !keep.has(s)).forEach((s) => folded.add(s));
    categories.push('Other');
  }

  const index = new Map(categories.map((c, i) => [c, i]));
  const byAssignee = new Map<string, number[]>();
  for (const t of items) {
    const key = folded.has(t.status) ? 'Other' : t.status;
    const i = index.get(key);
    if (i === undefined) continue;
    const values = byAssignee.get(t.assignee) ?? new Array<number>(categories.length).fill(0);
    values[i] += t.hours;
    byAssignee.set(t.assignee, values);
  }

  // Ascending total so the biggest contributor sits at the top of the horizontal bar.
  const rows = [...byAssignee.entries()]
    .map(([label, values]) => ({ label, values: values.map(round), total: values.reduce((a, b) => a + b, 0) }))
    .sort((a, b) => a.total - b.total)
    .map(({ label, values }) => ({ label, values }));

  return { categories, rows };
});

const timeInStatusOption = computed(() => stackedBarOption(flow.value.categories, flow.value.rows, 'h'));
const blockedOption = computed(() =>
  barOption(assignedBlocked.value.map((b) => ({ label: b.assignee, value: round(b.hours) })))
);

// Give each person's row room; keep a sensible floor and cap.
const flowHeight = computed(() => Math.min(720, Math.max(240, flow.value.rows.length * 44 + 60)));
const blockedHeight = computed(() => Math.min(480, Math.max(160, assignedBlocked.value.length * 40 + 40)));

function round(n: number) {
  return Math.round(n * 10) / 10;
}
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
  width: 100%;
}
</style>
