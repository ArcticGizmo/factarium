<template>
  <BasePage title="Claude Code" subtitle="Usage via OTEL">
    <template #actions>
      <v-btn size="small" variant="text" :loading="loading" @click="load">Refresh</v-btn>
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

    <v-row>
      <v-col cols="12" md="6">
        <div class="chart-title">Cost over time (USD/day)</div>
        <v-chart class="chart" :option="costOption" autoresize />
      </v-col>
      <v-col cols="12" md="6">
        <div class="chart-title">Tokens over time</div>
        <v-chart class="chart" :option="tokensOption" autoresize />
      </v-col>
      <v-col cols="12" md="6">
        <div class="chart-title">Cost by person (USD)</div>
        <v-chart class="chart" :option="costByActorOption" autoresize />
      </v-col>
      <v-col cols="12" md="6">
        <div class="chart-title">Lines added over time</div>
        <v-chart class="chart" :option="linesOption" autoresize />
      </v-col>
    </v-row>
  </BasePage>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import VChart from 'vue-echarts'
import BasePage from './BasePage.vue'
import { lineOption, barOption } from '../charts'
import type { ClaudeCodeData } from '../types'

const data = ref<ClaudeCodeData | null>(null)
const error = ref<string | null>(null)
const loading = ref(false)

async function load() {
  loading.value = true
  error.value = null
  try {
    data.value = await fetch('/api/dashboards/claude-code').then((r) => r.json())
  } catch (e) {
    error.value = String(e)
  } finally {
    loading.value = false
  }
}

onMounted(load)

const tiles = computed(() => {
  const t = data.value?.totals
  if (!t) return []
  return [
    { label: 'Cost (USD)', value: `$${t.costUsd}` },
    { label: 'Tokens', value: t.tokens.toLocaleString() },
    { label: 'Lines added', value: t.linesAdded.toLocaleString() },
    { label: 'Lines removed', value: t.linesRemoved.toLocaleString() },
    { label: 'Sessions', value: t.sessions.toLocaleString() },
  ]
})

const costOption = computed(() => lineOption([{ name: 'Cost (USD)', points: data.value?.costByDay ?? [] }]))
const tokensOption = computed(() => lineOption([{ name: 'Tokens', points: data.value?.tokensByDay ?? [] }]))
const linesOption = computed(() => lineOption([{ name: 'Lines added', points: data.value?.linesAddedByDay ?? [] }]))
const costByActorOption = computed(() => barOption(data.value?.costByActor ?? []))
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
