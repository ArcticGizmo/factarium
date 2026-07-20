<template>
  <v-dialog :model-value="modelValue" max-width="600" @update:model-value="$emit('update:modelValue', $event)">
    <v-card>
      <v-card-title class="pt-4">
        {{ isEdit ? 'Edit Tempo connection' : 'Add Tempo connection' }}
      </v-card-title>

      <v-card-text>
        <div class="text-body-2 text-medium-emphasis mb-4">
          Connect Tempo Timesheets for one Jira project. Factarium replicates its worklogs (logged effort) from the
          "sync since" date onward. Your Tempo API token is encrypted before it is stored.
        </div>

        <div v-if="error" class="text-error text-body-2 mb-3">{{ error }}</div>

        <v-text-field
          v-model="form.projectKey"
          label="Project key"
          placeholder="QAI"
          density="comfortable"
          persistent-hint
          hint="The Jira project whose worklogs are synced (one project per connection)"
        />

        <div class="mt-4 mb-1 text-body-2">Sync since</div>
        <VueDatePicker
          v-model="form.syncSince"
          dark
          auto-apply
          :enable-time-picker="false"
          :max-date="today"
          format="yyyy-MM-dd"
          placeholder="Earliest work date to pull"
          class="sync-since mb-1"
        />
        <div class="text-caption text-medium-emphasis">
          Worklogs dated before this are skipped, so you don't pull years of history. Blank = everything.
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
          :label="isEdit ? 'Tempo API token (leave blank to keep current)' : 'Tempo API token'"
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
import { VueDatePicker } from '@vuepic/vue-datepicker';
import '@vuepic/vue-datepicker/dist/main.css';
import CronField from '../../components/CronField.vue';
import type { Integration, TempoConfig } from '../../types';
import { api } from '../../api';

const props = defineProps<{
  modelValue: boolean;
  // When set, the dialog edits this integration; otherwise it creates a new one.
  integration?: Integration | null;
}>();
const emit = defineEmits(['update:modelValue', 'saved']);

const isEdit = computed(() => !!props.integration);
const busy = ref(false);
const error = ref<string | null>(null);
// "Sync since" is a historical bound, so never let it point into the future.
const today = new Date();

const form = reactive({
  projectKey: '',
  syncSince: null as Date | null,
  cron: '',
  enabled: false,
  credential: ''
});

const credentialHint = computed(() =>
  isEdit.value && props.integration?.hasCredential
    ? 'A token is stored (encrypted). Leave blank to keep it.'
    : 'Create in Tempo → Settings → Data Access → API Integration. Stored encrypted.'
);

const canSave = computed(() => form.projectKey.trim().length > 0);

// Reset the form each time the dialog opens (prefilled in edit mode).
watch(
  () => props.modelValue,
  (open) => {
    if (!open) return;
    error.value = null;
    if (props.integration) {
      const config = props.integration.config as TempoConfig | null;
      form.projectKey = config?.projectKey ?? '';
      form.syncSince = config?.syncSince ? new Date(config.syncSince) : null;
      form.cron = props.integration.scheduleCron ?? '';
      form.enabled = props.integration.enabled;
    } else {
      form.projectKey = '';
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
  const projectKey = form.projectKey.trim();
  const body = {
    name: `Tempo · ${projectKey}`,
    cron: form.cron || null,
    enabled: form.enabled,
    projectKey,
    syncSince: form.syncSince ? startOfDay(form.syncSince) : null,
    credential: form.credential || null
  };
  try {
    // wretch rejects on non-2xx with the response body as the error message.
    if (isEdit.value && props.integration) {
      await api.url(`/integrations/tempo/${props.integration.id}`).json(body).put().res();
    } else {
      await api.url('/integrations/tempo').json(body).post().res();
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
.sync-since :deep(.dp__input) {
  font-size: 0.875rem;
  border-radius: 4px;
}
</style>
