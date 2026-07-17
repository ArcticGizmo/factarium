<script setup>
import { ref, computed, onMounted } from 'vue'
import VChart from 'vue-echarts'
import { lineOption, barOption } from '../charts'

const data = ref(null)
const error = ref(null)
const loading = ref(false)

async function load() {
  loading.value = true
  error.value = null
  try {
    data.value = await fetch('/api/dashboards/repo-activity').then((r) => r.json())
  } catch (e) {
    error.value = String(e)
  } finally {
    loading.value = false
  }
}

defineExpose({ load })
onMounted(load)

const tiles = computed(() => {
  const t = data.value?.totals
  if (!t) return []
  return [
    { label: 'Commits', value: t.commits },
    { label: 'PRs opened', value: t.prsOpened },
    { label: 'PRs merged', value: t.prsMerged },
    { label: 'Repositories', value: t.repositories },
    { label: 'People', value: t.people },
    { label: 'Unmapped identities', value: t.unmappedIdentities },
  ]
})

const commitsOption = computed(() =>
  lineOption([{ name: 'Commits', points: data.value?.commitsByDay ?? [] }]))

const prOption = computed(() =>
  lineOption(
    [
      { name: 'Opened', points: data.value?.prsOpenedByDay ?? [] },
      { name: 'Merged', points: data.value?.prsMergedByDay ?? [] },
    ],
    { legend: true },
  ))

const latencyOption = computed(() =>
  lineOption([{ name: 'Hours to first review', points: data.value?.reviewLatencyByDay ?? [] }]))

const byActorOption = computed(() => barOption(data.value?.commitsByActor ?? []))
</script>

<template>
  <v-card title="Repo / PR activity">
    <template #append>
      <v-btn size="small" variant="text" :loading="loading" @click="load">Refresh</v-btn>
    </template>
    <v-card-text>
      <div v-if="error" class="text-error mb-2">{{ error }}</div>

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
    </v-card-text>
  </v-card>
</template>

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
  height: 260px;
}
</style>
