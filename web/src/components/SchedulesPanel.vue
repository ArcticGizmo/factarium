<template>
  <BasePage title="Sources" subtitle="Schedules & pipeline">
    <template #actions>
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
        <div class="text-body-2 text-medium-emphasis mb-3">
          Connect a source. Factarium replicates its data on the schedule below (or on demand), then transforms and
          aggregates it into the dashboards. Your token is encrypted before it is stored.
        </div>

        <v-row dense>
          <v-col cols="12" sm="4">
            <v-select
              v-model="form.type"
              :items="typeOptions"
              label="Source type"
              density="compact"
              persistent-hint
              hint="What you're connecting to"
            />
          </v-col>
          <v-col cols="12" sm="4">
            <v-text-field
              v-model="form.name"
              label="Display name"
              :placeholder="form.type === 'github' ? 'Acme GitHub' : 'Acme Jira'"
              density="compact"
              persistent-hint
              hint="A friendly label shown in this list"
            />
          </v-col>
          <v-col cols="12" sm="4">
            <v-text-field
              v-model="form.cron"
              label="Schedule (cron, optional)"
              placeholder="0 0/30 * * * ?"
              density="compact"
              persistent-hint
              hint="Blank = manual only · e.g. 0 0/30 * * * ? = every 30 min"
            />
          </v-col>
        </v-row>

        <v-row v-if="form.type === 'github'" dense>
          <v-col cols="12" sm="6">
            <v-text-field
              v-model="form.org"
              label="Organization or user"
              placeholder="acme-inc"
              density="compact"
              persistent-hint
              hint="Syncs every repo in this GitHub org/user"
            />
          </v-col>
          <v-col cols="12" sm="6">
            <v-text-field
              v-model="form.repos"
              label="Specific repos (optional)"
              placeholder="acme-inc/api, acme-inc/web"
              density="compact"
              persistent-hint
              hint="owner/name, comma-separated — instead of, or in addition to, an org"
            />
          </v-col>
        </v-row>

        <v-row v-else dense>
          <v-col cols="12" sm="4">
            <v-text-field
              v-model="form.baseUrl"
              label="Jira site URL"
              placeholder="https://acme.atlassian.net"
              density="compact"
              persistent-hint
              hint="Your Atlassian Cloud site"
            />
          </v-col>
          <v-col cols="12" sm="4">
            <v-text-field
              v-model="form.email"
              label="Account email"
              placeholder="you@acme.com"
              density="compact"
              persistent-hint
              hint="Atlassian login email (paired with the token to authenticate)"
            />
          </v-col>
          <v-col cols="12" sm="4">
            <v-text-field
              v-model="form.projectKeys"
              label="Project keys (optional)"
              placeholder="QAI, OPS"
              density="compact"
              persistent-hint
              hint="Limit to these projects · blank = everything you can see"
            />
          </v-col>
        </v-row>

        <v-row dense align="start">
          <v-col cols="12" sm="8">
            <v-text-field
              v-model="form.credential"
              :label="form.type === 'github' ? 'GitHub personal access token' : 'Jira API token'"
              :placeholder="form.type === 'github' ? 'ghp_…' : 'ATATT…'"
              type="password"
              density="compact"
              persistent-hint
              :hint="credentialHint"
            />
          </v-col>
          <v-col cols="12" sm="4" class="d-flex align-center pt-2">
            <v-btn color="primary" variant="tonal" :disabled="!form.name" @click="createIntegration">
              Create source
            </v-btn>
          </v-col>
        </v-row>
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
  </BasePage>
</template>

<script setup lang="ts">
import { ref, reactive, computed, onMounted } from 'vue';
import BasePage from './BasePage.vue';
import type { Integration, PipelineStep } from '../types';

const emit = defineEmits(['changed']);

const typeOptions = [
  { title: 'GitHub — repos, PRs, commits, reviews', value: 'github' },
  { title: 'Jira — issues & status', value: 'jira' }
];

const credentialHint = computed(() =>
  form.type === 'github'
    ? 'GitHub → Settings → Developer settings → Personal access tokens (repo read scope). Stored encrypted.'
    : 'Create at id.atlassian.com → Security → API tokens. Stored encrypted.'
);

const integrations = ref<Integration[]>([]);
const steps = ref<PipelineStep[]>([]);
const error = ref<string | null>(null);
const busy = ref(false);
const isDev = ref(false);

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
  credential: ''
});

async function load() {
  try {
    [integrations.value, steps.value] = await Promise.all([
      fetch('/api/integrations').then((r) => r.json()),
      fetch('/api/pipeline').then((r) => r.json())
    ]);
    error.value = null;
  } catch (e) {
    error.value = String(e);
  }
}

async function save(row: Integration) {
  await fetch(`/api/integrations/${row.id}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ enabled: row.enabled, cron: row.scheduleCron || null })
  });
  await refresh();
}

async function syncNow(id: number) {
  await fetch(`/api/integrations/${id}/sync`, { method: 'POST' });
  setTimeout(refresh, 1500);
}

async function remove(id: number) {
  await fetch(`/api/integrations/${id}`, { method: 'DELETE' });
  await refresh();
}

async function runPipeline() {
  busy.value = true;
  try {
    await fetch('/api/pipeline/run', { method: 'POST' });
    await refresh();
  } finally {
    busy.value = false;
  }
}

async function createIntegration() {
  const settings =
    form.type === 'github'
      ? { org: form.org || null, repos: form.repos || null }
      : { baseUrl: form.baseUrl || null, email: form.email || null, projectKeys: form.projectKeys || null };

  await fetch('/api/integrations', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      type: form.type,
      name: form.name,
      cron: form.cron || null,
      enabled: !!form.cron,
      settings,
      credential: form.credential || null
    })
  });
  Object.assign(form, {
    show: false,
    name: '',
    cron: '',
    org: '',
    repos: '',
    baseUrl: '',
    email: '',
    projectKeys: '',
    credential: ''
  });
  await refresh();
}

async function refresh() {
  await load();
  emit('changed');
}

function fmt(ts: string | null | undefined) {
  return ts ? new Date(ts).toLocaleString() : '—';
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

async function resetDatabase() {
  if (
    !window.confirm(
      'Clear ALL synced data (integrations, raw records, canonical, metrics, people)? This cannot be undone.'
    )
  ) {
    return;
  }
  busy.value = true;
  try {
    await fetch('/api/debug/reset', { method: 'POST' });
    await refresh();
  } finally {
    busy.value = false;
  }
}

onMounted(async () => {
  await load();
  try {
    const health = await fetch('/api/health').then((r) => r.json());
    isDev.value = health.environment === 'Development';
  } catch {
    isDev.value = false;
  }
});
</script>

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
