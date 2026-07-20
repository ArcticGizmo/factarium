<template>
  <v-menu>
    <template #activator="{ props: menuProps }">
      <v-btn size="x-small" variant="text" color="red" append-icon="mdi-menu-down" v-bind="menuProps">Purge</v-btn>
    </template>
    <v-list density="compact" min-width="180">
      <v-list-item title="All entities" prepend-icon="mdi-delete-sweep" @click="confirm(null)" />
      <template v-if="integration.entities.length">
        <v-divider />
        <v-list-subheader>Just one entity</v-list-subheader>
        <v-list-item v-for="e in integration.entities" :key="e" :title="e" @click="confirm(e)" />
      </template>
    </v-list>
  </v-menu>

  <!-- Purge is destructive and can't be undone, so confirm before deleting. -->
  <v-dialog v-model="dialogOpen" max-width="480">
    <v-card>
      <v-card-title class="pt-4">Purge {{ target ?? 'all' }} records?</v-card-title>
      <v-card-text>
        This permanently deletes
        <strong>{{ target ? `all ${target}` : "every entity's" }}</strong> bronze records for
        <strong>{{ integration.name }}</strong> and resets the matching sync position, so the next sync re-fetches them
        from scratch. This can't be undone.
        <div v-if="errorText" class="text-error mt-2">{{ errorText }}</div>
      </v-card-text>
      <v-card-actions class="px-4 pb-4">
        <v-spacer />
        <v-btn variant="text" @click="dialogOpen = false">Cancel</v-btn>
        <v-btn color="red" variant="tonal" :loading="purging" @click="purge">Purge</v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>

<script setup lang="ts">
// The per-integration Purge control, mirroring SyncMenu: an "All entities" option plus one
// item per entity, each confirming before it deletes that entity's bronze records and resets
// its sync cursor. Purges are async, so refresh after a beat.
import { ref } from 'vue';
import type { Integration } from '../../types';
import { api } from '../../api';

const props = defineProps<{ integration: Integration }>();
const emit = defineEmits(['purged']);

const dialogOpen = ref(false);
const purging = ref(false);
const errorText = ref<string | null>(null);
// null => every entity; otherwise the single entity type to purge.
const target = ref<string | null>(null);

function confirm(entity: string | null) {
  target.value = entity;
  errorText.value = null;
  dialogOpen.value = true;
}

async function purge() {
  purging.value = true;
  errorText.value = null;
  try {
    // The backend purge is per-entity (entityType is required), so "all" loops the entities.
    const entities = target.value ? [target.value] : props.integration.entities;
    for (const e of entities) {
      await api
        .url(`/integrations/${props.integration.id}/records?entityType=${encodeURIComponent(e)}`)
        .delete()
        .res();
    }
    dialogOpen.value = false;
    setTimeout(() => emit('purged'), 500);
  } catch (e) {
    errorText.value = String(e);
  } finally {
    purging.value = false;
  }
}
</script>
