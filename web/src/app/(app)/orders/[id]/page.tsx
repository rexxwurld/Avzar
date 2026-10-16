import { Fragment } from 'react';
import Link from 'next/link';
import { notFound } from 'next/navigation';
import { LoadError, NoAccess } from '@/components/app/Notices';
import { OrderActions } from '@/components/app/OrderActions';
import { OrderInvoiceAction } from '@/components/app/OrderInvoiceAction';
import { formatDateTime } from '@/lib/dates';
import { money } from '@/lib/format';
import type { Order } from '@/lib/order-types';
import type { InvoiceSummary, Paged } from '@/lib/finance-types';
import { formatDay, isLate, statusLabel, statusTone, todayIn } from '@/lib/orders';
import { getSession, serverGet } from '@/lib/session';
import { can } from '@/lib/team';

export const metadata = { title: 'Order · Avzar' };
export const dynamic = 'force-dynamic';

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export default async function OrderPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  if (!GUID.test(id)) notFound();
  const me = (await getSession())!;
  if (!can(me, 'orders.view')) return <NoAccess />;
  const res = await serverGet<Order>(`/api/orders/${id}`);
  if (!res.ok) { if (res.status === 404) notFound(); return <LoadError status={res.status} />; }
  const o = res.data;
  const invoiceHistory = can(me, 'invoices.view')
    ? await serverGet<Paged<InvoiceSummary>>(`/api/invoices?orderId=${encodeURIComponent(id)}&page=1&pageSize=50`)
    : null;
  const tone = statusTone(o.status);
  const late = isLate(o.status, o.promisedDate, todayIn(me.timeZoneId));
  const tz = me.timeZoneId;
  const when = (iso: string | null) => (iso ? formatDateTime(iso, tz) : null);
  const timeline = ([['Created', o.createdAtUtc], ['Quoted', o.quotedAtUtc], ['Confirmed', o.confirmedAtUtc], ['Ready', o.readyAtUtc], ['Delivered', o.deliveredAtUtc], ['Cancelled', o.cancelledAtUtc]] as const)
    .map(([label, iso]) => [label, when(iso)] as const).filter(([, v]) => v);
  const p = o.progress;

  return (
    <>
      <p style={{ margin: '0 0 .5rem' }}><Link href="/orders">← Orders</Link></p>
      <h1 style={{ margin: '0 0 1rem' }}>
        {o.orderNumber} <span className={`ap-badge${tone === 'on' ? ' ap-badge-on' : tone === 'off' ? ' ap-badge-off' : ''}`}>{statusLabel(o.status)}</span>
        {late && <span className="ap-badge ap-badge-off">Late</span>}
      </h1>

      {o.status === 'Cancelled' && o.cancellationReason && <p className="ap-alert" role="status">Cancelled: {o.cancellationReason}</p>}

      <div className="ap-stack">
        <OrderActions order={o} canManage={can(me, 'orders.manage')} canConfirm={can(me, 'orders.confirm')} canEdit={can(me, 'orders.manage') && can(me, 'staff.view')} />
        <OrderInvoiceAction orderId={o.id} orderStatus={o.status} canManage={can(me, 'invoices.manage')} />
        {invoiceHistory?.ok && <section className="ap-panel ap-stack"><div className="ap-row"><h2 className="ap-h2">Invoice history</h2><span className="ap-foot">{invoiceHistory.data.totalCount} invoice(s)</span></div><div className="ap-scroll"><table className="ap-table"><thead><tr><th>Invoice</th><th>Status</th><th>Total</th><th>Paid</th><th>Balance</th><th>Due date</th></tr></thead><tbody>{invoiceHistory.data.items.map((invoice) => <tr key={invoice.id}><td><Link href={`/finance/invoices/${invoice.id}`}><strong>{invoice.number ?? 'Draft invoice'}</strong></Link><div className="ap-foot">{invoice.kind}</div></td><td>{invoice.status}{invoice.paymentStatus ? ` · ${invoice.paymentStatus}` : ''}</td><td>{money(invoice.total, invoice.currencyCode)}</td><td>{money(invoice.amountPaid, invoice.currencyCode)}</td><td>{money(invoice.balance, invoice.currencyCode)}</td><td>{invoice.dueDate ?? '—'}</td></tr>)}</tbody></table>{invoiceHistory.data.items.length === 0 && <p className="ap-foot">No invoices are linked to this order yet.</p>}</div></section>}
        {invoiceHistory && !invoiceHistory.ok && <p className="ap-alert" role="status">Invoice history could not be loaded (HTTP {invoiceHistory.status}).</p>}
        <section className="ap-panel" aria-labelledby="ord-h">
          <h2 id="ord-h" className="ap-h2">Order</h2>
          <dl className="ap-dl">
            <dt>Customer</dt>
            <dd>{can(me, 'customers.view') ? <Link href={`/customers/${o.customerId}`}>{o.customerName}</Link> : o.customerName}</dd>
            <dt>Promised</dt><dd>{formatDay(o.promisedDate)}</dd>
            <dt>Priority</dt><dd>{o.priority}</dd>
            <dt>Delivery</dt><dd>{o.deliveryMethod}{o.deliveryAddress ? ` · ${o.deliveryAddress}` : ''}</dd>
          </dl>
          {o.deliveryInstructions && <><h3 className="ap-h3">Delivery instructions</h3><p className="ap-pre">{o.deliveryInstructions}</p></>}
          {o.notes && <><h3 className="ap-h3">Notes</h3><p className="ap-pre">{o.notes}</p></>}
        </section>

        <section className="ap-panel" aria-labelledby="gar-h">
          <h2 id="gar-h" className="ap-h2">Garments</h2>
          <div className="ap-scroll">
            <table className="ap-table">
              <thead><tr><th>#</th><th>Item</th><th>Details</th><th>Qty</th><th>Unit</th><th>Line total</th></tr></thead>
              <tbody>
                {o.garments.map((g) => (
                  <tr key={g.id}>
                    <td>{g.lineNumber}</td>
                    <td><strong>{g.name}</strong>{g.designName && g.designName !== g.name ? <div className="ap-hint">Design: {g.designName}</div> : null}</td>
                    <td>
                      {[g.category, g.fabric, g.colour].filter(Boolean).join(' · ') || '—'}
                      {g.measurementRevision != null && <div className="ap-hint">Measurements r{g.measurementRevision}</div>}
                      {g.styleInstructions && <div className="ap-hint">{g.styleInstructions}</div>}
                      {g.alterationNotes && <div className="ap-hint">Alteration: {g.alterationNotes}</div>}
                    </td>
                    <td>{g.quantity}</td>
                    <td>{money(g.unitPrice, o.currencyCode)}</td>
                    <td>{money(g.lineTotal, o.currencyCode)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <dl className="ap-dl" style={{ marginTop: '1rem' }}>
            <dt>Subtotal</dt><dd>{money(o.subtotal, o.currencyCode)}</dd>
            <dt>Discount</dt><dd>{money(o.discount, o.currencyCode)}</dd>
            <dt>Total</dt><dd><strong>{money(o.total, o.currencyCode)}</strong></dd>
          </dl>
        </section>

        <section className="ap-panel" aria-labelledby="prog-h">
          <h2 id="prog-h" className="ap-h2">Production</h2>
          {p && p.totalTasks > 0 ? (
            <dl className="ap-dl">
              <dt>Progress</dt><dd>{p.percentComplete}% ({p.completed} of {p.totalTasks} tasks)</dd>
              <dt>In progress</dt><dd>{p.inProgress}</dd>
              <dt>Blocked</dt><dd>{p.blocked}</dd>
              <dt>Overdue</dt><dd>{p.overdue}</dd>
            </dl>
          ) : <p style={{ margin: 0 }}>No production tasks yet. They are created when the order is confirmed.</p>}
        </section>

        <section className="ap-panel" aria-labelledby="tl-h">
          <h2 id="tl-h" className="ap-h2">Timeline</h2>
          <dl className="ap-dl">{timeline.map(([k, v]) => <Fragment key={k}><dt>{k}</dt><dd>{v}</dd></Fragment>)}</dl>
        </section>
      </div>
    </>
  );
}
