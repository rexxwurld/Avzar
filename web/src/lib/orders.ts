import { ORDER_STATUSES } from './order-types';

export const ORDER_PAGE_SIZE = 25;
const DAY = /^\d{4}-\d{2}-\d{2}$/;

export interface OrderListQuery { search: string; status: string; dueFrom: string; dueTo: string; page: number }

type Raw = Record<string, string | string[] | undefined>;
const first = (v: string | string[] | undefined) => (Array.isArray(v) ? v[0] : v);

/** A real calendar date in yyyy-MM-dd form, or ''. Rejects 2026-02-31 and anything else a URL can carry. */
export function cleanDay(v: string | undefined): string {
  if (!v || !DAY.test(v)) return '';
  const d = new Date(`${v}T00:00:00Z`);
  return !Number.isNaN(d.getTime()) && d.toISOString().startsWith(v) ? v : '';
}

/** Reads the URL's query string defensively: anything unrecognised falls back to the default. */
export function parseOrderQuery(raw: Raw): OrderListQuery {
  const search = (first(raw.search) ?? '').trim().slice(0, 100);
  const statusRaw = first(raw.status) ?? '';
  const status = (ORDER_STATUSES as readonly string[]).includes(statusRaw) ? statusRaw : '';
  let dueFrom = cleanDay(first(raw.dueFrom));
  let dueTo = cleanDay(first(raw.dueTo));
  if (dueFrom && dueTo && dueFrom > dueTo) [dueFrom, dueTo] = [dueTo, dueFrom];
  const n = Number.parseInt(first(raw.page) ?? '1', 10);
  const page = Number.isFinite(n) && n >= 1 && n <= 100000 ? n : 1;
  return { search, status, dueFrom, dueTo, page };
}

export function orderApiQuery(q: OrderListQuery): string {
  const p = new URLSearchParams();
  p.set('page', String(q.page));
  p.set('pageSize', String(ORDER_PAGE_SIZE));
  if (q.search) p.set('search', q.search);
  if (q.status) p.set('status', q.status);
  if (q.dueFrom) p.set('dueFrom', q.dueFrom);
  if (q.dueTo) p.set('dueTo', q.dueTo);
  return p.toString();
}

export function orderListHref(q: OrderListQuery, overrides: Partial<OrderListQuery> = {}): string {
  const m = { ...q, ...overrides };
  const p = new URLSearchParams();
  if (m.search) p.set('search', m.search);
  if (m.status) p.set('status', m.status);
  if (m.dueFrom) p.set('dueFrom', m.dueFrom);
  if (m.dueTo) p.set('dueTo', m.dueTo);
  if (m.page > 1) p.set('page', String(m.page));
  const s = p.toString();
  return s ? `/orders?${s}` : '/orders';
}

export const STATUS_LABELS: Record<string, string> = { InProduction: 'In production' };
export const statusLabel = (s: string) => STATUS_LABELS[s] ?? s;

/** Which badge colour a status gets. Unknown statuses stay neutral rather than throwing. */
export function statusTone(s: string): 'on' | 'off' | 'neutral' {
  if (s === 'Ready' || s === 'Delivered' || s === 'Confirmed' || s === 'InProduction') return 'on';
  if (s === 'Cancelled') return 'off';
  return 'neutral';
}

/** A promised date (yyyy-MM-dd) has no time zone, so it is formatted as written and never shifted by the viewer's zone. */
export function formatDay(day: string): string {
  const d = cleanDay(day);
  if (!d) return '—';
  return new Intl.DateTimeFormat('en-GB', { dateStyle: 'medium', timeZone: 'UTC' }).format(new Date(`${d}T00:00:00Z`));
}

/** Today's calendar date in the business's time zone, as yyyy-MM-dd. Falls back to UTC for an unknown zone. */
export function todayIn(timeZone: string, now: Date = new Date()): string {
  const fmt = (tz: string) => new Intl.DateTimeFormat('en-CA', { timeZone: tz, year: 'numeric', month: '2-digit', day: '2-digit' }).format(now);
  try { return fmt(timeZone); } catch { return fmt('UTC'); }
}

/** Late = promised date has passed and the order is neither delivered nor cancelled. */
export const isLate = (status: string, promisedDate: string, today: string): boolean =>
  status !== 'Delivered' && status !== 'Cancelled' && cleanDay(promisedDate) !== '' && promisedDate < today;

/** Mirrors the UI's action affordances; the API remains authoritative for every transition. */
export function orderActionAvailability(status: string, canManage: boolean, canConfirm: boolean) {
  const editable = status === 'Draft' || status === 'Quoted';
  return {
    edit: canManage && editable,
    quote: canManage && status === 'Draft',
    confirm: canConfirm && status === 'Quoted',
    cancel: canManage && status !== 'Cancelled' && status !== 'Delivered',
  };
}
