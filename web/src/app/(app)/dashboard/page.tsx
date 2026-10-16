import { getSession } from '@/lib/session';

export const metadata = { title: 'Dashboard · Avzar' };
export const dynamic = 'force-dynamic';

export default async function DashboardPage() {
  const user = (await getSession())!; // the layout has already redirected anonymous visitors
  return (
    <>
      <h1>Dashboard</h1>
      <section className="ap-panel" aria-labelledby="acct">
        <h2 id="acct" style={{ marginTop: 0, fontSize: '1.1rem' }}>Your business</h2>
        <dl className="ap-dl">
          <dt>Business</dt><dd>{user.businessName}</dd>
          <dt>Signed in as</dt><dd>{user.fullName} ({user.email})</dd>
          <dt>Role</dt><dd>{user.isOwner ? 'Owner' : 'Staff'}</dd>
          <dt>Time zone</dt><dd>{user.timeZoneId}</dd>
          <dt>Currency</dt><dd>{user.currencyCode}</dd>
        </dl>
      </section>
    </>
  );
}
