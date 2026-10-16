import { LoadError, NoAccess } from '@/components/app/Notices';
import { WorkerScheduleManager } from '@/components/app/WorkerScheduleManager';
import { getSession, serverGet } from '@/lib/session';
import { can } from '@/lib/team';

export const metadata = { title: 'Worker schedules · Avzar' };
export const dynamic = 'force-dynamic';

type Worker = { membershipId: string; displayName: string; isActive: boolean; skills: string[]; openTasks: number };

export default async function WorkerSchedulesPage() {
  const me = (await getSession())!;
  if (!can(me, 'tasks.view')) return <NoAccess />;
  const result = await serverGet<Worker[]>('/api/workers');
  if (!result.ok) return <LoadError status={result.status} />;
  return <div className="ap-stack">
    <div className="ap-row" style={{ marginBottom: '.25rem' }}><div><h1 style={{ margin: 0 }}>Worker schedules</h1><p className="ap-foot" style={{ margin: '.35rem 0 0' }}>Manage individual availability, daily workload capacity, approved leave and assigned task calendars.</p></div></div>
    <WorkerScheduleManager initialWorkers={result.data} canManage={can(me, 'workers.manage')} canRequest={can(me, 'tasks.work')} timeZoneId={me.timeZoneId} />
  </div>;
}
