'use client';

import { useRouter } from 'next/navigation';
import { useState } from 'react';
import { api } from '@/lib/client';
import type { Order } from '@/lib/order-types';
import { messageFor } from '@/lib/problem';
import { orderActionAvailability } from '@/lib/orders';

export function OrderActions({ order, canManage, canConfirm, canEdit }: { order: Order; canManage: boolean; canConfirm: boolean; canEdit: boolean }) {
  const router = useRouter();
  const [reason, setReason] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const available = orderActionAvailability(order.status, canManage, canConfirm);

  async function transition(action: 'quote' | 'confirm' | 'cancel') {
    if (busy) return;
    if (action === 'cancel' && reason.trim().length < 3) { setError('Enter a cancellation reason of at least 3 characters.'); return; }
    if (action === 'cancel' && !window.confirm('Cancel this order? This cannot be undone. If it has invoices, they must be voided first.')) return;
    setBusy(true); setError(null);
    try {
      await api<Order>(`/api/orders/${order.id}/${action}`, {
        method: 'POST',
        ...(action === 'cancel' ? { json: { reason: reason.trim() } } : {}),
      });
      router.refresh();
    } catch (e) { setError(messageFor(e)); }
    finally { setBusy(false); }
  }

  if (!canManage && !canConfirm) return null;
  return (
    <section className="ap-panel ap-stack" aria-labelledby="order-actions-heading">
      <h2 id="order-actions-heading" className="ap-h2">Order actions</h2>
      {error && <p role="alert" className="ap-alert">{error}</p>}
      <div className="ap-row" style={{ justifyContent: 'flex-start' }}>
        {available.edit && canEdit && <button type="button" className="ap-btn ap-btn-quiet" onClick={() => router.push(`/orders/${order.id}/edit`)}>Edit order</button>}
        {available.quote && <button type="button" className="ap-btn" onClick={() => void transition('quote')} disabled={busy}>{busy ? 'Working…' : 'Issue quotation'}</button>}
        {available.confirm && <button type="button" className="ap-btn" onClick={() => void transition('confirm')} disabled={busy}>{busy ? 'Working…' : 'Confirm order'}</button>}
      </div>
      {available.cancel && (
        <div className="ap-grid2">
          <div className="ap-field"><label htmlFor="order-cancel-reason">Cancellation reason</label><input id="order-cancel-reason" className="ap-input" maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} placeholder="Why is this order being cancelled?" /></div>
          <div className="ap-row" style={{ justifyContent: 'flex-start', alignSelf: 'end' }}><button type="button" className="ap-btn ap-btn-danger" onClick={() => void transition('cancel')} disabled={busy}>{busy ? 'Working…' : 'Cancel order'}</button></div>
        </div>
      )}
      <p className="ap-hint">Avzar validates every transition on the server. Confirming a quoted order also creates its production tasks; cancellation may be blocked while invoices remain active.</p>
    </section>
  );
}
