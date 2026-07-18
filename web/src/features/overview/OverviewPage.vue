<template>
  <BasePage title="Overview">
    <template #actions>
      <v-chip size="x-small" color="green" variant="flat">
        <v-icon start size="x-small" icon="mdi-circle" />
        live
      </v-chip>
      <span class="text-caption text-medium-emphasis">updated {{ generated }}</span>
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

    <div v-if="data" class="mt-2">
      <div class="text-caption text-medium-emphasis mb-1">
        Issues by status ({{ data.issues.done }}/{{ data.issues.total }} done)
      </div>
      <v-chip
        v-for="s in data.issues.byStatus"
        :key="s.status"
        :color="statusColors[s.status] || 'grey'"
        variant="flat"
        size="small"
        class="mr-2 mb-1"
      >
        {{ s.status }}: {{ s.count }}
      </v-chip>
    </div>
  </BasePage>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue';
import BasePage from '../../components/BasePage.vue';
import type { LiveSummary } from '../../types';

const data = ref<LiveSummary | null>(null);
const error = ref<string | null>(null);
let timer: ReturnType<typeof setInterval> | undefined;

async function load() {
  try {
    data.value = await fetch('/api/live/summary').then((r) => r.json());
    error.value = null;
  } catch (e) {
    error.value = String(e);
  }
}

onMounted(() => {
  load();
  timer = setInterval(load, 15000); // live: refresh every 15s
});
onUnmounted(() => clearInterval(timer));

const tiles = computed(() => {
  const d = data.value;
  if (!d) return [];
  return [
    { label: 'Open PRs', value: d.pullRequests.open },
    { label: 'Merged (7d)', value: d.pullRequests.mergedLast7d },
    { label: 'Commits (7d)', value: d.commits.last7d },
    { label: 'Issues resolved (7d)', value: d.issues.resolvedLast7d },
    { label: 'Issue completion', value: `${d.issues.completionPct}%` }
  ];
});

const statusColors: Record<string, string> = {
  Done: 'green',
  'In Progress': 'blue',
  'In Review': 'orange',
  'To Do': 'grey'
};

const generated = computed(() => (data.value ? new Date(data.value.generatedAt).toLocaleTimeString() : ''));
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
</style>
