<template>
  <v-card :title="title" :subtitle="subtitle">
    <template v-if="$slots.actions" #append>
      <div class="d-flex align-center ga-2">
        <slot name="actions" />
      </div>
    </template>
    <v-card-text>
      <slot />
    </v-card-text>
  </v-card>
</template>

<script setup lang="ts">
// Shared page shell. Every routed page composes this and supplies its own
// title (and optional subtitle / header actions), so page identity lives with
// the page rather than in the route table.
defineProps<{
  title: string
  subtitle?: string
}>()
</script>
