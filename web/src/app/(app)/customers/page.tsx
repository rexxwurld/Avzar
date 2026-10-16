import Link from 'next/link';
import { LoadError, NoAccess } from '@/components/app/Notices';
import type { CustomerSummary, Paged } from '@/lib/customer-types';
import { PAGE_SIZE, STATUS_FILTERS, apiQueryString, listHref, parseListQuery, totalPages } from '@/lib/customers';
import { formatDate } from '@/lib/dates';
import { getSession, serverGet } from '@/lib/session';
import { can } from '@/lib/team';

export const metadata = { title: 'Customers · Avzar' };
export const dynamic = 'force-dynamic';

const SORT_LABELS: Record<string, string> = { name: 'Name A–Z', 'name-desc': 'Name Z–A', created: 'Oldest first', 'created-desc': 'Newest first' };

export default async function CustomersPage({ searchParams }: { searchParams: Promise<Record<string, string | string[] | undefined>> }) {
  const me = (await getSession())!;
  if (!can(me, 'customers.view')) return <NoAccess />;
  const q = parseListQuery(await searchParams);
  const res = await serverGet<Paged<CustomerSummary>>(`/api/customers?${apiQueryString(q)}`);
  if (!res.ok) return <LoadError status={res.status} />;
  const { items, totalCount } = res.data;
  const pages = totalPages(totalCount, res.data.pageSize || PAGE_SIZE);
  const filtered = !!(q.search || q.status);

  return (
    <>
      <div className="ap-row" style={{ marginBottom: '1rem' }}>
        <h1 style={{ margin: 0 }}>Customers</h1>
        {can(me, 'customers.manage') && <Link href="/customers/new" className="ap-btn" style={{ textDecoration: 'none' }}>New customer</Link>}
      </div>

      <form method="get" action="/customers" className="ap-panel ap-filter" role="search">
        <label className="ap-sr" htmlFor="f-search">Search customers</label>
        <input id="f-search" name="search" className="ap-input" placeholder="Search by name, email or phone" defaultValue={q.search} maxLength={100} />
        <label className="ap-sr" htmlFor="f-status">Status</label>
        <select id="f-status" name="status" className="ap-input" defaultValue={q.status}>
          {STATUS_FILTERS.map((s) => <option key={s} value={s}>{s || 'Active and inactive'}</option>)}
        </select>
        <label className="ap-sr" htmlFor="f-sort">Sort</label>
        <select id="f-sort" name="sort" className="ap-input" defaultValue={q.sort}>
          {Object.entries(SORT_LABELS).map(([v, l]) => <option key={v} value={v}>{l}</option>)}
        </select>
        <button className="ap-btn" type="submit">Apply</button>
        {filtered && <Link href="/customers" className="ap-btn ap-btn-quiet" style={{ textDecoration: 'none' }}>Clear</Link>}
      </form>

      <section className="ap-panel" style={{ marginTop: '1rem' }} aria-label="Customer list">
        {items.length === 0 ? (
          <p style={{ margin: 0 }}>
            {totalCount > 0 ? <>No customers on this page. <Link href={listHref(q, { page: 1 })}>Go to the first page</Link>.</> : filtered ? 'No customers match your search.' : 'No customers yet.'}
          </p>
        ) : (
          <div className="ap-scroll">
            <table className="ap-table">
              <thead><tr><th>Name</th><th>Email</th><th>Phone</th><th>Status</th><th>Orders</th><th>Added</th></tr></thead>
              <tbody>
                {items.map((c) => (
                  <tr key={c.id} className={c.status === 'Active' ? undefined : 'ap-dim'}>
                    <td><Link href={`/customers/${c.id}`}><strong>{c.fullName}</strong></Link></td>
                    <td>{c.email || '—'}</td>
                    <td>{c.phone || '—'}</td>
                    <td><span className={c.status === 'Active' ? 'ap-badge ap-badge-on' : c.status === 'Inactive' ? 'ap-badge ap-badge-off' : 'ap-badge'} style={{ marginLeft: 0 }}>{c.status}</span></td>
                    <td>{c.orderCount}</td>
                    <td>{formatDate(c.createdAtUtc, me.timeZoneId)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        {totalCount > 0 && (
          <nav className="ap-row ap-pager" aria-label="Pages">
            <span className="ap-hint">{totalCount} customer{totalCount === 1 ? '' : 's'} · page {Math.min(q.page, pages)} of {pages}</span>
            <span className="ap-actions">
              {q.page > 1 && <Link className="ap-btn ap-btn-quiet ap-btn-sm" style={{ textDecoration: 'none' }} href={listHref(q, { page: q.page - 1 })} rel="prev">Previous</Link>}
              {q.page < pages && <Link className="ap-btn ap-btn-quiet ap-btn-sm" style={{ textDecoration: 'none' }} href={listHref(q, { page: q.page + 1 })} rel="next">Next</Link>}
            </span>
          </nav>
        )}
      </section>
    </>
  );
}
