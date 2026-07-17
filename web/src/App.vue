<script setup>
import { ref, onMounted } from 'vue'
import VChart from 'vue-echarts'
import IntegrationsPanel from './components/IntegrationsPanel.vue'

const health = ref(null)
const me = ref(null)
const error = ref(null)

async function load() {
  try {
    const [h, m] = await Promise.all([
      fetch('/api/health').then((r) => r.json()),
      fetch('/api/me').then((r) => r.json()),
    ])
    health.value = h
    me.value = m
  } catch (e) {
    error.value = String(e)
  }
}

onMounted(load)

// Placeholder series until real materialized metrics arrive in Phase 2.
const chartOption = {
  tooltip: { trigger: 'axis' },
  grid: { left: 40, right: 16, top: 24, bottom: 24 },
  xAxis: {
    type: 'category',
    data: ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'],
  },
  yAxis: { type: 'value' },
  series: [
    { name: 'Commits', type: 'line', smooth: true, data: [3, 5, 2, 8, 6, 1, 4] },
  ],
}
</script>

<template>
  <v-app>
    <v-app-bar color="surface" flat>
      <v-app-bar-title>Factarium</v-app-bar-title>
      <template #append>
        <v-chip
          v-if="health"
          :color="health.status === 'healthy' ? 'green' : 'orange'"
          variant="flat"
          size="small"
        >
          {{ health.status }} · db {{ health.database }}
        </v-chip>
      </template>
    </v-app-bar>

    <v-main>
      <v-container>
        <v-row>
          <v-col cols="12">
            <v-card>
              <v-card-title>
                Welcome<span v-if="me">, {{ me.displayName }}</span>
              </v-card-title>
              <v-card-subtitle>sync · transform · aggregate · render</v-card-subtitle>
              <v-card-text>
                Phase 0 walking skeleton — the API, database, and SPA are wired end to end.
                Real dashboards arrive from Phase 2.
                <div v-if="error" class="text-error mt-2">API error: {{ error }}</div>
              </v-card-text>
            </v-card>
          </v-col>

          <v-col cols="12">
            <IntegrationsPanel />
          </v-col>

          <v-col cols="12">
            <v-card title="Sample metric (placeholder)">
              <v-card-text>
                <v-chart class="chart" :option="chartOption" autoresize />
              </v-card-text>
            </v-card>
          </v-col>
        </v-row>
      </v-container>
    </v-main>
  </v-app>
</template>

<style>
.chart {
  height: 320px;
}
</style>
