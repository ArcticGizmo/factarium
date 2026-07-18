// Formats a timestamp as a sortable "yyyy-MM-dd HH:mm:ss" in local time.
// Returns an em dash for missing/invalid values.
export function formatDateTime(ts: string | number | Date | null | undefined): string {
  if (ts === null || ts === undefined || ts === '') return '—';
  const d = new Date(ts);
  if (Number.isNaN(d.getTime())) return '—';
  const p = (n: number) => String(n).padStart(2, '0');
  return (
    `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())} ` +
    `${p(d.getHours())}:${p(d.getMinutes())}:${p(d.getSeconds())}`
  );
}
