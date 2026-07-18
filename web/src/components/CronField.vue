<template>
  <div>
    <v-text-field
      :model-value="modelValue"
      label="Schedule (cron, optional)"
      placeholder="0 0/30 * * * ?"
      density="comfortable"
      persistent-hint
      hint="Quartz cron — 6 fields, seconds first. Blank = manual only."
      @update:model-value="$emit('update:modelValue', $event)"
    />
    <div class="text-caption text-medium-emphasis mt-1">
      Examples: <code>0 0/30 * * * ?</code> every 30 min · <code>0 0 * * * ?</code> hourly ·
      <code>0 0 9 * * ?</code> 9am daily ·
      <a :href="guideUrl" target="_blank" rel="noopener">cron guide ↗</a>
    </div>
  </div>
</template>

<script setup lang="ts">
// A cron input with an inline explanation and a link out, since not everyone
// knows cron syntax. Schedules use Quartz's 6-field format (seconds first).
defineProps<{ modelValue: string }>();
defineEmits(['update:modelValue']);

const guideUrl =
  'https://www.quartz-scheduler.org/documentation/quartz-2.3.0/tutorials/crontrigger.html';
</script>
