<template>
  <v-dialog :model-value="modelValue" max-width="580" @update:model-value="$emit('update:modelValue', $event)">
    <v-card>
      <v-card-title class="pt-4">
        {{ isEdit ? 'Edit GitHub connection' : 'Add GitHub connection' }}
      </v-card-title>

      <v-card-text>
        <div class="text-body-2 text-medium-emphasis mb-4">
          {{
            isEdit
              ? 'Update this repository connection. Changes save together.'
              : 'Connect one or more repositories. Each becomes its own row so you can manage them separately.'
          }}
        </div>

        <div v-if="error" class="text-error text-body-2 mb-3">{{ error }}</div>

        <v-combobox
          v-if="!isEdit"
          v-model="form.repos"
          label="Repositories"
          placeholder="owner/name — press Enter to add"
          multiple
          chips
          closable-chips
          density="comfortable"
          persistent-hint
          hint="e.g. acme-inc/api · add as many as you like"
        />
        <v-text-field
          v-else
          v-model="form.repo"
          label="Repository"
          placeholder="owner/name"
          density="comfortable"
          persistent-hint
          hint="e.g. acme-inc/api"
        />

        <CronField v-model="form.cron" class="mt-3" />

        <v-switch
          v-model="form.enabled"
          label="Enabled (sync on the schedule above)"
          color="primary"
          density="compact"
          hide-details
          class="mt-2"
        />

        <v-text-field
          v-model="form.credential"
          :label="isEdit ? 'GitHub token (leave blank to keep current)' : 'GitHub personal access token'"
          placeholder="ghp_…"
          type="password"
          density="comfortable"
          class="mt-3"
          persistent-hint
          :hint="credentialHint"
        />
      </v-card-text>

      <v-card-actions class="px-4 pb-4">
        <v-spacer />
        <v-btn variant="text" @click="close">Cancel</v-btn>
        <v-btn color="primary" variant="tonal" :loading="busy" :disabled="!canSave" @click="save">
          {{ isEdit ? 'Save' : 'Create' }}
        </v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>

<script setup lang="ts">
import { reactive, ref, computed, watch } from 'vue';
import CronField from '../../components/CronField.vue';
import type { Integration, GitHubConfig } from '../../types';

const props = defineProps<{
  modelValue: boolean;
  // When set, the dialog edits this integration; otherwise it creates new ones.
  integration?: Integration | null;
}>();
const emit = defineEmits(['update:modelValue', 'saved']);

const isEdit = computed(() => !!props.integration);
const busy = ref(false);
const error = ref<string | null>(null);

const form = reactive({
  repos: [] as string[],
  repo: '',
  cron: '',
  enabled: false,
  credential: ''
});

const credentialHint = computed(() =>
  isEdit.value && props.integration?.hasCredential
    ? 'A token is stored (encrypted). Leave blank to keep it.'
    : 'GitHub → Settings → Developer settings → Personal access tokens (repo read scope). Stored encrypted.'
);

const canSave = computed(() =>
  isEdit.value ? form.repo.trim().length > 0 : form.repos.length > 0
);

// Reset the form each time the dialog opens (prefilled in edit mode).
watch(
  () => props.modelValue,
  (open) => {
    if (!open) return;
    error.value = null;
    if (props.integration) {
      const config = props.integration.config as GitHubConfig | null;
      form.repo = config?.repos?.[0] ?? config?.org ?? props.integration.name;
      form.repos = [];
      form.cron = props.integration.scheduleCron ?? '';
      form.enabled = props.integration.enabled;
    } else {
      form.repos = [];
      form.repo = '';
      form.cron = '';
      form.enabled = false;
    }
    form.credential = '';
  }
);

function close() {
  emit('update:modelValue', false);
}

async function save() {
  busy.value = true;
  error.value = null;
  try {
    if (isEdit.value && props.integration) {
      const repo = form.repo.trim();
      const res = await fetch(`/api/integrations/github/${props.integration.id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          name: repo,
          cron: form.cron || null,
          enabled: form.enabled,
          org: null,
          repos: [repo],
          credential: form.credential || null
        })
      });
      if (!res.ok) throw new Error(await res.text());
    } else {
      // Fan out: one integration per repo so each is managed on its own row.
      const failures: string[] = [];
      for (const repo of form.repos.map((r) => r.trim()).filter(Boolean)) {
        const res = await fetch('/api/integrations/github', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            name: repo,
            cron: form.cron || null,
            enabled: form.enabled,
            org: null,
            repos: [repo],
            credential: form.credential || null
          })
        });
        if (!res.ok) failures.push(repo);
      }
      if (failures.length > 0) {
        throw new Error(`Could not add: ${failures.join(', ')} (already connected?)`);
      }
    }
    emit('saved');
    close();
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e);
  } finally {
    busy.value = false;
  }
}
</script>
