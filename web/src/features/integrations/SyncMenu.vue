<template>
  <v-menu>
    <template #activator="{ props: menuProps }">
      <v-btn size="x-small" variant="text" append-icon="mdi-menu-down" v-bind="menuProps">Sync</v-btn>
    </template>
    <v-list density="compact" min-width="180">
      <v-list-item title="All entities" prepend-icon="mdi-sync" @click="run(null)" />
      <template v-if="integration.entities.length">
        <v-divider />
        <v-list-subheader>Just one entity</v-list-subheader>
        <v-list-item v-for="e in integration.entities" :key="e" :title="e" @click="run(e)" />
      </template>
    </v-list>
  </v-menu>
</template>

<script setup lang="ts">
// The per-integration Sync control: an "All entities" default plus one item per entity,
// so a single entity can be re-run on its own. Triggers are async, so refresh after a beat.
import type { Integration } from '../../types';
import { api } from '../../api';

const props = defineProps<{ integration: Integration }>();
const emit = defineEmits(['synced']);

async function run(entity: string | null) {
  const base = `/integrations/${props.integration.id}/sync`;
  const url = entity ? `${base}?entity=${encodeURIComponent(entity)}` : base;
  await api.url(url).post().res();
  setTimeout(() => emit('synced'), 1500);
}
</script>
