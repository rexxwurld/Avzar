import { describe, expect, it } from 'vitest';
import { cleanDay, formatDay, isLate, orderApiQuery, orderListHref, parseOrderQuery, statusLabel, statusTone, todayIn, orderActionAvailability } from '../src/lib/orders';

describe('parseOrderQuery: the URL is untrusted', () => {
  it('defaults', () => expect(parseOrderQuery({})).toEqual({ search: '', status: '', dueFrom: '', dueTo: '', page: 1 }));
  it('reads valid values', () =>
    expect(parseOrderQuery({ search: ' ORD-1 ', status: 'Ready', dueFrom: '2026-10-01', dueTo: '2026-10-31', page: '2' }))
      .toEqual({ search: 'ORD-1', status: 'Ready', dueFrom: '2026-10-01', dueTo: '2026-10-31', page: 2 }));
  it.each(['ready', 'Hacked', 'Draft; drop', 'Paid'])('ignores status %s', (s) => expect(parseOrderQuery({ status: s }).status).toBe(''));
  it.each(['2026-02-31', '2026-13-01', '10/10/2026', '2026-1-1', 'x', '2026-10-01T00:00'])('drops bad date %s', (d) => expect(cleanDay(d)).toBe(''));
  it('swaps a reversed date range instead of sending an empty one', () => {
    const q = parseOrderQuery({ dueFrom: '2026-11-01', dueTo: '2026-10-01' });
    expect([q.dueFrom, q.dueTo]).toEqual(['2026-10-01', '2026-11-01']);
  });
  it.each(['0', '-3', 'abc', '1e9', '99999999999'])('page %s falls back to 1', (p) => expect(parseOrderQuery({ page: p }).page).toBe(1));
  it('takes the first repeated param and caps search length', () => {
    expect(parseOrderQuery({ search: ['a', 'b'] }).search).toBe('a');
    expect(parseOrderQuery({ search: 'x'.repeat(300) }).search).toHaveLength(100);
  });
});

describe('query strings and links', () => {
  it('builds the API query', () =>
    expect(orderApiQuery(parseOrderQuery({ search: 'a b', status: 'Draft', dueTo: '2026-10-31' }))).toBe('page=1&pageSize=25&search=a+b&status=Draft&dueTo=2026-10-31'));
  it('leaves defaults out of page links', () => {
    expect(orderListHref(parseOrderQuery({}))).toBe('/orders');
    expect(orderListHref(parseOrderQuery({ status: 'Ready', page: '3' }), { page: 4 })).toBe('/orders?status=Ready&page=4');
    expect(orderListHref(parseOrderQuery({ page: '3' }), { page: 1 })).toBe('/orders');
  });
});

describe('status and dates', () => {
  it('labels and tones, with unknown values neutral', () => {
    expect(statusLabel('InProduction')).toBe('In production');
    expect(statusLabel('Draft')).toBe('Draft');
    expect(statusTone('Cancelled')).toBe('off');
    expect(statusTone('Delivered')).toBe('on');
    expect(statusTone('Draft')).toBe('neutral');
    expect(statusTone('Whatever')).toBe('neutral');
  });
  it('formats a promised date as written, with no zone shift', () => {
    expect(formatDay('2026-10-16')).toBe('16 Oct 2026');
    expect(formatDay('garbage')).toBe('—');
  });
  it('finds today in the business zone and falls back to UTC for an unknown zone', () => {
    const now = new Date('2026-10-16T23:30:00Z');
    expect(todayIn('UTC', now)).toBe('2026-10-16');
    expect(todayIn('Africa/Lagos', now)).toBe('2026-10-17');
    expect(todayIn('Not/AZone', now)).toBe('2026-10-16');
  });
  it('is late only when past due and still open', () => {
    expect(isLate('Confirmed', '2026-10-15', '2026-10-16')).toBe(true);
    expect(isLate('Confirmed', '2026-10-16', '2026-10-16')).toBe(false);
    expect(isLate('Delivered', '2026-10-01', '2026-10-16')).toBe(false);
    expect(isLate('Cancelled', '2026-10-01', '2026-10-16')).toBe(false);
    expect(isLate('Confirmed', 'junk', '2026-10-16')).toBe(false);
  });
});


describe('order action permissions', () => {
  it('allows managers to edit and quote drafts, but not confirm them', () => {
    expect(orderActionAvailability('Draft', true, false)).toEqual({ edit: true, quote: true, confirm: false, cancel: true });
  });
  it('allows confirmation only for quoted orders and users with orders.confirm', () => {
    expect(orderActionAvailability('Quoted', true, false)).toEqual({ edit: true, quote: false, confirm: false, cancel: true });
    expect(orderActionAvailability('Quoted', false, true)).toEqual({ edit: false, quote: false, confirm: true, cancel: false });
  });
  it('never offers cancellation for delivered or cancelled orders', () => {
    expect(orderActionAvailability('Delivered', true, true).cancel).toBe(false);
    expect(orderActionAvailability('Cancelled', true, true).cancel).toBe(false);
  });
  it('does not offer actions to users without the required permissions', () => {
    expect(orderActionAvailability('Draft', false, false)).toEqual({ edit: false, quote: false, confirm: false, cancel: false });
  });
});
