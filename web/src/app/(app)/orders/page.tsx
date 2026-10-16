import Link from 'next/link';
import { LoadError, NoAccess } from '@/components/app/Notices';
import { money } from '@/lib/format';
import { ORDER_STATUSES, type OrderSummary } from '@/lib/order-types';
import { ORDER_PAGE_SIZE, formatDay, isLate, orderApiQuery, orderListHref, parseOrderQuery, statusLabel, statusTone, todayIn } from '@/lib/orders';
import { getSession, serverGet } from '@/lib/session';
import type { Paged } from '@/lib/customer-types';
import { totalPages } from '@/lib/customers';
import { can } from '@/lib/team';

export const metadata = { title: 'Orders · Avzar' };
export const dynamic = 'force-dynamic';

export default async function OrdersPage({ searchParams }: { searchParams: Promise<Record<string, string | string[] | undefined>> }) {
  const me = (await getSession())!;
  if (!can(me, 'orders.view')) return <NoAccess />;
  const q = parseOrderQuery(await searchParams);
  const res = await serverGet<Paged<OrderSummary>>(`/api/orders?${orderApiQuery(q)}`);
  if (!res.ok) return <LoadError status={res.status} />;
  const { items, totalCount } = res.data;
  const pages = totalPages(totalCount, res.data.pageSize || ORDER_PAGE_SIZE);
  const filtered = !!(q.search || q.status || q.dueFrom || q.dueTo);
  const today = todayIn(me.timeZoneId);

  return (
    <>
      <div className="ap-row" style={{ marginBottom: '1rem' }}><h1 style={{ margin: 0 }}>Orders</h1>{can(me, 'orders.manage') && can(me, 'customers.view') && can(me, 'staff.view') && <Link href="/orders/new" className="ap-btn" style={{ textDecoration: 'none' }}>New order</Link>}</div>
      <form method="get" action="/orders" className="ap-panel ap-filter" role="search">
        <label className="ap-sr" htmlFor="o-search">Search orders</label>
        <input id="o-search" name="search" className="ap-input" placeholder="Order number or customer name" defaultValue={q.search} maxLength={100} />
        <label className="ap-sr" htmlFor="o-status">Status</label>
        <select id="o-status" name="status" className="ap-input" defaultValue={q.status}>
          <option value="">All statuses</option>
          {ORDER_STATUSES.map((s) => <option key={s} value={s}>{statusLabel(s)}</option>)}
        </select>
        <label className="ap-sr" htmlFor="o-from">Due from</label>
        <input id="o-from" name="dueFrom" type="date" className="ap-input" defaultValue={q.dueFrom} />
        <label className="ap-sr" htmlFor="o-to">Due to</label>
        <input id="o-to" name="dueTo" type="date" className="ap-input" defaultValue={q.dueTo} />
        <button className="ap-btn" type="submit">Apply</button>
        {filtered && <Link href="/orders" className="ap-btn ap-btn-quiet" style={{ textDecoration: 'none' }}>Clear</Link>}
      </form>

      <section className="ap-panel" style={{ marginTop: '1rem' }} aria-label="Order list">
        {items.length === 0 ? (
          <p style={{ margin: 0 }}>
            {totalCount > 0 ? <>No orders on this page. <Link href={orderListHref(q, { page: 1 })}>Go to the first page</Link>.</> : filtered ? 'No orders match your filters.' : 'No orders yet.'}
          </p>
        ) : (
          <div className="ap-scroll">
            <table className="ap-table">
              <thead><tr><th>Order</th><th>Customer</th><th>Status</th><th>Promised</th><th>Priority</th><th>Items</th><th>Total</th></tr></thead>
              <tbody>
                {items.map((o) => {
                  const late = isLate(o.status, o.promisedDate, today);
                  return (
                    <tr key={o.id} className={o.status === 'Cancelled' ? 'ap-dim' : undefined}>
                      <td><Link href={`/orders/${o.id}`}><strong>{o.orderNumber}</strong></Link></td>
                      <td>{o.customerName}</td>
                      <td><span className={`ap-badge${statusTone(o.status) === 'on' ? ' ap-badge-on' : statusTone(o.status) === 'off' ? ' ap-badge-off' : ''}`} style={{ marginLeft: 0 }}>{statusLabel(o.status)}</span></td>
                      <td>{formatDay(o.promisedDate)}{late && <span className="ap-badge ap-badge-off">Late</span>}</td>
                      <td>{o.priority}</td>
                      <td>{o.garmentCount}</td>
                      <td>{money(o.total, o.currencyCode)}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
        {totalCount > 0 && (
          <nav className="ap-row ap-pager" aria-label="Pages">
            <span className="ap-hint">{totalCount} order{totalCount === 1 ? '' : 's'} · page {Math.min(q.page, pages)} of {pages}</span>
            <span className="ap-actions">
              {q.page > 1 && <Link className="ap-btn ap-btn-quiet ap-btn-sm" style={{ textDecoration: 'none' }} href={orderListHref(q, { page: q.page - 1 })} rel="prev">Previous</Link>}
              {q.page < pages && <Link className="ap-btn ap-btn-quiet ap-btn-sm" style={{ textDecoration: 'none' }} href={orderListHref(q, { page: q.page + 1 })} rel="next">Next</Link>}
            </span>
          </nav>
        )}
      </section>
    </>
  );
}
