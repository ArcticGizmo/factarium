<template>
  <BasePage :title="title" subtitle="Synced records (raw, as stored)">
    <template #actions>
      <v-btn size="small" variant="text" prepend-icon="mdi-arrow-left" :to="backTo">Back</v-btn>
      <v-btn size="small" variant="text" @click="reload">Refresh</v-btn>
    </template>

    <div v-if="error" class="text-error mb-2">{{ error }}</div>

    <div class="text-body-2 text-medium-emphasis mb-3">
      Everything Factarium has replicated for this connection, exactly as held in the bronze tier — the raw source
      payload plus provenance. Read-only.
    </div>

    <div v-if="summary && summary.entityTypes.length === 0" class="text-medium-emphasis">
      No records synced yet. Run a sync from the connection's page first.
    </div>

    <template v-else-if="summary">
      <v-tabs v-model="selectedType" density="comfortable" class="mb-3">
        <v-tab v-for="t in summary.entityTypes" :key="t.entityType" :value="t.entityType">
          {{ t.entityType }} ({{ t.count }})
        </v-tab>
      </v-tabs>

      <div v-if="loadingRecords" class="text-medium-emphasis text-caption py-2">Loading…</div>

      <v-expansion-panels v-else multiple variant="accordion">
        <v-expansion-panel v-for="r in records" :key="r.id">
          <v-expansion-panel-title>
            <div class="d-flex align-center flex-wrap ga-3">
              <span class="font-weight-medium">{{ r.sourceId }}</span>
              <span class="text-caption text-medium-emphasis">updated {{ fmt(r.sourceUpdatedAt) }}</span>
              <v-chip v-if="r.version > 1" size="x-small" variant="tonal">v{{ r.version }}</v-chip>
            </div>
          </v-expansion-panel-title>
          <v-expansion-panel-text>
            <div class="text-caption text-medium-emphasis mb-2">
              first seen {{ fmt(r.firstSeenAt) }} · fetched {{ fmt(r.fetchedAt) }}
            </div>
            <pre class="payload">{{ pretty(r.payload) }}</pre>
          </v-expansion-panel-text>
        </v-expansion-panel>
      </v-expansion-panels>

      <div v-if="!loadingRecords && records.length === 0" class="text-medium-emphasis text-caption py-2">
        No records for this entity.
      </div>
    </template>
  </BasePage>
</template>

<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue';
import { useRoute } from 'vue-router';
import BasePage from '../../components/BasePage.vue';
import type { RecordsSummary, RawRecordView } from '../../types';

const route = useRoute();
const id = computed(() => String(route.params.id));

const summary = ref<RecordsSummary | null>(null);
const records = ref<RawRecordView[]>([]);
const selectedType = ref<string | null>(null);
const error = ref<string | null>(null);
const loadingRecords = ref(false);

const title = computed(() => (summary.value ? `${summary.value.name} — records` : 'Records'));

// Route back to the connection's own page based on its type.
const backTo = computed(() => {
  switch (summary.value?.type) {
    case 'github':
      return '/integrations/github';
    case 'jira':
      return '/integrations/jira';
    case 'claude-code':
      return '/integrations/claude';
    default:
      return '/integrations';
  }
});

async function loadSummary() {
  try {
    summary.value = await fetch(`/api/integrations/${id.value}/records/summary`).then((r) => {
      if (!r.ok) throw new Error(`HTTP ${r.status}`);
      return r.json();
    });
    error.value = null;
    const types = summary.value?.entityTypes ?? [];
    selectedType.value = types.length > 0 ? types[0].entityType : null;
  } catch (e) {
    error.value = String(e);
  }
}

async function loadRecords() {
  if (!selectedType.value) {
    records.value = [];
    return;
  }
  loadingRecords.value = true;
  try {
    const params = new URLSearchParams({ entityType: selectedType.value, limit: '200' });
    records.value = await fetch(`/api/integrations/${id.value}/records?${params}`).then((r) => r.json());
  } catch (e) {
    error.value = String(e);
  } finally {
    loadingRecords.value = false;
  }
}

async function reload() {
  await loadSummary();
  await loadRecords();
}

watch(selectedType, loadRecords);

function fmt(ts: string | null | undefined) {
  return ts ? new Date(ts).toLocaleString() : '—';
}

function pretty(payload: unknown) {
  return JSON.stringify(payload, null, 2);
}

onMounted(loadSummary);
</script>

<style scoped>
.payload {
  background: rgba(255, 255, 255, 0.06);
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 8px;
  padding: 12px;
  font-size: 0.78rem;
  line-height: 1.4;
  overflow-x: auto;
  white-space: pre;
}
</style>
