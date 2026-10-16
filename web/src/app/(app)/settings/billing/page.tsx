import Link from 'next/link';
import { BillingManager } from '@/components/app/BillingManager';
import { LoadError, NoAccess } from '@/components/app/Notices';
import { getSession, serverGet } from '@/lib/session';
import { can } from '@/lib/team';
export const metadata={title:'Subscription billing · Avzar'};export const dynamic='force-dynamic';
type Plan={code:string;name:string;description:string|null;currencyCode:string;monthlyPrice:number;annualPrice:number|null;entitlements:Record<string,string>};
type Catalog={entitlements:unknown[];plans:Plan[]};type Current={planCode:string;planName:string;status:string;items:{key:string;value:string;description:string;used:number|null}[]};
export default async function BillingPage(){const me=(await getSession())!;if(!can(me,'business.settings'))return <NoAccess/>;const [catalog,current]=await Promise.all([serverGet<Catalog>('/api/plans'),serverGet<Current>('/api/billing/entitlements')]);if(!catalog.ok)return <LoadError status={catalog.status}/>;if(!current.ok)return <LoadError status={current.status}/>;return <div className="ap-stack"><p><Link href="/settings">← Business settings</Link></p><h1>Subscription billing</h1><BillingManager catalog={catalog.data} current={current.data}/></div>;}
