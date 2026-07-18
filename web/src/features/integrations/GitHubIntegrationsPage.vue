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
          <th>Enabled</th>
          <th>Schedule</th>
          <th>Last run</th>
          <th>Next run</th>
          <th></th>
        </tr>
      </thead>
      <tbody>
        <tr v-if="integrations.length === 0">
          <td colspan="6" class="text-medium-emphasis text-caption py-4">No repositories connected yet.</td>
        </tr>
        <tr v-for="i in integrations" :key="i.id">
          <td>{{ repoLabel(i) }}</td>
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
              <v-btn size="x-small" variant="text" @click="syncNow(i.id)">Sync</v-btn>
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
import { ref, onMounted } from 'vue';
import BasePage from '../../components/BasePage.vue';
import GitHubIntegrationDialog from './GitHubIntegrationDialog.vue';
import type { Integration, GitHubConfig } from '../../types';
import { useIntegrations } from './useIntegrations';

const { integrations, error, load } = useIntegrations('github');

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

async function syncNow(id: string) {
  await fetch(`/api/integrations/${id}/sync`, { method: 'POST' });
  setTimeout(load, 1500);
}

async function remove(id: string) {
  await fetch(`/api/integrations/${id}`, { method: 'DELETE' });
  await load();
}

function repoLabel(i: Integration): string {
  const config = i.config as GitHubConfig | null;
  return config?.repos?.[0] ?? config?.org ?? i.name;
}

function fmt(ts: string | null | undefined) {
  return ts ? new Date(ts).toLocaleString() : '—';
}

const statusColor: Record<string, string> = {
  Success: 'green',
  Failed: 'red',
  Running: 'blue',
  Never: 'grey'
};

onMounted(load);
</script>
