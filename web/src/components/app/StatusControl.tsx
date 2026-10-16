'use client';

import { useRouter } from 'next/navigation';
import { useState } from 'react';
import { api } from '@/lib/client';
import { messageFor } from '@/lib/problem';

export function StatusControl({ customerId, status }: { customerId: string; status: 'Active' | 'Inactive' }) {
  const router = useRouter();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const next = status === 'Active' ? 'Inactive' : 'Active';

  async function change() {
    if (busy) return;
    setBusy(true); setError(null);
    try {
      await api(`/api/customers/${customerId}/status`, { method: 'POST', json: { status: next } });
      router.refresh();
    } catch (err) {
      setError(messageFor(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div>
      {error && <p role="alert" className="ap-alert">{error}</p>}
      <p className="ap-hint" style={{ margin: '0 0 .5rem' }}>
        {status === 'Active' ? 'Inactive customers stay on record but cannot get new fittings.' : 'Reactivating counts toward your plan’s active customer limit.'}
      </p>
      <button type="button" className="ap-btn ap-btn-quiet" onClick={change} disabled={busy}>{busy ? 'Saving…' : `Mark ${next.toLowerCase()}`}</button>
    </div>
  );
}
