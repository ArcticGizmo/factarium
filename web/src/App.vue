<script setup>
import { ref, shallowRef, onMounted } from 'vue'
import LiveSummaryPanel from './components/LiveSummaryPanel.vue'
import RepoActivityDashboard from './components/RepoActivityDashboard.vue'
import DeliveryDashboard from './components/DeliveryDashboard.vue'
import ClaudeCodeDashboard from './components/ClaudeCodeDashboard.vue'
import IdentityMappingPanel from './components/IdentityMappingPanel.vue'
import SchedulesPanel from './components/SchedulesPanel.vue'

// First-round information architecture: one page per distinct reason to be here.
// Overview is the live pulse; the middle three are the "what happened" dashboards;
// People and Sources are the "how the data gets here / who it belongs to" admin pages.
const navItems = [
  { key: 'overview', title: 'Overview', icon: 'mdi-pulse', component: LiveSummaryPanel },
  { key: 'repositories', title: 'Repositories', icon: 'mdi-source-branch', component: RepoActivityDashboard },
  { key: 'delivery', title: 'Delivery', icon: 'mdi-rocket-launch-outline', component: DeliveryDashboard },
  { key: 'claude-code', title: 'Claude Code', icon: 'mdi-robot-outline', component: ClaudeCodeDashboard },
  { key: 'people', title: 'People', icon: 'mdi-account-group-outline', component: IdentityMappingPanel },
  { key: 'sources', title: 'Sources', icon: 'mdi-sync', component: SchedulesPanel },
]

const current = ref(navItems[0])
const currentComponent = shallowRef(navItems[0].component)

function select(item) {
  current.value = item
  currentComponent.value = item.component
}

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
</script>

<template>
  <v-app>
    <v-navigation-drawer permanent color="surface" width="240">
      <div class="px-4 py-4">
        <div class="text-h6">Factarium</div>
        <div class="text-caption text-medium-emphasis">
          sync · transform · aggregate · render
        </div>
      </div>
      <v-divider />
      <v-list nav density="comfortable">
        <v-list-item
          v-for="item in navItems"
          :key="item.key"
          :active="current.key === item.key"
          :prepend-icon="item.icon"
          :title="item.title"
          @click="select(item)"
        />
      </v-list>
    </v-navigation-drawer>

    <v-app-bar color="surface" flat>
      <v-app-bar-title>{{ current.title }}</v-app-bar-title>
      <template #append>
        <span v-if="me" class="text-caption text-medium-emphasis mr-4">
          {{ me.displayName }}
        </span>
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
        <div v-if="error" class="text-error mb-2">API error: {{ error }}</div>
        <component :is="currentComponent" />
      </v-container>
    </v-main>
  </v-app>
</template>
