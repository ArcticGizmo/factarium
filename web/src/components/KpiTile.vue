<template>
  <div class="tile">
    <div class="d-flex align-start justify-space-between mb-1" style="gap: 8px">
      <div class="tile-label">{{ label }}</div>
      <v-chip
        v-if="targetChip"
        :color="targetChip.pass ? 'green' : 'amber'"
        size="x-small"
        variant="tonal"
        class="flex-shrink-0"
      >
        <v-icon start size="x-small" :icon="targetChip.pass ? 'mdi-check' : 'mdi-alert-outline'" />
        {{ targetChip.label }}
      </v-chip>
    </div>

    <div class="tile-value">{{ formatted }}</div>

    <div class="tile-foot">
      <span v-if="delta" class="delta" :class="delta.cls">
        <v-icon size="12" :icon="delta.icon" />{{ delta.text }}
      </span>
      <span v-else />

      <svg
        v-if="sparkPoints"
        class="spark"
        :viewBox="`0 0 ${SPARK_W} ${SPARK_H}`"
        preserveAspectRatio="none"
      >
        <polyline :points="sparkPoints" fill="none" :stroke="palette.muted" stroke-width="1.5" />
      </svg>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import { palette } from '../charts';

const props = withDefaults(
  defineProps<{
    label: string;
    value: number;
    unit?: string;
    // Prior-period value for the "vs prev" delta (null/undefined = no delta shown).
    previous?: number | null;
    // For duration/latency metrics a fall is an improvement; flips delta colouring.
    lowerIsBetter?: boolean;
    // Values for the mini sparkline (current window series).
    spark?: number[];
    // Optional target: pass = comparison against `value` (or `compareValue`) in `direction`.
    target?: { value: number; direction: 'gte' | 'lte'; label: string } | null;
    // Value used for the target comparison when it differs from the displayed value
    // (e.g. a per-week rate shown as a window total). Defaults to `value`.
    compareValue?: number;
  }>(),
  { unit: '', previous: null, lowerIsBetter: false, spark: undefined, target: null, compareValue: undefined }
);

const SPARK_W = 120;
const SPARK_H = 28;

function round(n: number) {
  return Math.round(n * 10) / 10;
}

const formatted = computed(() => `${round(props.value)}${props.unit}`);

const delta = computed(() => {
  if (props.previous === null || props.previous === undefined) return null;
  const change = props.value - props.previous;
  if (Math.abs(change) < 0.05) {
    return { cls: 'text-medium-emphasis', icon: 'mdi-minus', text: 'no change vs prev' };
  }
  const good = props.lowerIsBetter ? change < 0 : change > 0;
  const pct = props.previous !== 0 ? Math.abs(Math.round((change / props.previous) * 100)) : null;
  const magnitude = pct !== null ? `${pct}%` : `${round(Math.abs(change))}${props.unit}`;
  return {
    cls: good ? 'text-success' : 'text-error',
    icon: change > 0 ? 'mdi-arrow-up' : 'mdi-arrow-down',
    text: `${magnitude} vs prev`
  };
});

const targetChip = computed(() => {
  const t = props.target;
  if (!t) return null;
  const against = props.compareValue ?? props.value;
  const pass = t.direction === 'gte' ? against >= t.value : against <= t.value;
  return { pass, label: t.label };
});

// Normalise the spark series into a polyline across the fixed viewBox.
const sparkPoints = computed(() => {
  const s = props.spark;
  if (!s || s.length < 2) return null;
  const min = Math.min(...s);
  const max = Math.max(...s);
  const range = max - min || 1;
  const step = SPARK_W / (s.length - 1);
  return s
    .map((v, i) => {
      const x = i * step;
      // Leave 2px padding top/bottom so peaks aren't clipped.
      const y = SPARK_H - 2 - ((v - min) / range) * (SPARK_H - 4);
      return `${round(x)},${round(y)}`;
    })
    .join(' ');
});
</script>

<style scoped>
.tile {
  padding: 12px 16px;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 8px;
  height: 100%;
}
.tile-label {
  font-size: 0.75rem;
  color: #898781;
  line-height: 1.25;
  /* Wrap to at most two lines rather than truncating to an unreadable stub. */
  display: -webkit-box;
  -webkit-line-clamp: 2;
  line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
  min-height: 1.9em;
}
.tile-value {
  font-size: 1.6rem;
  font-weight: 600;
  line-height: 1.2;
}
.tile-foot {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  margin-top: 6px;
  min-height: 20px;
}
.delta {
  font-size: 0.72rem;
  display: inline-flex;
  align-items: center;
  gap: 2px;
  white-space: nowrap;
}
.spark {
  width: 60px;
  height: 20px;
  flex-shrink: 0;
  opacity: 0.7;
}
</style>
