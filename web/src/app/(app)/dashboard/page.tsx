import Link from 'next/link';
import { getSession, serverGet } from '@/lib/session';
import { can } from '@/lib/team';
import { money } from '@/lib/format';
export const metadata = { title: 'Dashboard · Avzar' };
export const dynamic = 'force-dynamic';
type Report={currencyCode:string;ordersCreated:number;ordersDelivered:number;orderValue:number;verifiedRevenue:number;expenses:number;grossAfterExpenses:number;openTasks:number;completedTasks:number;overdueTasks:number};
type Paged<T>={items:T[];totalCount:number};
type Task={id:string;title:string;stageName:string;status:string;deadlineUtc:string|null};
export default async function DashboardPage() {
  const user = (await getSession())!;
  const [report,orders,myTasks] = await Promise.all([
    can(user,'reports.view') ? serverGet<Report>('/api/reports/summary') : Promise.resolve(null),
    can(user,'orders.view') ? serverGet<Paged<unknown>>('/api/orders?page=1&pageSize=1') : Promise.resolve(null),
    can(user,'tasks.work') ? serverGet<Paged<Task>>('/api/my/tasks?page=1&pageSize=5&show=open') : Promise.resolve(null),
  ]);
  const metrics = report?.ok ? report.data : null;
  return (
    <div className="ap-stack">
      <div className="ap-row"><h1 style={{margin:0}}>Dashboard</h1><div className="ap-actions"><Link href="/orders" className="ap-btn ap-btn-quiet" style={{textDecoration:'none'}}>Orders</Link><Link href="/tasks" className="ap-btn" style={{textDecoration:'none'}}>Production</Link></div></div>
      <section className="ap-panel" aria-labelledby="acct"><h2 id="acct" style={{ marginTop: 0, fontSize: '1.1rem' }}>Your business</h2><dl className="ap-dl"><dt>Business</dt><dd>{user.businessName}</dd><dt>Signed in as</dt><dd>{user.fullName} ({user.email})</dd><dt>Role</dt><dd>{user.isOwner ? 'Owner' : 'Staff'}</dd><dt>Time zone</dt><dd>{user.timeZoneId}</dd><dt>Currency</dt><dd>{user.currencyCode}</dd></dl></section>
      {metrics && <section className="ap-stack"><div><h2 className="ap-h2">Business overview</h2><p className="ap-foot">Last 30 days · verified payments are revenue; pending claims are excluded.</p></div><div className="ap-grid2">{[['Orders created',String(metrics.ordersCreated)],['Orders delivered',String(metrics.ordersDelivered)],['Order value',money(metrics.orderValue,metrics.currencyCode)],['Verified revenue',money(metrics.verifiedRevenue,metrics.currencyCode)],['Expenses',money(metrics.expenses,metrics.currencyCode)],['Revenue less expenses',money(metrics.grossAfterExpenses,metrics.currencyCode)],['Open production tasks',String(metrics.openTasks)],['Overdue tasks',String(metrics.overdueTasks)]].map(([label,value])=><section className="ap-panel" key={label}><p className="ap-foot" style={{margin:'0 0 .35rem'}}>{label}</p><strong style={{fontSize:'1.25rem'}}>{value}</strong></section>)}</div></section>}
      {orders?.ok && <section className="ap-panel ap-stack"><div className="ap-row"><h2 className="ap-h2">Order workspace</h2><span className="ap-foot">{orders.data.totalCount} total orders</span></div><Link href="/orders" className="ap-btn ap-btn-quiet" style={{textDecoration:'none',alignSelf:'flex-start'}}>Manage orders</Link></section>}
      {myTasks?.ok && <section className="ap-panel ap-stack"><div className="ap-row"><h2 className="ap-h2">My assigned work</h2><Link href="/tasks" className="ap-btn ap-btn-quiet ap-btn-sm" style={{textDecoration:'none'}}>Open production</Link></div>{myTasks.data.items.length===0?<p className="ap-foot">No open tasks assigned to you.</p>:<div className="ap-scroll"><table className="ap-table"><thead><tr><th>Task</th><th>Stage</th><th>Status</th><th>Deadline</th></tr></thead><tbody>{myTasks.data.items.map(t=><tr key={t.id}><td>{t.title}</td><td>{t.stageName}</td><td>{t.status}</td><td>{t.deadlineUtc?new Date(t.deadlineUtc).toLocaleString(): '—'}</td></tr>)}</tbody></table></div>}</section>}
    </div>
  );
}
