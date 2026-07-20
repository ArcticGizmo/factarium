<template>
  <BasePage title="GitHub" subtitle="Repositories, pull requests, reviews & commits">
    <template #actions>
      <v-btn size="small" color="primary" variant="tonal" prepend-icon="mdi-plus" @click="openCreate">
        Add GitHub connection
      </v-btn>
      <v-btn size="small" variant="text" @click="load">Refresh</v-btn>
    </template>

    <div v-if="error" class="text-error mb-2">{{ error }}</div>

    <div class="text-body-2 text-medium-emphasis mb-3">
      Each row is a single repository, managed on its own. Add several at once and they fan out to a row each.
    </div>

    <v-table density="comfortable">
      <thead>
        <tr>
          <th>Repository</th>
          <th v-for="e in entityColumns" :key="e" class="entity-col">{{ e }}</th>
          <th>Enabled</th>
          <th>Schedule</th>
          <th>Last run</th>
          <th>Next run</th>
          <th></th>
        </tr>
      </thead>
      <tbody>
        <tr v-if="integrations.length === 0">
          <td :colspan="6 + entityColumns.length" class="text-medium-emphasis text-caption py-4">
            No repositories connected yet.
          </td>
        </tr>
        <tr v-for="i in integrations" :key="i.id">
          <td>{{ repoLabel(i) }}</td>
          <td v-for="e in entityColumns" :key="e" class="text-caption entity-col" :title="fmt(i.entityLatest[e])">
            {{ formatRelative(i.entityLatest[e]) }}
          </td>
          <td>
            <v-chip :color="i.enabled ? 'green' : 'grey'" size="x-small" variant="flat">
              {{ i.enabled ? 'on' : 'off' }}
            </v-chip>
          </td>
          <td class="text-caption">{{ i.scheduleCron || 'manual' }}</td>
          <td>
            <v-chip :color="statusColor[i.lastRunStatus] || 'grey'" size="x-small" variant="flat">{{
              i.lastRunStatus
            }}</v-chip>
            <div v-if="i.lastRunError" class="text-caption text-error">{{ i.lastRunError }}</div>
          </td>
          <td class="text-caption">{{ fmt(i.nextRunAt) }}</td>
          <td>
            <div class="d-flex ga-1">
              <v-btn size="x-small" variant="tonal" color="primary" @click="openEdit(i)">Edit</v-btn>
              <v-btn size="x-small" variant="text" :to="{ name: 'integration-records', params: { id: i.id } }">
                View records
              </v-btn>
              <SyncMenu :integration="i" @synced="load" />
              <PurgeMenu :integration="i" @purged="load" />
              <v-btn size="x-small" variant="text" color="red" @click="remove(i.id)">Delete</v-btn>
            </div>
          </td>
        </tr>
      </tbody>
    </v-table>

    <GitHubIntegrationDialog v-model="dialog" :integration="editing" @saved="load" />
  </BasePage>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import BasePage from '../../components/BasePage.vue';
import GitHubIntegrationDialog from './GitHubIntegrationDialog.vue';
import SyncMenu from './SyncMenu.vue';
import PurgeMenu from './PurgeMenu.vue';
import type { Integration, GitHubConfig } from '../../types';
import { useIntegrations } from './useIntegrations';
import { formatDateTime as fmt, formatRelative } from '../../utils/datetime';
import { api } from '../../api';

const { integrations, error, load } = useIntegrations('github');

// Entity types are the same across a type's connections; take them from the first row.
const entityColumns = computed(() => integrations.value[0]?.entities ?? []);

const dialog = ref(false);
const editing = ref<Integration | null>(null);

function openCreate() {
  editing.value = null;
  dialog.value = true;
}

function openEdit(i: Integration) {
  editing.value = i;
  dialog.value = true;
}

async function remove(id: string) {
  await api.url(`/integrations/${id}`).delete().res();
  await load();
}

function repoLabel(i: Integration): string {
  const config = i.config as GitHubConfig | null;
  return config?.repos?.[0] ?? config?.org ?? i.name;
}

const statusColor: Record<string, string> = {
  Success: 'green',
  Failed: 'red',
  Running: 'blue',
  Never: 'grey'
};

onMounted(load);
</script>

<style scoped>
/* Compact freshness columns so several fit without dominating the row. */
.entity-col {
  white-space: nowrap;
  font-size: 0.75rem;
}
</style>
