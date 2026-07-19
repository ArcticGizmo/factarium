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

// Compact "how long ago" for freshness columns (e.g. "3d ago"). Em dash for missing values.
export function formatRelative(ts: string | number | Date | null | undefined): string {
  if (ts === null || ts === undefined || ts === '') return '—';
  const d = new Date(ts);
  if (Number.isNaN(d.getTime())) return '—';
  const ms = Date.now() - d.getTime();
  if (ms < 0) return 'just now';
  const s = Math.floor(ms / 1000);
  if (s < 60) return `${s}s ago`;
  const m = Math.floor(s / 60);
  if (m < 60) return `${m}m ago`;
  const h = Math.floor(m / 60);
  if (h < 24) return `${h}h ago`;
  const days = Math.floor(h / 24);
  if (days < 7) return `${days}d ago`;
  const w = Math.floor(days / 7);
  if (w < 5) return `${w}w ago`;
  const mo = Math.floor(days / 30);
  if (mo < 12) return `${mo}mo ago`;
  return `${Math.floor(days / 365)}y ago`;
}
