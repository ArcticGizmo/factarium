<template>
  <v-list-group :value="title">
    <template #activator="{ props }">
      <v-list-item v-bind="props" :prepend-icon="icon" :title="title" />
    </template>
    <slot />
  </v-list-group>
</template>

<script setup lang="ts">
// A collapsible left-nav parent whose children (NavItems) are supplied via the
// default slot. Like NavItem, groups are authored explicitly in App.vue so the
// menu structure stays independent of the route table.
defineProps<{
  icon: string;
  title: string;
}>();
</script>
