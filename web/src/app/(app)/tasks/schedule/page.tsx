import { NoAccess } from '@/components/app/Notices';
import { ShopSchedulePreview } from '@/components/app/ShopSchedulePreview';
import { getSession } from '@/lib/session';
import { can } from '@/lib/team';

export const metadata = { title: 'Shop schedule · Avzar' };
export const dynamic = 'force-dynamic';

export default async function ShopSchedulePage() {
  const me = (await getSession())!;
  if (!can(me, 'tasks.view')) return <NoAccess />;
  return <div className="ap-stack">
    <div className="ap-row" style={{ marginBottom: '.25rem' }}><div><h1 style={{ margin: 0 }}>Shop schedule</h1><p className="ap-foot" style={{ margin: '.35rem 0 0' }}>Preview a task plan across your shop before applying any changes.</p></div></div>
    <ShopSchedulePreview timeZoneId={me.timeZoneId} />
  </div>;
}
