<script setup>
import { ref, onMounted } from 'vue'

const integrations = ref([])
const loading = ref(false)
const error = ref(null)

const statusColor = {
  Success: 'green',
  Failed: 'red',
  Running: 'blue',
  Never: 'grey',
}

async function load() {
  loading.value = true
  error.value = null
  try {
    integrations.value = await fetch('/api/integrations').then((r) => r.json())
  } catch (e) {
    error.value = String(e)
  } finally {
    loading.value = false
  }
}

async function syncNow(id) {
  await fetch(`/api/integrations/${id}/sync`, { method: 'POST' })
  // Give the background job a moment, then refresh to show the new status.
  setTimeout(load, 1500)
}

function fmt(ts) {
  return ts ? new Date(ts).toLocaleString() : '—'
}

onMounted(load)
</script>

<template>
  <v-card title="Integrations & schedules">
    <template #append>
      <v-btn size="small" variant="text" :loading="loading" @click="load">Refresh</v-btn>
    </template>
    <v-card-text>
      <div v-if="error" class="text-error mb-2">{{ error }}</div>
      <v-table density="comfortable">
        <thead>
          <tr>
            <th>Name</th>
            <th>Type</th>
            <th>Schedule</th>
            <th>Last run</th>
            <th>Records</th>
            <th>Next run</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="i in integrations" :key="i.id">
            <td>{{ i.name }}</td>
            <td>{{ i.type }}</td>
            <td>
              <span v-if="i.scheduleCron">{{ i.scheduleCron }}</span>
              <span v-else class="text-medium-emphasis">manual</span>
            </td>
            <td>
              <v-chip :color="statusColor[i.lastRunStatus] || 'grey'" size="x-small" variant="flat">
                {{ i.lastRunStatus }}
              </v-chip>
              <div v-if="i.lastRunError" class="text-caption text-error">{{ i.lastRunError }}</div>
            </td>
            <td>{{ i.lastRunRecordsWritten }}</td>
            <td class="text-caption">{{ fmt(i.nextRunAt) }}</td>
            <td>
              <v-btn size="x-small" color="primary" variant="tonal" @click="syncNow(i.id)">
                Sync now
              </v-btn>
            </td>
          </tr>
          <tr v-if="!integrations.length">
            <td colspan="7" class="text-center text-medium-emphasis">
              No integrations yet.
            </td>
          </tr>
        </tbody>
      </v-table>
    </v-card-text>
  </v-card>
</template>
