'use client';

import { useRouter } from 'next/navigation';
import { useState } from 'react';
import { api } from '@/lib/client';
import type { TaskDetail, WorkerOption } from '@/lib/production-types';

type Props = {
  detail: TaskDetail;
  isWorker: boolean;
  canAssign: boolean;
  canManage: boolean;
  canOverride: boolean;
  workers: WorkerOption[];
};

export function TaskActions({ detail, isWorker, canAssign, canManage, canOverride, workers }: Props) {
  const router = useRouter();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [note, setNote] = useState('');
  const [overrideReason, setOverrideReason] = useState('');
  const [cancelReason, setCancelReason] = useState('');
  const [assignee, setAssignee] = useState(detail.task.assigneeMembershipId ?? '');
  const [override, setOverride] = useState(false);
  const [issueType, setIssueType] = useState('MissingMaterials');
  const [issue, setIssue] = useState('');
  const [checks, setChecks] = useState<string[]>(detail.completedChecks);
  const [completionNotes, setCompletionNotes] = useState('');
  const t = detail.task;
  const open = t.status !== 'Completed' && t.status !== 'Cancelled';

  async function act(path: string, method: 'POST' | 'PUT' = 'POST', body?: unknown) {
    setBusy(true); setError('');
    try { await api(path, { method, ...(body === undefined ? {} : { json: body }) }); router.refresh(); }
    catch (e) { setError(e instanceof Error ? e.message : 'The task action failed. Try again.'); }
    finally { setBusy(false); }
  }

  return <section className="ap-panel ap-stack" aria-labelledby="task-actions-heading">
    <h2 id="task-actions-heading" className="ap-h2">Task actions</h2>
    {error && <p className="ap-alert" role="alert">{error}</p>}
    {isWorker && open && <div className="ap-stack">
      <div className="ap-actions">
        {(t.status === 'Pending' || t.status === 'Scheduled') && <button className="ap-btn" disabled={busy || !t.isEligible} onClick={() => void act(`/api/my/tasks/${t.id}/start`)}>Start task</button>}
        {t.status === 'InProgress' && <><button className="ap-btn ap-btn-quiet" disabled={busy} onClick={() => void act(`/api/my/tasks/${t.id}/pause`, 'POST', { note: note || null })}>Pause</button><button className="ap-btn" disabled={busy || detail.requiredChecks.some((c) => !checks.includes(c))} onClick={() => void act(`/api/my/tasks/${t.id}/complete`, 'POST', { notes: completionNotes || null, completedChecks: checks, alterationsRequired: false })}>Complete task</button></>}
        {t.status === 'Paused' && <button className="ap-btn" disabled={busy} onClick={() => void act(`/api/my/tasks/${t.id}/resume`)}>Resume</button>}
      </div>
      {t.status === 'InProgress' && <div className="ap-field"><label htmlFor="task-completion-notes">Completion notes (optional)</label><textarea id="task-completion-notes" className="ap-input" value={completionNotes} onChange={(e) => setCompletionNotes(e.target.value)} maxLength={2000} rows={2} /></div>}
      {t.status === 'InProgress' && detail.requiredChecks.length > 0 && <fieldset className="ap-fieldset"><legend>Completion checklist</legend>{detail.requiredChecks.map((c) => <label className="ap-check" key={c}><input type="checkbox" checked={checks.includes(c)} onChange={(e) => setChecks((old) => e.target.checked ? [...old.filter((x) => x !== c), c] : old.filter((x) => x !== c))} />{c}</label>)}</fieldset>}
      <div className="ap-field"><label htmlFor="task-pause-note">Pause note (optional)</label><input id="task-pause-note" className="ap-input" value={note} onChange={(e) => setNote(e.target.value)} maxLength={1000} /></div>
      <fieldset className="ap-fieldset"><legend>Report a problem</legend><div className="ap-field"><label htmlFor="task-issue-type">Problem type</label><select id="task-issue-type" className="ap-input" value={issueType} onChange={(e) => setIssueType(e.target.value)}><option value="MissingMaterials">Missing materials</option><option value="Defect">Defect</option><option value="Clarification">Need clarification</option><option value="Other">Other</option></select></div><div className="ap-field"><label htmlFor="task-issue-description">Describe the problem</label><textarea id="task-issue-description" className="ap-input" value={issue} onChange={(e) => setIssue(e.target.value)} minLength={3} maxLength={2000} rows={3} /></div><button className="ap-btn ap-btn-quiet" disabled={busy || issue.trim().length < 3} onClick={() => void act(`/api/my/tasks/${t.id}/issues`, 'POST', { type: issueType, description: issue.trim() })}>Report problem</button></fieldset>
    </div>}
    {canAssign && open && <fieldset className="ap-fieldset"><legend>Assignment</legend><div className="ap-field"><label htmlFor="task-assignee">Assigned worker</label><select id="task-assignee" className="ap-input" value={assignee} onChange={(e) => setAssignee(e.target.value)}><option value="">Unassigned</option>{workers.filter((w) => w.isActive || w.membershipId === t.assigneeMembershipId).map((w) => <option key={w.membershipId} value={w.membershipId}>{w.displayName}{!w.isActive ? ' · inactive' : ''} · {w.openTasks} open tasks{w.skills.length ? ` · ${w.skills.join(', ')}` : ''}</option>)}</select></div>{canOverride && <label className="ap-check"><input type="checkbox" checked={override} onChange={(e) => setOverride(e.target.checked)} /> Override skill/location checks</label>}{override && <div className="ap-field"><label htmlFor="task-override-reason">Override reason (minimum 5 characters)</label><textarea id="task-override-reason" className="ap-input" value={overrideReason} onChange={(e) => setOverrideReason(e.target.value)} minLength={5} maxLength={500} rows={2} /></div>}<button className="ap-btn" disabled={busy || (override && overrideReason.trim().length < 5)} onClick={() => void act(`/api/tasks/${t.id}/assign`, 'POST', { membershipId: assignee || null, override, reason: override ? overrideReason.trim() : null })}>Save assignment</button></fieldset>}
    {canManage && t.status === 'Blocked' && <fieldset className="ap-fieldset"><legend>Unblock task</legend><div className="ap-field"><label htmlFor="task-unblock-note">Resolution note (optional)</label><textarea id="task-unblock-note" className="ap-input" value={note} onChange={(e) => setNote(e.target.value)} maxLength={1000} rows={2} /></div><button className="ap-btn" disabled={busy} onClick={() => void act(`/api/tasks/${t.id}/unblock`, 'POST', { note: note || null })}>Unblock</button></fieldset>}
    {canManage && open && <fieldset className="ap-fieldset"><legend>Cancel task</legend><div className="ap-field"><label htmlFor="task-cancel-reason">Reason (minimum 3 characters)</label><textarea id="task-cancel-reason" className="ap-input" value={cancelReason} onChange={(e) => setCancelReason(e.target.value)} minLength={3} maxLength={500} rows={2} /></div><button className="ap-btn ap-btn-danger" disabled={busy || cancelReason.trim().length < 3} onClick={() => { if (window.confirm('Cancel this production task? This action is recorded in the task history.')) void act(`/api/tasks/${t.id}/cancel`, 'POST', { reason: cancelReason.trim() }); }}>Cancel task</button></fieldset>}
    {!isWorker && !canAssign && !canManage && <p style={{ margin: 0 }}>You can view this task but do not have permission to change it.</p>}
    {busy && <p className="ap-foot" role="status">Saving task change…</p>}
  </section>;
}
