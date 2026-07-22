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

        <div class="mt-4 mb-1 text-body-2">Sync since</div>
        <VueDatePicker
          v-model="form.syncSince"
          dark
          auto-apply
          :enable-time-picker="false"
          :max-date="today"
          format="yyyy-MM-dd"
          placeholder="Earliest history to pull"
          class="sync-since mb-1"
        />
        <div class="text-caption text-medium-emphasis">
          Commits before this date — and pull requests with no activity since it — are skipped, so you
          don't replicate years of history. Blank = everything. Only bounds the first sync; later syncs
          follow the cursor.
        </div>

        <CronField v-model="form.cron" class="mt-4" />

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
          placeholder="github_pat_…"
          type="password"
          density="comfortable"
          class="mt-3"
          hide-details
        />
        <div class="text-caption text-medium-emphasis mt-2">
          <template v-if="isEdit && integration?.hasCredential">
            A token is stored (encrypted). Leave blank to keep it.
          </template>
          <template v-else>
            Create a
            <a href="https://github.com/settings/personal-access-tokens" target="_blank" rel="noopener noreferrer">
              fine-grained personal access token</a>
            with these read-only repository permissions:
            <ul class="perm-list">
              <li>Metadata — read only</li>
              <li>Contents — read only</li>
              <li>Pull requests — read only</li>
            </ul>
            Stored encrypted.
          </template>
        </div>
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
import { VueDatePicker } from '@vuepic/vue-datepicker';
import '@vuepic/vue-datepicker/dist/main.css';
import CronField from '../../components/CronField.vue';
import type { Integration, GitHubConfig } from '../../types';
import { api } from '../../api';

const props = defineProps<{
  modelValue: boolean;
  // When set, the dialog edits this integration; otherwise it creates new ones.
  integration?: Integration | null;
}>();
const emit = defineEmits(['update:modelValue', 'saved']);

const isEdit = computed(() => !!props.integration);
const busy = ref(false);
const error = ref<string | null>(null);
// "Sync since" is a historical bound, so never let it point into the future.
const today = new Date();

const form = reactive({
  repos: [] as string[],
  repo: '',
  syncSince: null as Date | null,
  cron: '',
  enabled: false,
  credential: ''
});

const canSave = computed(() => (isEdit.value ? form.repo.trim().length > 0 : form.repos.length > 0));

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
      form.syncSince = config?.syncSince ? new Date(config.syncSince) : null;
      form.cron = props.integration.scheduleCron ?? '';
      form.enabled = props.integration.enabled;
    } else {
      form.repos = [];
      form.repo = '';
      form.syncSince = null;
      form.cron = '';
      form.enabled = false;
    }
    form.credential = '';
  }
);

function close() {
  emit('update:modelValue', false);
}

// Inclusive-from-midnight in local time, matching the date-only picker.
function startOfDay(d: Date): string {
  const x = new Date(d);
  x.setHours(0, 0, 0, 0);
  return x.toISOString();
}

async function save() {
  busy.value = true;
  error.value = null;
  const syncSince = form.syncSince ? startOfDay(form.syncSince) : null;
  try {
    if (isEdit.value && props.integration) {
      const repo = form.repo.trim();
      // wretch rejects on non-2xx with the response body as the error message.
      await api
        .url(`/integrations/github/${props.integration.id}`)
        .json({
          name: repo,
          cron: form.cron || null,
          enabled: form.enabled,
          org: null,
          repos: [repo],
          syncSince,
          credential: form.credential || null
        })
        .put()
        .res();
    } else {
      // Fan out: one integration per repo so each is managed on its own row.
      const failures: string[] = [];
      for (const repo of form.repos.map((r) => r.trim()).filter(Boolean)) {
        try {
          await api
            .url('/integrations/github')
            .json({
              name: repo,
              cron: form.cron || null,
              enabled: form.enabled,
              org: null,
              repos: [repo],
              syncSince,
              credential: form.credential || null
            })
            .post()
            .res();
        } catch {
          failures.push(repo);
        }
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

<style scoped>
.perm-list {
  margin: 4px 0 4px 20px;
  padding: 0;
}
.perm-list li {
  list-style: disc;
}
a {
  color: rgb(var(--v-theme-primary));
  text-decoration: underline;
}
.sync-since :deep(.dp__input) {
  font-size: 0.875rem;
  border-radius: 4px;
}
</style>
