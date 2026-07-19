<template>
  <BasePage title="Jira" subtitle="Issues & status">
    <template #actions>
      <v-btn size="small" variant="text" @click="load">Refresh</v-btn>
    </template>

    <div v-if="error" class="text-error mb-2">{{ error }}</div>

    <div class="d-flex align-center justify-space-between">
      <div class="section-title">Connections</div>
      <v-btn size="small" variant="text" @click="form.show = !form.show">
        {{ form.show ? 'Cancel' : '+ Add Jira connection' }}
      </v-btn>
    </div>

    <v-expand-transition>
      <div v-if="form.show" class="add-form mb-3">
        <div class="text-body-2 text-medium-emphasis mb-3">
          Connect a Jira Cloud site. Factarium replicates issues and their status on the schedule below (or on demand).
          Your API token is encrypted before it is stored.
        </div>

        <v-row dense>
          <v-col cols="12" sm="6">
            <v-text-field
              v-model="form.name"
              label="Display name"
              placeholder="Acme Jira"
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
              label="Jira API token"
              placeholder="ATATT…"
              type="password"
              density="compact"
              persistent-hint
              hint="Create at id.atlassian.com → Security → API tokens. Stored encrypted."
            />
          </v-col>
          <v-col cols="12" sm="4" class="d-flex align-center pt-2">
            <v-btn color="primary" variant="tonal" :disabled="!form.name" @click="create"> Create connection </v-btn>
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
import { api } from '../../api';

const { integrations, error, load } = useIntegrations('jira');

const form = reactive({
  show: false,
  name: '',
  cron: '',
  baseUrl: '',
  email: '',
  projectKeys: '',
  credential: ''
});

async function create() {
  await api
    .url('/integrations/jira')
    .json({
      name: form.name,
      cron: form.cron || null,
      enabled: !!form.cron,
      baseUrl: form.baseUrl || null,
      email: form.email || null,
      projectKeys: splitCsv(form.projectKeys),
      jql: null,
      credential: form.credential || null
    })
    .post()
    .res();
  Object.assign(form, {
    show: false,
    name: '',
    cron: '',
    baseUrl: '',
    email: '',
    projectKeys: '',
    credential: ''
  });
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
