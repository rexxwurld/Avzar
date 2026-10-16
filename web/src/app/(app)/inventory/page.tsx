import { InventoryManager } from '@/components/app/InventoryManager';
import { NoAccess } from '@/components/app/Notices';
import { getSession } from '@/lib/session';
import { can } from '@/lib/team';
export const metadata={title:'Inventory · Avzar'}; export const dynamic='force-dynamic';
export default async function InventoryPage(){const me=(await getSession())!;if(!can(me,'inventory.view')&&!can(me,'inventory.manage'))return <NoAccess/>;return <div className="ap-stack"><div><h1 style={{marginBottom:'.25rem'}}>Inventory</h1><p className="ap-foot" style={{margin:0}}>Track fabrics and supplies, monitor reorder levels, and reserve, release or consume stock with a movement history in the backend.</p></div><InventoryManager canManage={can(me,'inventory.manage')}/></div>;}
