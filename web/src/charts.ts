// Chart styling shared across dashboards. Colors are the validated dataviz
// reference palette (dark steps — the app runs a dark Vuetify theme). Categorical
// slots are assigned in fixed order: slot 1 blue, slot 2 green.
import type { EChartsOption } from 'echarts';
import type { DayPoint, LabelValue, RecordMonthCount } from './types';

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

// A target reference drawn onto a line chart: a dashed line at `value`, with the "good"
// region (at or below `value`) shaded faint green. Used for DORA duration targets where
// lower is better (lead time, cycle time).
export interface Threshold {
  value: number;
  label?: string;
}

export function lineOption(
  seriesList: Series[],
  { legend = false, threshold }: { legend?: boolean; threshold?: Threshold } = {}
): EChartsOption {
  const { days, lines } = alignSeries(seriesList);
  const series: Record<string, unknown>[] = lines.map((l) => ({
    name: l.name,
    type: 'line',
    smooth: true,
    showSymbol: false,
    lineStyle: { width: 2 },
    data: l.values
  }));

  // Render the target as a flat dashed series (not a markLine) so it counts toward the
  // axis scale — a reference line off-screen is useless. Its area shades the "good" zone
  // (at or below the target) faint green.
  if (threshold) {
    series.push({
      name: threshold.label ?? `target ${threshold.value}`,
      type: 'line',
      showSymbol: false,
      data: days.map(() => threshold.value),
      lineStyle: { color: palette.categorical[1], type: 'dashed', width: 1.5 },
      areaStyle: { color: 'rgba(0, 131, 0, 0.10)', origin: 'start' }
    });
  }

  const showLegend = legend || !!threshold;
  return {
    color: palette.series,
    tooltip: { trigger: 'axis' },
    legend: showLegend ? { top: 0, textStyle: { color: palette.text }, icon: 'roundRect' } : undefined,
    grid: { left: 48, right: 16, top: showLegend ? 32 : 12, bottom: 28 },
    xAxis: { type: 'category', boundaryGap: false, data: days, ...axisBase },
    yAxis: { type: 'value', splitLine: { lineStyle: { color: palette.grid } }, ...axisBase },
    series: series as EChartsOption['series']
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

const MONTH_ABBR = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

// A "yyyy-MM" key → "Aug" (axis) / "Aug 2025" (tooltip).
function monthShort(key: string): string {
  const m = Number(key.split('-')[1]) - 1;
  return MONTH_ABBR[m] ?? key;
}
function monthLong(key: string): string {
  const [y, m] = key.split('-');
  return `${MONTH_ABBR[Number(m) - 1] ?? m} ${y}`;
}

// Compact trailing-12-month record histogram. A single magnitude series, so no legend — the
// caller's heading names it. `tooltip.trigger: 'axis'` with a shadow pointer makes the whole
// month column the hover target, so the tooltip still appears for a month whose bar is a
// sliver (or zero) — hovering doesn't depend on hitting the bar rect. Bars scale to the
// tallest month and fill the container height, so the chart reads at whatever height it's given.
export function monthlyBarOption(months: RecordMonthCount[]): EChartsOption {
  return {
    color: palette.series,
    tooltip: {
      trigger: 'axis',
      axisPointer: { type: 'shadow' },
      backgroundColor: palette.surface,
      borderColor: palette.grid,
      textStyle: { color: palette.text },
      formatter: (params: unknown) => {
        const p = (Array.isArray(params) ? params[0] : params) as { dataIndex: number; value: number };
        const n = p?.value ?? 0;
        return `${monthLong(months[p.dataIndex].key)}<br/><strong>${n}</strong> record${n === 1 ? '' : 's'}`;
      }
    },
    grid: { left: 4, right: 4, top: 6, bottom: 4, containLabel: true },
    xAxis: {
      type: 'category',
      data: months.map((m) => monthShort(m.key)),
      axisLine: { lineStyle: { color: palette.grid } },
      axisTick: { show: false },
      axisLabel: { color: palette.muted, fontSize: 10, interval: 0 }
    },
    // Hidden y-axis keeps the block short; the tooltip carries exact counts.
    yAxis: { type: 'value', minInterval: 1, show: false, splitLine: { show: false } },
    series: [
      {
        type: 'bar',
        data: months.map((m) => m.count),
        barWidth: '62%',
        itemStyle: { borderRadius: [3, 3, 0, 0] },
        emphasis: { itemStyle: { color: palette.categorical[0] } }
      }
    ]
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
