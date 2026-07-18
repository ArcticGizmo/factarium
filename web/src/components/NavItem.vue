<template>
  <v-list-item :to="to" :prepend-icon="icon" :title="title" />
</template>

<script setup lang="ts">
// A single left-nav entry. Nav items are authored explicitly (see App.vue) so
// the menu's order and visibility are independent of the route table.
defineProps<{
  to: string
  icon: string
  title: string
}>()
</script>
