<template>
  <v-table density="comfortable">
    <thead>
      <tr>
        <th>Name</th>
        <th>Enabled</th>
        <th style="width: 200px">Schedule (cron)</th>
        <th>Last run</th>
        <th>Next run</th>
        <th></th>
      </tr>
    </thead>
    <tbody>
      <tr v-if="integrations.length === 0">
        <td colspan="6" class="text-medium-emphasis text-caption py-4">Nothing connected yet.</td>
      </tr>
      <tr v-for="i in integrations" :key="i.id">
        <td>{{ i.name }}</td>
        <td><v-switch v-model="i.enabled" density="compact" hide-details color="primary" /></td>
        <td>
          <v-text-field
            v-model="i.scheduleCron"
            density="compact"
            hide-details
            placeholder="manual"
            style="max-width: 190px"
          />
        </td>
        <td>
          <v-chip :color="statusColor[i.lastRunStatus] || 'grey'" size="x-small" variant="flat">{{
            i.lastRunStatus
          }}</v-chip>
          <div v-if="i.lastRunError" class="text-caption text-error">{{ i.lastRunError }}</div>
        </td>
        <td class="text-caption">{{ fmt(i.nextRunAt) }}</td>
        <td>
          <div class="d-flex ga-1">
            <v-btn size="x-small" variant="tonal" color="primary" @click="save(i)">Save</v-btn>
            <v-btn size="x-small" variant="text" @click="syncNow(i.id)">Sync</v-btn>
            <v-btn size="x-small" variant="text" color="red" @click="remove(i.id)">Delete</v-btn>
          </div>
        </td>
      </tr>
    </tbody>
  </v-table>
</template>

<script setup lang="ts">
import type { Integration } from '../../types';
import { formatDateTime as fmt } from '../../utils/datetime';
import { api } from '../../api';

// Renders the rows for one integration type and owns the generic row actions
// (toggle/schedule save, manual sync, delete) which are the same across every
// integration type. Type-specific create forms live on each page.
defineProps<{ integrations: Integration[] }>();
const emit = defineEmits(['refresh']);

async function save(row: Integration) {
  await api
    .url(`/integrations/${row.id}`)
    .json({ enabled: row.enabled, cron: row.scheduleCron || null })
    .put()
    .res();
  emit('refresh');
}

async function syncNow(id: string) {
  await api.url(`/integrations/${id}/sync`).post().res();
  setTimeout(() => emit('refresh'), 1500);
}

async function remove(id: string) {
  await api.url(`/integrations/${id}`).delete().res();
  emit('refresh');
}

const statusColor: Record<string, string> = {
  Success: 'green',
  Failed: 'red',
  Running: 'blue',
  Never: 'grey',
  success: 'green',
  failed: 'red',
  never: 'grey'
};
</script>
