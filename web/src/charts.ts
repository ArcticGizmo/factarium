// Chart styling shared across dashboards. Colors are the validated dataviz
// reference palette (dark steps — the app runs a dark Vuetify theme). Categorical
// slots are assigned in fixed order: slot 1 blue, slot 2 green.
import type { EChartsOption } from 'echarts';
import type { DayPoint, LabelValue } from './types';

export interface Series {
  name: string;
  points: DayPoint[];
}

export const palette = {
  series: ['#3987e5', '#008300'],
  // Categorical slots (dark steps), in fixed CVD-safe order — validated as a stacked-bar
  // pairlist (worst adjacent ΔE 8.4). Used with 2px surface gaps + a legend as the required
  // secondary encoding. A neutral grey carries a folded "Other".
  categorical: ['#3987e5', '#008300', '#d55181', '#c98500', '#199e70', '#d95926', '#9085e9', '#e66767'],
  other: '#6b6a65',
  surface: '#1a1a19',
  text: '#c3c2b7',
  muted: '#898781',
  grid: '#2c2c2a'
};

const axisBase = {
  axisLine: { lineStyle: { color: palette.grid } },
  axisTick: { show: false },
  axisLabel: { color: palette.muted }
};

// Align multiple day/value series onto one sorted union-of-days x-axis.
export function alignSeries(seriesList: Series[]) {
  const days = [...new Set(seriesList.flatMap((s) => s.points.map((p) => p.day)))].sort();
  const index = new Map(days.map((d, i) => [d, i]));
  const lines = seriesList.map((s) => {
    const values = new Array<number>(days.length).fill(0);
    for (const p of s.points) {
      const i = index.get(p.day);
      if (i !== undefined) values[i] = p.value;
    }
    return { name: s.name, values };
  });
  return { days, lines };
}

export function lineOption(seriesList: Series[], { legend = false } = {}): EChartsOption {
  const { days, lines } = alignSeries(seriesList);
  return {
    color: palette.series,
    tooltip: { trigger: 'axis' },
    legend: legend ? { top: 0, textStyle: { color: palette.text }, icon: 'roundRect' } : undefined,
    grid: { left: 48, right: 16, top: legend ? 32 : 12, bottom: 28 },
    xAxis: { type: 'category', boundaryGap: false, data: days, ...axisBase },
    yAxis: { type: 'value', splitLine: { lineStyle: { color: palette.grid } }, ...axisBase },
    series: lines.map((l) => ({
      name: l.name,
      type: 'line',
      smooth: true,
      showSymbol: false,
      lineStyle: { width: 2 },
      data: l.values
    }))
  } as EChartsOption;
}

// Horizontal stacked bar: one row per `rows` entry (e.g. a person), each split into the
// ordered `categories` (e.g. workflow statuses). `rows[i].values` aligns to `categories`.
// A 2px surface gap separates segments (secondary encoding + the mark-spec spacer); the
// axis-triggered tooltip shows the full per-row breakdown.
export function stackedBarOption(
  categories: string[],
  rows: { label: string; values: number[] }[],
  unit = ''
): EChartsOption {
  const colors = categories.map((c, i) => (c === 'Other' ? palette.other : palette.categorical[i % palette.categorical.length]));
  return {
    color: colors,
    tooltip: {
      trigger: 'axis',
      axisPointer: { type: 'shadow' },
      valueFormatter: (v) => (typeof v === 'number' && v > 0 ? `${v}${unit}` : '—')
    },
    legend: { top: 0, textStyle: { color: palette.text }, icon: 'roundRect' },
    grid: { left: 8, right: 24, top: 32, bottom: 8, containLabel: true },
    xAxis: { type: 'value', splitLine: { lineStyle: { color: palette.grid } }, ...axisBase },
    yAxis: { type: 'category', data: rows.map((r) => r.label), ...axisBase },
    series: categories.map((cat, ci) => ({
      name: cat,
      type: 'bar',
      stack: 'total',
      data: rows.map((r) => r.values[ci] ?? 0),
      itemStyle: { borderColor: palette.surface, borderWidth: 2 },
      emphasis: { focus: 'series' }
    }))
  } as EChartsOption;
}

// Horizontal bar for a single magnitude series (commits by person), value-labelled.
export function barOption(items: LabelValue[]): EChartsOption {
  const sorted = [...items].sort((a, b) => a.value - b.value);
  return {
    color: palette.series,
    tooltip: { trigger: 'item' },
    grid: { left: 8, right: 40, top: 8, bottom: 8, containLabel: true },
    xAxis: { type: 'value', splitLine: { lineStyle: { color: palette.grid } }, ...axisBase },
    yAxis: { type: 'category', data: sorted.map((i) => i.label), ...axisBase },
    series: [
      {
        type: 'bar',
        data: sorted.map((i) => i.value),
        barWidth: '60%',
        itemStyle: { borderRadius: [0, 4, 4, 0] },
        label: { show: true, position: 'right', color: palette.text }
      }
    ]
  } as EChartsOption;
}
