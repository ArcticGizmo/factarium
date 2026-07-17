// Chart styling shared across dashboards. Colors are the validated dataviz
// reference palette (dark steps — the app runs a dark Vuetify theme). Categorical
// slots are assigned in fixed order: slot 1 blue, slot 2 green.
export const palette = {
  series: ['#3987e5', '#008300'],
  text: '#c3c2b7',
  muted: '#898781',
  grid: '#2c2c2a',
}

const axisBase = {
  axisLine: { lineStyle: { color: palette.grid } },
  axisTick: { show: false },
  axisLabel: { color: palette.muted },
}

// Align multiple day/value series onto one sorted union-of-days x-axis.
export function alignSeries(seriesList) {
  const days = [...new Set(seriesList.flatMap((s) => s.points.map((p) => p.day)))].sort()
  const index = new Map(days.map((d, i) => [d, i]))
  const lines = seriesList.map((s) => {
    const values = new Array(days.length).fill(0)
    for (const p of s.points) values[index.get(p.day)] = p.value
    return { name: s.name, values }
  })
  return { days, lines }
}

export function lineOption(seriesList, { legend = false } = {}) {
  const { days, lines } = alignSeries(seriesList)
  return {
    color: palette.series,
    tooltip: { trigger: 'axis' },
    legend: legend
      ? { top: 0, textStyle: { color: palette.text }, icon: 'roundRect' }
      : undefined,
    grid: { left: 48, right: 16, top: legend ? 32 : 12, bottom: 28 },
    xAxis: { type: 'category', boundaryGap: false, data: days, ...axisBase },
    yAxis: { type: 'value', splitLine: { lineStyle: { color: palette.grid } }, ...axisBase },
    series: lines.map((l) => ({
      name: l.name,
      type: 'line',
      smooth: true,
      showSymbol: false,
      lineStyle: { width: 2 },
      data: l.values,
    })),
  }
}

// Horizontal bar for a single magnitude series (commits by person), value-labelled.
export function barOption(items) {
  const sorted = [...items].sort((a, b) => a.value - b.value)
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
        label: { show: true, position: 'right', color: palette.text },
      },
    ],
  }
}
