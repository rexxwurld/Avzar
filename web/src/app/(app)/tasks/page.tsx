import Link from 'next/link';
import { LoadError, NoAccess } from '@/components/app/Notices';
import { formatDateTime } from '@/lib/dates';
import { getSession, serverGet } from '@/lib/session';
import type { Paged } from '@/lib/customer-types';
import type { TaskSummary } from '@/lib/production-types';
import { can } from '@/lib/team';
import { managerTaskApiQuery, managerTaskListHref, parseManagerTaskQuery, taskNeedsAttention, taskPageCount, taskStatusLabel, taskStatusTone, TASK_STATUSES, workerTaskApiQuery, type TaskShow } from '@/lib/production';

export const metadata = { title: 'Production · Avzar' };
export const dynamic = 'force-dynamic';

function Status({ value }: { value: string }) {
  const tone = taskStatusTone(value);
  return <span className={`ap-badge${tone === 'on' ? ' ap-badge-on' : tone === 'off' ? ' ap-badge-off' : ''}`} style={{ marginLeft: 0 }}>{taskStatusLabel(value)}</span>;
}

export default async function TasksPage({ searchParams }: { searchParams: Promise<Record<string, string | string[] | undefined>> }) {
  const me = (await getSession())!;
  const manager = can(me, 'tasks.view');
  const worker = can(me, 'tasks.work');
  if (!manager && !worker) return <NoAccess />;
  const params = await searchParams;

  if (!manager && worker) {
    const rawShow = Array.isArray(params.show) ? params.show[0] : params.show;
    const show: TaskShow = rawShow === 'all' || rawShow === 'completed' ? rawShow : 'open';
    const rawPage = Array.isArray(params.page) ? params.page[0] : params.page;
    const page = /^\d{1,5}$/.test(rawPage ?? '') && Number(rawPage) >= 1 && Number(rawPage) <= 10000 ? Number(rawPage) : 1;
    const result = await serverGet<Paged<TaskSummary>>(`/api/my/tasks?${workerTaskApiQuery(show, page)}`);
    if (!result.ok) return <LoadError status={result.status} />;
    const { items, totalCount, pageSize } = result.data;
    const pages = taskPageCount(totalCount, pageSize || 25);
    return <>
      <div className="ap-row" style={{ marginBottom: '1rem' }}><h1 style={{ margin: 0 }}>My production tasks</h1></div>
      <nav className="ap-tabs" aria-label="Task view">
        {(['open', 'completed', 'all'] as const).map((s) => <Link key={s} href={s === 'open' ? '/tasks' : `/tasks?show=${s}`} aria-current={show === s ? 'page' : undefined}>{s === 'open' ? 'Open tasks' : s === 'completed' ? 'Completed' : 'All tasks'}</Link>)}
      </nav>
      <section className="ap-panel" aria-label="My tasks">
        {items.length === 0 ? <p style={{ margin: 0 }}>{show === 'completed' ? 'No completed tasks yet.' : show === 'all' ? 'No tasks have been assigned to you.' : 'No open tasks assigned to you. You are up to date.'}</p> : <div className="ap-scroll"><table className="ap-table"><thead><tr><th>Task</th><th>Order</th><th>Stage</th><th>Status</th><th>Deadline</th><th>Priority</th></tr></thead><tbody>{items.map((t) => <tr key={t.id} className={taskNeedsAttention(t) ? 'ap-task-attention' : undefined}><td><Link href={`/tasks/${t.id}`}><strong>{t.title}</strong></Link><div className="ap-small ap-dim">{t.garmentName} · Qty {t.quantity}</div>{t.blockedReason && <div className="ap-small">Blocked: {t.blockedReason}</div>}</td><td>{t.orderNumber}</td><td>{t.stageName}</td><td><Status value={t.status} />{t.unmetDependencies > 0 && <div className="ap-small">{t.unmetDependencies} prerequisite(s) remain</div>}</td><td>{t.deadlineUtc ? formatDateTime(t.deadlineUtc, me.timeZoneId) : 'Not set'}{t.isOverdue && <div><span className="ap-badge ap-badge-off" style={{ marginLeft: 0 }}>Overdue</span></div>}</td><td>{t.priority}</td></tr>)}</tbody></table></div>}
        {totalCount > 0 && <div className="ap-row ap-pager"><span className="ap-hint">{totalCount} task{totalCount === 1 ? '' : 's'} · page {page} of {pages}</span><span className="ap-actions">{page > 1 && <Link className="ap-btn ap-btn-quiet ap-btn-sm" style={{ textDecoration: 'none' }} href={`/tasks?show=${show === 'open' ? 'open' : show}&page=${page - 1}`} rel="prev">Previous</Link>}{page < pages && <Link className="ap-btn ap-btn-quiet ap-btn-sm" style={{ textDecoration: 'none' }} href={`/tasks?show=${show}&page=${page + 1}`} rel="next">Next</Link>}</span></div>}
      </section>
      <p className="ap-foot" style={{ marginTop: '.75rem' }}>Task updates are not available in this first production-dashboard increment. This view is read-only; the API remains the authority for assignment and workflow rules.</p>
    </>;
  }

  const query = parseManagerTaskQuery(params);
  const result = await serverGet<Paged<TaskSummary>>(`/api/tasks?${managerTaskApiQuery(query)}`);
  if (!result.ok) return <LoadError status={result.status} />;
  const { items, totalCount, pageSize } = result.data;
  const pages = taskPageCount(totalCount, pageSize || 25);
  const filtered = !!(query.status || query.stage || query.overdue);
  return <>
    <div className="ap-row" style={{ marginBottom: '1rem' }}><h1 style={{ margin: 0 }}>Production</h1><span className="ap-foot">Manager view</span></div>
    <form method="get" action="/tasks" className="ap-panel ap-filter" role="search">
      <label className="ap-sr" htmlFor="task-status">Status</label><select id="task-status" name="status" className="ap-input" defaultValue={query.status}><option value="">All statuses</option>{TASK_STATUSES.map((s) => <option key={s} value={s}>{taskStatusLabel(s)}</option>)}</select>
      <label className="ap-sr" htmlFor="task-stage">Production stage</label><input id="task-stage" name="stage" className="ap-input" placeholder="Filter by stage" maxLength={80} defaultValue={query.stage} />
      <label className="ap-check"><input type="checkbox" name="overdue" value="true" defaultChecked={query.overdue} /> Overdue only</label>
      <button className="ap-btn" type="submit">Apply</button>{filtered && <Link href="/tasks" className="ap-btn ap-btn-quiet" style={{ textDecoration: 'none' }}>Clear</Link>}
    </form>
    <section className="ap-panel" style={{ marginTop: '1rem' }} aria-label="Production task list">
      {items.length === 0 ? <p style={{ margin: 0 }}>{filtered ? 'No production tasks match these filters.' : 'No production tasks yet. Confirm an order to generate its production tasks, or review the workflow configuration.'}</p> : <div className="ap-scroll"><table className="ap-table"><thead><tr><th>Task</th><th>Order / garment</th><th>Stage</th><th>Status</th><th>Assignee</th><th>Deadline</th><th>Priority</th></tr></thead><tbody>{items.map((t) => <tr key={t.id} className={taskNeedsAttention(t) ? 'ap-task-attention' : undefined}><td><Link href={`/tasks/${t.id}`}><strong>{t.title}</strong></Link><div className="ap-small ap-dim">{t.requiredSkill ? `Skill: ${t.requiredSkill}` : 'No specific skill required'}{t.needsEstimate ? ' · Estimate needed' : ''}</div>{t.blockedReason && <div className="ap-small">{t.blockedReason}</div>}</td><td><Link href={`/orders/${t.orderId}`}>{t.orderNumber}</Link><div className="ap-small ap-dim">{t.garmentName} · Qty {t.quantity}</div></td><td>{t.stageName}</td><td><Status value={t.status} />{t.unmetDependencies > 0 && <div className="ap-small">{t.unmetDependencies} prerequisite(s)</div>}{!t.isEligible && t.status !== 'Completed' && t.status !== 'Cancelled' && <div className="ap-small">Not yet eligible</div>}</td><td>{t.assigneeName ?? <span className="ap-dim">Unassigned</span>}</td><td>{t.deadlineUtc ? formatDateTime(t.deadlineUtc, me.timeZoneId) : 'Not set'}{t.isOverdue && <div><span className="ap-badge ap-badge-off" style={{ marginLeft: 0 }}>Overdue</span></div>}</td><td>{t.priority}</td></tr>)}</tbody></table></div>}
      {totalCount > 0 && <div className="ap-row ap-pager"><span className="ap-hint">{totalCount} task{totalCount === 1 ? '' : 's'} · page {query.page} of {pages}</span><span className="ap-actions">{query.page > 1 && <Link className="ap-btn ap-btn-quiet ap-btn-sm" style={{ textDecoration: 'none' }} href={managerTaskListHref(query, { page: query.page - 1 })} rel="prev">Previous</Link>}{query.page < pages && <Link className="ap-btn ap-btn-quiet ap-btn-sm" style={{ textDecoration: 'none' }} href={managerTaskListHref(query, { page: query.page + 1 })} rel="next">Next</Link>}</span></div>}
    </section>
    <p className="ap-foot" style={{ marginTop: '.75rem' }}>This first production-dashboard increment provides live task visibility and filters. Assignment, scheduling and worker status actions remain follow-up work; all access and workflow enforcement stays on the API.</p>
  </>;
}
