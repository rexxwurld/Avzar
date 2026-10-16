'use client';

import { useState, type FormEvent } from 'react';
import { api } from '@/lib/client';
import { messageFor } from '@/lib/problem';

type Slot = { taskId: string; startUtc: string; endUtc: string; version: number };
type Unscheduled = { taskId: string; reason: string };
type Preview = {
  startUtc: string;
  horizonDays: number;
  timeZoneId: string;
  scheduled: Slot[];
  unscheduled: Unscheduled[];
  taskVersions: Record<string, number>;
  isPreviewOnly: boolean;
};
type Applied = { updatedTasks: number; appliedAtUtc: string; notificationsQueued: boolean };

function displayTime(value: string, timeZoneId: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return new Intl.DateTimeFormat(undefined, {
    timeZone: timeZoneId,
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(date);
}

export function ShopSchedulePreview({ timeZoneId }: { timeZoneId: string }) {
  const [startDate, setStartDate] = useState(() => new Date().toISOString().slice(0, 10));
  const [horizonDays, setHorizonDays] = useState(30);
  const [preview, setPreview] = useState<Preview | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [applied, setApplied] = useState<Applied | null>(null);

  async function generate(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setError('');
    setPreview(null);
    setApplied(null);
    try {
      const result = await api<Preview>('/api/tasks/schedule/preview', {
        method: 'POST',
        json: { startUtc: `${startDate}T00:00:00+00:00`, horizonDays },
      });
      setPreview(result);
    } catch (err) {
      const message = messageFor(err);
      setError(message);
    } finally {
      setBusy(false);
    }
  }

  async function applySchedule() {
    if (!preview || preview.unscheduled.length > 0 || preview.scheduled.length !== Object.keys(preview.taskVersions).length) return;
    setBusy(true);
    setError('');
    setApplied(null);
    try {
      const result = await api<Applied>('/api/tasks/schedule/apply', {
        method: 'POST',
        json: {
          startUtc: preview.startUtc,
          horizonDays: preview.horizonDays,
          expectedTaskVersions: preview.taskVersions,
          expectedSlots: preview.scheduled,
        },
      });
      setApplied(result);
      setPreview(null);
    } catch (err) {
      setError(messageFor(err));
    } finally {
      setBusy(false);
    }
  }

  return <div className="ap-stack">
    <section className="ap-panel ap-stack">
      <h2 className="ap-h2">Generate a proposed schedule</h2>
      <p className="ap-foot" style={{ margin: 0 }}>Avzar uses shop hours, closure dates, worker availability, approved leave, daily workload capacity, estimates, dependencies and existing task reservations. Preview does not change task schedules.</p>
      <form className="ap-grid2" onSubmit={(event) => void generate(event)}>
        <div className="ap-field">
          <label htmlFor="schedule-start">Start date (UTC)</label>
          <input id="schedule-start" className="ap-input" type="date" value={startDate} required disabled={busy} onChange={(event) => setStartDate(event.target.value)} />
        </div>
        <div className="ap-field">
          <label htmlFor="schedule-horizon">Planning horizon</label>
          <select id="schedule-horizon" className="ap-input" value={horizonDays} disabled={busy} onChange={(event) => setHorizonDays(Number(event.target.value))}>
            <option value={7}>7 days</option><option value={14}>14 days</option><option value={30}>30 days</option><option value={60}>60 days</option><option value={90}>90 days</option><option value={180}>180 days</option><option value={366}>366 days</option>
          </select>
        </div>
        <div><button className="ap-btn" type="submit" disabled={busy || !startDate}>{busy ? 'Planning…' : 'Generate preview'}</button></div>
      </form>
      <p className="ap-foot" style={{ margin: 0 }}>Business time zone: <strong>{timeZoneId}</strong>. The start date is submitted at 00:00 UTC.</p>
      {error && <p className="ap-alert" role="alert">{error}{/entitlement|subscription|plan/i.test(error) ? ' This feature may require the shop scheduling plan.' : ''}</p>}
      {applied && <p className="ap-ok" role="status">Schedule applied to {applied.updatedTasks} tasks. {applied.notificationsQueued ? 'Assignment emails were queued; eligible 24-hour reminders will be delivered by the background dispatcher.' : 'No worker email notifications were queued. Check that assigned workers have valid email addresses and email delivery is configured.'}</p>}
    </section>

    {preview && <>
      <section className="ap-panel ap-stack" aria-live="polite">
        <div className="ap-row"><h2 className="ap-h2" style={{ margin: 0 }}>Proposed schedule</h2><span className="ap-badge">Preview only</span></div>
        <div className="ap-grid2">
          <div><div className="ap-small ap-dim">Scheduled tasks</div><strong>{preview.scheduled.length}</strong></div>
          <div><div className="ap-small ap-dim">Needs attention</div><strong>{preview.unscheduled.length}</strong></div>
        </div>
        {preview.scheduled.length === 0 ? <p style={{ margin: 0 }}>No tasks could be scheduled in this horizon. Review the reasons below or check shop hours, task estimates and assignments.</p> : <div className="ap-scroll"><table className="ap-table"><thead><tr><th>Task ID</th><th>Starts ({preview.timeZoneId})</th><th>Ends ({preview.timeZoneId})</th></tr></thead><tbody>{preview.scheduled.map((slot) => <tr key={slot.taskId}><td><code>{slot.taskId}</code></td><td>{displayTime(slot.startUtc, preview.timeZoneId)}</td><td>{displayTime(slot.endUtc, preview.timeZoneId)}</td></tr>)}</tbody></table></div>}
        <div className="ap-row">
          <p className="ap-foot" style={{ margin: 0 }}>{preview.unscheduled.length > 0 ? 'Resolve every unscheduled task before applying this plan.' : 'Applying saves the proposed times, creates task audit events, and queues deduplicated assignment emails plus future 24-hour reminders.'}</p>
          <button className="ap-btn" type="button" disabled={busy || preview.unscheduled.length > 0 || preview.scheduled.length === 0 || preview.scheduled.length !== Object.keys(preview.taskVersions).length} onClick={() => void applySchedule()}>{busy ? 'Applying…' : 'Apply schedule'}</button>
        </div>
      </section>
      <section className="ap-panel ap-stack">
        <h2 className="ap-h2">Tasks not scheduled</h2>
        {preview.unscheduled.length === 0 ? <p style={{ margin: 0 }}>Every candidate task received a proposed slot.</p> : <div className="ap-scroll"><table className="ap-table"><thead><tr><th>Task ID</th><th>Reason</th></tr></thead><tbody>{preview.unscheduled.map((task) => <tr key={task.taskId}><td><code>{task.taskId}</code></td><td>{task.reason}</td></tr>)}</tbody></table></div>}
      </section>
    </>}
  </div>;
}
