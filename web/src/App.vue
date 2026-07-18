<template>
  <v-app>
    <v-navigation-drawer permanent color="surface" width="240">
      <div class="px-4 py-4">
        <div class="text-h6">Factarium</div>
      </div>
      <v-divider />
      <v-list nav density="comfortable">
        <!-- Nav order and visibility are authored here, independent of the
             route table. Reorder freely; add v-if to show/hide an item. -->
        <NavItem to="/overview" icon="mdi-pulse" title="Overview" />
        <NavItem to="/repositories" icon="mdi-source-branch" title="Repositories" />
        <NavItem to="/delivery" icon="mdi-rocket-launch-outline" title="Delivery" />
        <NavItem to="/claude-code" icon="mdi-robot-outline" title="Claude Code" />
        <NavItem to="/people" icon="mdi-account-group-outline" title="People" />
        <NavItem to="/sources" icon="mdi-sync" title="Sources" />
      </v-list>
    </v-navigation-drawer>

    <v-app-bar color="surface" flat>
      <template #append>
        <span v-if="me" class="text-caption text-medium-emphasis mr-4">
          {{ me.displayName }}
        </span>
        <v-chip v-if="health" :color="health.status === 'healthy' ? 'green' : 'orange'" variant="flat" size="small">
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

<script setup lang="ts">
import { ref, onMounted } from 'vue';
import NavItem from './components/NavItem.vue';

const health = ref<{ status: string; database: string } | null>(null);
const me = ref<{ displayName: string } | null>(null);
const error = ref<string | null>(null);

async function load() {
  try {
    const [h, m] = await Promise.all([
      fetch('/api/health').then((r) => r.json()),
      fetch('/api/me').then((r) => r.json())
    ]);
    health.value = h;
    me.value = m;
  } catch (e) {
    error.value = String(e);
  }
}

onMounted(load);
</script>
