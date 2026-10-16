'use client';

import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { useEffect, useState } from 'react';
import { SignOutButton } from './SignOutButton';

type Item = { href: string; label: string };
type Props = { items: Item[]; businessName: string; fullName: string; email: string };

/** The longest matching menu entry is the current page, so /tasks/schedule does not also light up /tasks. */
function currentHref(path: string, items: Item[]): string | null {
  const hits = items.filter((i) => path === i.href || path.startsWith(`${i.href}/`));
  return hits.sort((a, b) => b.href.length - a.href.length)[0]?.href ?? null;
}

/** Sidebar on wide screens. On phones: a top bar with a menu button that opens the same links as a drawer. */
export function AppNav({ items, businessName, fullName, email }: Props) {
  const path = usePathname();
  const [open, setOpen] = useState(false);
  const current = currentHref(path, items);

  useEffect(() => setOpen(false), [path]);
  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') setOpen(false); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [open]);

  return (
    <>
      <header className="ap-topbar">
        <Link href="/dashboard" className="ap-logo">Avzar</Link>
        <span className="ap-topbiz">{businessName}</span>
        <button type="button" className="ap-burger" aria-expanded={open} aria-controls="ap-drawer" onClick={() => setOpen((v) => !v)}>
          <span className="ap-sr">{open ? 'Close menu' : 'Open menu'}</span>
          <span aria-hidden="true" className="ap-burger-bars"><i /><i /><i /></span>
        </button>
      </header>

      {open && <button type="button" className="ap-scrim" aria-label="Close menu" tabIndex={-1} onClick={() => setOpen(false)} />}

      <aside id="ap-drawer" className={`ap-side${open ? ' is-open' : ''}`}>
        <div>
          <Link href="/dashboard" className="ap-logo ap-logo-side">Avzar</Link>
          <p className="ap-biz">{businessName}</p>
          <p className="ap-who">{fullName} · {email}</p>
        </div>
        <nav aria-label="Main">
          <ul>
            {items.map((i) => (
              <li key={i.href}>
                <Link href={i.href} aria-current={i.href === current ? 'page' : undefined}>{i.label}</Link>
              </li>
            ))}
          </ul>
        </nav>
        <Link href="/help" className="ap-helplink">Help and support</Link>
        <SignOutButton />
      </aside>
    </>
  );
}
