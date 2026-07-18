<script setup>
import { ref, reactive, onMounted } from 'vue'

const emit = defineEmits(['changed'])

const integrations = ref([])
const steps = ref([])
const error = ref(null)
const busy = ref(false)
const isDev = ref(false)

const form = reactive({
  show: false,
  type: 'github',
  name: '',
  cron: '',
  org: '',
  repos: '',
  baseUrl: '',
  email: '',
  projectKeys: '',
  credential: '',
})

async function load() {
  try {
    ;[integrations.value, steps.value] = await Promise.all([
      fetch('/api/integrations').then((r) => r.json()),
      fetch('/api/pipeline').then((r) => r.json()),
    ])
    error.value = null
  } catch (e) {
    error.value = String(e)
  }
}

async function save(row) {
  await fetch(`/api/integrations/${row.id}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ enabled: row.enabled, cron: row.scheduleCron || null }),
  })
  await refresh()
}

async function syncNow(id) {
  await fetch(`/api/integrations/${id}/sync`, { method: 'POST' })
  setTimeout(refresh, 1500)
}

async function remove(id) {
  await fetch(`/api/integrations/${id}`, { method: 'DELETE' })
  await refresh()
}

async function runPipeline() {
  busy.value = true
  try {
    await fetch('/api/pipeline/run', { method: 'POST' })
    await refresh()
  } finally {
    busy.value = false
  }
}

async function createIntegration() {
  const settings =
    form.type === 'github'
      ? { org: form.org || null, repos: form.repos || null }
      : { baseUrl: form.baseUrl || null, email: form.email || null, projectKeys: form.projectKeys || null }

  await fetch('/api/integrations', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      type: form.type,
      name: form.name,
      cron: form.cron || null,
      enabled: !!form.cron,
      settings,
      credential: form.credential || null,
    }),
  })
  Object.assign(form, { show: false, name: '', cron: '', org: '', repos: '', baseUrl: '', email: '', projectKeys: '', credential: '' })
  await refresh()
}

async function refresh() {
  await load()
  emit('changed')
}

function fmt(ts) {
  return ts ? new Date(ts).toLocaleString() : '—'
}

const statusColor = { Success: 'green', Failed: 'red', Running: 'blue', Never: 'grey', success: 'green', failed: 'red', never: 'grey' }

async function resetDatabase() {
  if (!window.confirm('Clear ALL synced data (integrations, raw records, canonical, metrics, people)? This cannot be undone.')) {
    return
  }
  busy.value = true
  try {
    await fetch('/api/debug/reset', { method: 'POST' })
    await refresh()
  } finally {
    busy.value = false
  }
}

onMounted(async () => {
  await load()
  try {
    const health = await fetch('/api/health').then((r) => r.json())
    isDev.value = health.environment === 'Development'
  } catch {
    isDev.value = false
  }
})
</script>

<template>
  <v-card title="Sources, schedules & pipeline">
    <template #append>
      <v-btn
        v-if="isDev"
        size="small"
        variant="tonal"
        color="red"
        :loading="busy"
        prepend-icon="mdi-delete-alert"
        @click="resetDatabase"
      >
        Clear database (debug)
      </v-btn>
      <v-btn size="small" variant="text" @click="load">Refresh</v-btn>
    </template>
    <v-card-text>
      <div v-if="error" class="text-error mb-2">{{ error }}</div>

      <!-- Pipeline steps -->
      <div class="section-title">Pipeline</div>
      <div class="d-flex align-center flex-wrap ga-2 mb-4">
        <v-chip v-for="s in steps" :key="s.name" :color="statusColor[s.lastStatus] || 'grey'" variant="flat" size="small">
          {{ s.name }} · {{ s.lastStatus }} · {{ s.lastItemsProcessed }} @ {{ fmt(s.lastRunAt) }}
        </v-chip>
        <v-btn size="small" color="primary" variant="tonal" :loading="busy" @click="runPipeline">
          Run pipeline now
        </v-btn>
      </div>

      <!-- Integrations -->
      <div class="d-flex align-center justify-space-between">
        <div class="section-title">Sources</div>
        <v-btn size="small" variant="text" @click="form.show = !form.show">
          {{ form.show ? 'Cancel' : '+ Add source' }}
        </v-btn>
      </div>

      <v-expand-transition>
        <div v-if="form.show" class="add-form mb-3">
          <div class="d-flex flex-wrap ga-2">
            <v-select v-model="form.type" :items="['github', 'jira']" label="Type" density="compact" hide-details style="max-width: 130px" />
            <v-text-field v-model="form.name" label="Name" density="compact" hide-details style="max-width: 200px" />
            <v-text-field v-model="form.cron" label="Cron (optional)" placeholder="0 0/30 * * * ?" density="compact" hide-details style="max-width: 200px" />
          </div>
          <div class="d-flex flex-wrap ga-2 mt-2">
            <template v-if="form.type === 'github'">
              <v-text-field v-model="form.org" label="org" density="compact" hide-details style="max-width: 200px" />
              <v-text-field v-model="form.repos" label="repos (csv owner/name)" density="compact" hide-details style="max-width: 260px" />
            </template>
            <template v-else>
              <v-text-field v-model="form.baseUrl" label="baseUrl" density="compact" hide-details style="max-width: 240px" />
              <v-text-field v-model="form.email" label="email" density="compact" hide-details style="max-width: 200px" />
              <v-text-field v-model="form.projectKeys" label="projectKeys (csv)" density="compact" hide-details style="max-width: 200px" />
            </template>
            <v-text-field v-model="form.credential" label="token (stored encrypted)" type="password" density="compact" hide-details style="max-width: 240px" />
            <v-btn color="primary" variant="tonal" @click="createIntegration">Create</v-btn>
          </div>
        </div>
      </v-expand-transition>

      <v-table density="comfortable">
        <thead>
          <tr>
            <th>Name</th>
            <th>Type</th>
            <th>Enabled</th>
            <th style="width: 200px">Schedule (cron)</th>
            <th>Last run</th>
            <th>Next run</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="i in integrations" :key="i.id">
            <td>{{ i.name }}</td>
            <td>{{ i.type }}</td>
            <td><v-switch v-model="i.enabled" density="compact" hide-details color="primary" /></td>
            <td>
              <v-text-field v-model="i.scheduleCron" density="compact" hide-details placeholder="manual" style="max-width: 190px" />
            </td>
            <td>
              <v-chip :color="statusColor[i.lastRunStatus] || 'grey'" size="x-small" variant="flat">{{ i.lastRunStatus }}</v-chip>
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
    </v-card-text>
  </v-card>
</template>

<style scoped>
.section-title {
  font-size: 0.9rem;
  font-weight: 600;
  margin: 4px 0 8px;
}
.add-form {
  padding: 12px;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 8px;
}
</style>
