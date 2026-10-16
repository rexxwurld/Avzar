'use client';

import { useRouter } from 'next/navigation';
import { useState } from 'react';
import { api } from '@/lib/client';

export function SignOutButton() {
  const router = useRouter();
  const [busy, setBusy] = useState(false);
  async function signOut() {
    if (busy) return;
    setBusy(true);
    try { await api('/api/auth/logout', { method: 'POST' }); } catch { /* an expired session is already signed out */ }
    router.replace('/login');
    router.refresh();
  }
  return <button type="button" className="ap-btn ap-btn-quiet" onClick={signOut} disabled={busy}>{busy ? 'Signing out…' : 'Sign out'}</button>;
}
