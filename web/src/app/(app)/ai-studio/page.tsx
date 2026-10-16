import { AiStudio } from '@/components/app/AiStudio';
import { NoAccess } from '@/components/app/Notices';
import { getSession } from '@/lib/session';
import { can } from '@/lib/team';
export const metadata={title:'AI studio · Avzar'}; export const dynamic='force-dynamic';
export default async function AiStudioPage(){const me=(await getSession())!;const canDesign=can(me,'designs.manage');const canBuildAssistant=can(me,'storefront.edit');if(!canDesign&&!canBuildAssistant)return <NoAccess/>;return <div className="ap-stack"><div><h1 style={{marginBottom:'.25rem'}}>AI studio</h1><p className="ap-foot" style={{margin:0}}>Fashion concept generation and storefront assistant configuration. Access depends on permissions and your plan's AI entitlements.</p></div><AiStudio canDesign={canDesign} canBuildAssistant={canBuildAssistant} canSave={can(me,'media.manage')}/></div>;}
