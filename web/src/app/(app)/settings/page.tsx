import { BusinessScheduleManager } from '@/components/app/BusinessScheduleManager';
import Link from 'next/link';
import { LoadError, NoAccess } from '@/components/app/Notices';
import { getSession, serverGet } from '@/lib/session';
import { can } from '@/lib/team';

export const metadata = { title: 'Business settings · Avzar' };
export const dynamic = 'force-dynamic';

type DayHours = { dayOfWeek: number; isOpen: boolean; opensAt: string | null; closesAt: string | null };
type Holiday = { id: string; date: string; name: string };
type BusinessSchedule = { timeZoneId: string; days: DayHours[]; holidays: Holiday[] };

export default async function SettingsPage() {
  const me = (await getSession())!;
  if (!can(me, 'business.settings')) return <NoAccess />;
  const schedule = await serverGet<BusinessSchedule>('/api/business/schedule');
  if (!schedule.ok) return <LoadError status={schedule.status} />;
  return <div className="ap-stack"><div className="ap-row"><h1 style={{margin:0}}>Business settings</h1><Link className="ap-btn" href="/settings/billing" style={{textDecoration:'none'}}>Subscription billing</Link></div><BusinessScheduleManager initialSchedule={schedule.data} /></div>;
}
