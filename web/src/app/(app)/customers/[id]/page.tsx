import Link from 'next/link';
import { notFound } from 'next/navigation';
import { CommunicationLog, type LogEntry } from '@/components/app/CommunicationLog';
import { CustomerPortalLinkButton } from '@/components/app/CustomerPortalLinkButton';
import { MergePanel } from '@/components/app/MergePanel';
import { LoadError, NoAccess } from '@/components/app/Notices';
import { StatusControl } from '@/components/app/StatusControl';
import type { Communication, Customer } from '@/lib/customer-types';
import { formatDate, formatDateTime } from '@/lib/dates';
import { getSession, serverGet } from '@/lib/session';
import { can } from '@/lib/team';

export const metadata = { title: 'Customer · Avzar' };
export const dynamic = 'force-dynamic';

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export default async function CustomerPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  if (!GUID.test(id)) notFound();
  const me = (await getSession())!;
  if (!can(me, 'customers.view')) return <NoAccess />;

  const [cust, comms] = await Promise.all([
    serverGet<Customer>(`/api/customers/${id}`),
    serverGet<Communication[]>(`/api/customers/${id}/communications`),
  ]);
  if (!cust.ok) { if (cust.status === 404) notFound(); return <LoadError status={cust.status} />; }
  if (!comms.ok) return <LoadError status={comms.status} />;

  const c = cust.data;
  const merged = c.status === 'Merged';
  const manage = can(me, 'customers.manage') && !merged;
  const entries: LogEntry[] = comms.data.map((x) => ({ id: x.id, channel: x.channel, direction: x.direction, subject: x.subject, body: x.body, when: formatDateTime(x.createdAtUtc, me.timeZoneId) }));

  return (
    <>
      <p style={{ margin: '0 0 .5rem' }}><Link href="/customers">← Customers</Link></p>
      <div className="ap-row" style={{ marginBottom: '1rem' }}>
        <h1 style={{ margin: 0 }}>{c.fullName} <span className={c.status === 'Active' ? 'ap-badge ap-badge-on' : c.status === 'Inactive' ? 'ap-badge ap-badge-off' : 'ap-badge'}>{c.status}</span></h1>
        {manage && <Link href={`/customers/${c.id}/edit`} className="ap-btn" style={{ textDecoration: 'none' }}>Edit</Link>}
      </div>

      {merged && (
        <p className="ap-alert" role="status">
          This profile was merged into {c.mergedIntoCustomerId ? <Link href={`/customers/${c.mergedIntoCustomerId}`}>another customer</Link> : 'another customer'} and can no longer be edited.
        </p>
      )}

      <div className="ap-stack">
        <section className="ap-panel" aria-labelledby="details-h">
          <h2 id="details-h" className="ap-h2">Details</h2>
          <dl className="ap-dl">
            <dt>Email</dt><dd>{c.email || '—'}</dd>
            <dt>Phone</dt><dd>{c.phone || '—'}</dd>
            <dt>Address</dt><dd>{[c.addressLine, c.city].filter(Boolean).join(', ') || '—'}</dd>
            <dt>Customer since</dt><dd>{formatDate(c.createdAtUtc, me.timeZoneId)}</dd>
          </dl>
          {manage && <div style={{ marginTop: '1rem' }}><CustomerPortalLinkButton customerId={c.id} /></div>}
          {c.garmentPreferences && <><h3 className="ap-h3">Garment preferences</h3><p className="ap-pre">{c.garmentPreferences}</p></>}
          {c.notes && <><h3 className="ap-h3">Notes</h3><p className="ap-pre">{c.notes}</p></>}
        </section>

        <section className="ap-panel" aria-labelledby="comm-h">
          <h2 id="comm-h" className="ap-h2">Communication log</h2>
          <CommunicationLog customerId={c.id} entries={entries} canManage={manage} />
        </section>

        {manage && (
          <section className="ap-panel" aria-labelledby="status-h">
            <h2 id="status-h" className="ap-h2">Status</h2>
            <StatusControl customerId={c.id} status={c.status as 'Active' | 'Inactive'} />
          </section>
        )}

        {manage && can(me, 'customers.merge') && (
          <section className="ap-panel" aria-labelledby="merge-h">
            <h2 id="merge-h" className="ap-h2">Merge duplicate</h2>
            <MergePanel sourceId={c.id} sourceName={c.fullName} />
          </section>
        )}
      </div>
    </>
  );
}
