'use client';

import { useRouter } from 'next/navigation';
import { useState, type FormEvent } from 'react';
import type { CurrentUser } from '@/lib/auth-types';
import { api } from '@/lib/client';
import { ApiError, messageFor } from '@/lib/problem';
import type { Invitation, Location, Role, StaffMember } from '@/lib/staff-types';
import { assignableRoles, roleNames, sameSet, toggle } from '@/lib/team';
import { Field } from './Field';

interface Props { me: CurrentUser; staff: StaffMember[]; roles: Role[]; locations: Location[]; canManage: boolean }

function RoleLocationPickers(props: {
  idPrefix: string; roles: Role[]; locations: Location[];
  roleIds: string[]; locationIds: string[];
  onRoles: (v: string[]) => void; onLocations: (v: string[]) => void;
}) {
  const { idPrefix, roles, locations, roleIds, locationIds, onRoles, onLocations } = props;
  return (
    <>
      <fieldset className="ap-fieldset">
        <legend>Roles (at least one)</legend>
        {assignableRoles(roles).map((r) => (
          <div key={r.id} className="ap-check">
            <input id={`${idPrefix}-r-${r.id}`} type="checkbox" checked={roleIds.includes(r.id)} onChange={(e) => onRoles(toggle(roleIds, r.id, e.target.checked))} />
            <label htmlFor={`${idPrefix}-r-${r.id}`}>{r.name}</label>
          </div>
        ))}
      </fieldset>
      <fieldset className="ap-fieldset">
        <legend>Locations</legend>
        {locations.filter((l) => l.isActive).map((l) => (
          <div key={l.id} className="ap-check">
            <input id={`${idPrefix}-l-${l.id}`} type="checkbox" checked={locationIds.includes(l.id)} onChange={(e) => onLocations(toggle(locationIds, l.id, e.target.checked))} />
            <label htmlFor={`${idPrefix}-l-${l.id}`}>{l.name}</label>
          </div>
        ))}
        <p className="ap-hint">Without a location, staff see no orders or fittings unless their role includes the &ldquo;locations all&rdquo; permission.</p>
      </fieldset>
    </>
  );
}

function InvitePanel({ roles, locations }: { roles: Role[]; locations: Location[] }) {
  const router = useRouter();
  const [open, setOpen] = useState(false);
  const [fullName, setFullName] = useState('');
  const [email, setEmail] = useState('');
  const [roleIds, setRoleIds] = useState<string[]>([]);
  const [locationIds, setLocationIds] = useState<string[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [fields, setFields] = useState<Record<string, string[]>>({});
  const [sent, setSent] = useState<Invitation | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (busy) return;
    setError(null); setFields({}); setSent(null);
    if (roleIds.length === 0) { setError('Choose at least one role.'); return; }
    setBusy(true);
    try {
      const inv = await api<Invitation>('/api/staff/invitations', { method: 'POST', json: { email: email.trim(), fullName: fullName.trim(), roleIds, locationIds } });
      setSent(inv);
      setFullName(''); setEmail(''); setRoleIds([]); setLocationIds([]); setOpen(false);
      router.refresh();
    } catch (err) {
      if (err instanceof ApiError) setFields(err.fields);
      setError(messageFor(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="ap-panel" aria-labelledby="invite-h">
      <div className="ap-row">
        <h2 id="invite-h" className="ap-h2">Invite someone</h2>
        {!open && <button type="button" className="ap-btn" onClick={() => { setOpen(true); setSent(null); }}>Invite staff</button>}
      </div>
      {sent && <p role="status" className="ap-ok">Invitation sent to {sent.email}. It expires {new Date(sent.expiresAtUtc).toLocaleString()}.</p>}
      {open && (
        <form onSubmit={submit} className="ap-form" aria-busy={busy}>
          {error && <p role="alert" className="ap-alert">{error}</p>}
          <Field id="inv-name" label="Full name" required minLength={2} maxLength={120} value={fullName} onChange={setFullName} error={fields.fullName?.[0]} />
          <Field id="inv-email" label="Email" type="email" required maxLength={256} value={email} onChange={setEmail} error={fields.email?.[0]} />
          <RoleLocationPickers idPrefix="inv" roles={roles} locations={locations} roleIds={roleIds} locationIds={locationIds} onRoles={setRoleIds} onLocations={setLocationIds} />
          <div className="ap-row">
            <button className="ap-btn" type="submit" disabled={busy}>{busy ? 'Sending…' : 'Send invitation'}</button>
            <button className="ap-btn ap-btn-quiet" type="button" onClick={() => { setOpen(false); setError(null); }} disabled={busy}>Cancel</button>
          </div>
        </form>
      )}
    </section>
  );
}

function MemberRow({ member, me, roles, locations, canManage }: { member: StaffMember; me: CurrentUser; roles: Role[]; locations: Location[]; canManage: boolean }) {
  const router = useRouter();
  const activeIds = new Set(locations.filter((l) => l.isActive).map((l) => l.id));
  const origLocations = member.locationIds.filter((id) => activeIds.has(id));
  const [editing, setEditing] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const [roleIds, setRoleIds] = useState(member.roleIds);
  const [locationIds, setLocationIds] = useState(origLocations);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const isSelf = member.userId === me.userId;
  const deactivated = member.status === 'Deactivated';
  const locName = new Map(locations.map((l) => [l.id, l.name]));
  const locs = member.locationIds.map((id) => locName.get(id)).filter(Boolean).join(', ');

  async function run(fn: () => Promise<void>) {
    if (busy) return;
    setBusy(true); setError(null);
    try { await fn(); router.refresh(); } catch (err) { setError(messageFor(err)); } finally { setBusy(false); }
  }

  async function save() {
    if (roleIds.length === 0) { setError('Choose at least one role.'); return; }
    await run(async () => {
      if (!sameSet(roleIds, member.roleIds)) await api(`/api/staff/${member.membershipId}/roles`, { method: 'PUT', json: { roleIds } });
      if (!sameSet(locationIds, origLocations)) await api(`/api/staff/${member.membershipId}/locations`, { method: 'PUT', json: { locationIds } });
      setEditing(false);
    });
  }

  const setStatus = (action: 'deactivate' | 'reactivate') => run(async () => {
    await api(`/api/staff/${member.membershipId}/${action}`, { method: 'POST' });
    setConfirming(false);
  });

  return (
    <>
      <tr className={deactivated ? 'ap-dim' : undefined}>
        <td><strong>{member.displayName}</strong>{isSelf && <span className="ap-badge">You</span>}<br /><span className="ap-hint">{member.email}</span></td>
        <td>{member.isOwner ? <span className="ap-badge">Owner</span> : (roleNames(member.roleIds, roles).join(', ') || '—')}</td>
        <td>{member.isOwner ? 'All' : (locs || '—')}</td>
        <td><span className={deactivated ? 'ap-badge ap-badge-off' : 'ap-badge ap-badge-on'}>{member.status}</span></td>
        {canManage && (
          <td>
            {!member.isOwner && (
              <div className="ap-actions">
                {!deactivated && <button type="button" className="ap-btn ap-btn-quiet ap-btn-sm" onClick={() => { setEditing(!editing); setConfirming(false); setError(null); }} aria-expanded={editing}>Edit access</button>}
                {deactivated && <button type="button" className="ap-btn ap-btn-quiet ap-btn-sm" onClick={() => setStatus('reactivate')} disabled={busy}>Reactivate</button>}
                {!deactivated && !isSelf && !confirming && <button type="button" className="ap-btn ap-btn-quiet ap-btn-sm" onClick={() => { setConfirming(true); setEditing(false); }}>Deactivate</button>}
                {confirming && (
                  <>
                    <button type="button" className="ap-btn ap-btn-danger ap-btn-sm" onClick={() => setStatus('deactivate')} disabled={busy}>Confirm deactivate</button>
                    <button type="button" className="ap-btn ap-btn-quiet ap-btn-sm" onClick={() => setConfirming(false)} disabled={busy}>Cancel</button>
                  </>
                )}
              </div>
            )}
          </td>
        )}
      </tr>
      {(editing || error) && (
        <tr>
          <td colSpan={canManage ? 5 : 4}>
            {error && <p role="alert" className="ap-alert">{error}</p>}
            {editing && (
              <div className="ap-form">
                <RoleLocationPickers idPrefix={`m-${member.membershipId}`} roles={roles} locations={locations} roleIds={roleIds} locationIds={locationIds} onRoles={setRoleIds} onLocations={setLocationIds} />
                <div className="ap-row">
                  <button type="button" className="ap-btn" onClick={save} disabled={busy || (sameSet(roleIds, member.roleIds) && sameSet(locationIds, origLocations))}>{busy ? 'Saving…' : 'Save access'}</button>
                  <button type="button" className="ap-btn ap-btn-quiet" onClick={() => { setEditing(false); setRoleIds(member.roleIds); setLocationIds(origLocations); setError(null); }} disabled={busy}>Cancel</button>
                </div>
              </div>
            )}
          </td>
        </tr>
      )}
    </>
  );
}

function OwnershipTransferPanel({ staff }: { staff: StaffMember[] }) {
  const [targetId, setTargetId] = useState('');
  const [password, setPassword] = useState('');
  const [twoFactorCode, setTwoFactorCode] = useState('');
  const [recoveryCode, setRecoveryCode] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [sent, setSent] = useState(false);
  const candidates = staff.filter((m) => m.status === 'Active' && !m.isOwner);

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (busy || !targetId) return;
    setBusy(true); setError(null); setSent(false);
    try {
      await api<{ status: string }>('/api/business/ownership-transfer', { method: 'POST', json: {
        newOwnerMembershipId: targetId, password: password,
        twoFactorCode: twoFactorCode.trim() || undefined, recoveryCode: recoveryCode.trim() || undefined,
      } });
      setSent(true); setPassword(''); setTwoFactorCode(''); setRecoveryCode('');
    } catch (err) { setError(messageFor(err)); }
    finally { setBusy(false); }
  }

  return <section className="ap-panel ap-stack" aria-labelledby="ownership-transfer-heading">
    <h2 id="ownership-transfer-heading" className="ap-h2">Transfer business ownership</h2>
    <p className="ap-foot">This does not transfer ownership immediately. After you re-enter your password, Avzar emails you a one-time confirmation link that expires in 30 minutes. The transfer occurs only after you open that link while signed in and confirm.</p>
    {error && <p role="alert" className="ap-alert">{error}</p>}
    {sent && <p role="status" className="ap-ok">Confirmation email queued. Ownership has not changed. Open the link sent to your account email to finish the transfer.</p>}
    {candidates.length === 0 ? <p className="ap-hint">There are no other active staff members to transfer ownership to.</p> : <form className="ap-form" onSubmit={submit}>
      <div className="ap-field"><label htmlFor="ownership-target">New owner</label><select id="ownership-target" className="ap-input" required value={targetId} disabled={busy} onChange={(e) => setTargetId(e.target.value)}><option value="">Choose an active staff member</option>{candidates.map((m) => <option key={m.membershipId} value={m.membershipId}>{m.displayName} · {m.email}</option>)}</select></div>
      <div className="ap-field"><label htmlFor="ownership-password">Your password</label><input id="ownership-password" className="ap-input" type="password" autoComplete="current-password" required maxLength={128} value={password} disabled={busy} onChange={(e) => setPassword(e.target.value)} /></div>
      <div className="ap-field"><label htmlFor="ownership-2fa">Authenticator code (if enabled)</label><input id="ownership-2fa" className="ap-input" inputMode="numeric" autoComplete="one-time-code" maxLength={10} value={twoFactorCode} disabled={busy} onChange={(e) => setTwoFactorCode(e.target.value)} /></div>
      <div className="ap-field"><label htmlFor="ownership-recovery">Recovery code (instead of authenticator code)</label><input id="ownership-recovery" className="ap-input" autoComplete="off" maxLength={80} value={recoveryCode} disabled={busy} onChange={(e) => setRecoveryCode(e.target.value)} /></div>
      <button className="ap-btn ap-btn-danger" type="submit" disabled={busy || !targetId || !password.trim()}>{busy ? 'Requesting confirmation…' : 'Email confirmation link'}</button>
    </form>}
  </section>;
}

export function TeamView({ me, staff, roles, locations, canManage }: Props) {
  return (
    <div className="ap-stack">
      {me.isOwner && <OwnershipTransferPanel staff={staff} />}
      {canManage && <InvitePanel roles={roles} locations={locations} />}
      <section className="ap-panel" aria-labelledby="team-h">
        <h2 id="team-h" className="ap-h2">Team ({staff.length})</h2>
        <div className="ap-scroll">
          <table className="ap-table">
            <thead><tr><th>Person</th><th>Roles</th><th>Locations</th><th>Status</th>{canManage && <th><span className="ap-sr">Actions</span></th>}</tr></thead>
            <tbody>
              {staff.map((m) => (
                <MemberRow key={`${m.membershipId}:${m.status}:${m.roleIds.join(',')}:${m.locationIds.join(',')}`} member={m} me={me} roles={roles} locations={locations} canManage={canManage} />
              ))}
            </tbody>
          </table>
        </div>
      </section>
    </div>
  );
}
