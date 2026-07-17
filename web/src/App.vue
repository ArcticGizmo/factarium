<script setup>
import { ref, onMounted } from 'vue'
import IntegrationsPanel from './components/IntegrationsPanel.vue'
import RepoActivityDashboard from './components/RepoActivityDashboard.vue'
import IdentityMappingPanel from './components/IdentityMappingPanel.vue'

const health = ref(null)
const me = ref(null)
const error = ref(null)
const dashboard = ref(null)

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

// When identity mappings change, re-pull the dashboard (metrics were re-aggregated).
function onMappingChanged() {
  dashboard.value?.load()
}

onMounted(load)
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
        <div class="text-caption text-medium-emphasis mb-3">
          sync · transform · aggregate · render{{ me ? ` — signed in as ${me.displayName}` : '' }}
        </div>
        <div v-if="error" class="text-error mb-2">API error: {{ error }}</div>

        <v-row>
          <v-col cols="12">
            <RepoActivityDashboard ref="dashboard" />
          </v-col>
          <v-col cols="12">
            <IdentityMappingPanel @changed="onMappingChanged" />
          </v-col>
          <v-col cols="12">
            <IntegrationsPanel />
          </v-col>
        </v-row>
      </v-container>
    </v-main>
  </v-app>
</template>
