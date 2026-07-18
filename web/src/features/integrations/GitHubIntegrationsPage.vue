<template>
  <BasePage title="GitHub" subtitle="Repositories, pull requests, reviews & commits">
    <template #actions>
      <v-btn size="small" variant="text" @click="load">Refresh</v-btn>
    </template>

    <div v-if="error" class="text-error mb-2">{{ error }}</div>

    <div class="d-flex align-center justify-space-between">
      <div class="section-title">Connections</div>
      <v-btn size="small" variant="text" @click="form.show = !form.show">
        {{ form.show ? 'Cancel' : '+ Add GitHub connection' }}
      </v-btn>
    </div>

    <v-expand-transition>
      <div v-if="form.show" class="add-form mb-3">
        <div class="text-body-2 text-medium-emphasis mb-3">
          Connect a GitHub org and/or specific repos. Factarium replicates repositories, pull requests, reviews and
          commits on the schedule below (or on demand). Your token is encrypted before it is stored.
        </div>

        <v-row dense>
          <v-col cols="12" sm="6">
            <v-text-field
              v-model="form.name"
              label="Display name"
              placeholder="Acme GitHub"
              density="compact"
              persistent-hint
              hint="A friendly label shown in this list"
            />
          </v-col>
          <v-col cols="12" sm="6">
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

        <v-row dense>
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

        <v-row dense align="start">
          <v-col cols="12" sm="8">
            <v-text-field
              v-model="form.credential"
              label="GitHub personal access token"
              placeholder="ghp_…"
              type="password"
              density="compact"
              persistent-hint
              hint="GitHub → Settings → Developer settings → Personal access tokens (repo read scope). Stored encrypted."
            />
          </v-col>
          <v-col cols="12" sm="4" class="d-flex align-center pt-2">
            <v-btn color="primary" variant="tonal" :disabled="!form.name" @click="create">
              Create connection
            </v-btn>
          </v-col>
        </v-row>
      </div>
    </v-expand-transition>

    <IntegrationsTable :integrations="integrations" @refresh="load" />
  </BasePage>
</template>

<script setup lang="ts">
import { reactive, onMounted } from 'vue';
import BasePage from '../../components/BasePage.vue';
import IntegrationsTable from './IntegrationsTable.vue';
import { useIntegrations, splitCsv } from './useIntegrations';

const { integrations, error, load } = useIntegrations('github');

const form = reactive({
  show: false,
  name: '',
  cron: '',
  org: '',
  repos: '',
  credential: ''
});

async function create() {
  await fetch('/api/integrations/github', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      name: form.name,
      cron: form.cron || null,
      enabled: !!form.cron,
      org: form.org || null,
      repos: splitCsv(form.repos),
      credential: form.credential || null
    })
  });
  Object.assign(form, { show: false, name: '', cron: '', org: '', repos: '', credential: '' });
  await load();
}

onMounted(load);
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
