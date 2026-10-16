import Link from 'next/link';
import { redirect } from 'next/navigation';
import type { Metadata } from 'next';
import { SignOutButton } from '@/components/app/SignOutButton';
import { visibleNav } from '@/lib/nav';
import { getSession } from '@/lib/session';
import '../app.css';

export const metadata: Metadata = { robots: { index: false, follow: false } };
export const dynamic = 'force-dynamic';

export default async function AppLayout({ children }: { children: React.ReactNode }) {
  const user = await getSession();
  if (!user) redirect('/login');
  const items = visibleNav(user);
  return (
    <div className="ap ap-shell">
      <aside className="ap-side">
        <div>
          <p className="ap-biz">{user.businessName}</p>
          <p className="ap-who">{user.fullName} · {user.email}</p>
        </div>
        <nav aria-label="Main">
          <ul>{items.map((i) => <li key={i.href}><Link href={i.href}>{i.label}</Link></li>)}</ul>
        </nav>
        <SignOutButton />
      </aside>
      <main className="ap-main">{children}</main>
    </div>
  );
}
