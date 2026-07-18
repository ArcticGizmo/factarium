<script setup>
import { ref, computed, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { routes } from './router'

// Nav is derived from the route table: every route with meta.nav shows up here.
const navItems = routes.filter((r) => r.meta?.nav)

const route = useRoute()
const currentTitle = computed(() => route.meta?.title ?? 'Factarium')

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
          :key="item.path"
          :to="item.path"
          :prepend-icon="item.meta.icon"
          :title="item.meta.title"
        />
      </v-list>
    </v-navigation-drawer>

    <v-app-bar color="surface" flat>
      <v-app-bar-title>{{ currentTitle }}</v-app-bar-title>
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
        <router-view />
      </v-container>
    </v-main>
  </v-app>
</template>
