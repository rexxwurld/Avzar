import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useCallback, useEffect, useState } from 'react';
import { AppState, FlatList, Text, View } from 'react-native';
import { api } from '@/api/client';
import { Button, Card, Field, Heading, Loading, Message, Screen, colors } from '@/components/ui';
import { useTaskEvents, type LiveState } from '@/realtime/events';
import { listPending, performTaskAction, syncQueue, type TaskActionName } from '@/offline/queue';

type Task = { id: string; title: string; stageName?: string; status: string; deadlineUtc?: string | null; instructions?: string | null };
type Info = { tone: 'muted' | 'danger' | 'success'; text: string };

const LIVE_LABEL: Record<LiveState, string> = {
  connecting: 'Live updates: connecting…',
  live: 'Live updates: on',
  reconnecting: 'Live updates: reconnecting…',
  offline: 'Live updates: off (use refresh)',
};

const ACTIONS: Array<{ label: string; action: TaskActionName }> = [
  { label: 'Start', action: 'start' },
  { label: 'Pause', action: 'pause' },
  { label: 'Resume', action: 'resume' },
  { label: 'Complete', action: 'complete' },
];

export default function TasksScreen() {
  const queryClient = useQueryClient();
  const tasks = useQuery({ queryKey: ['my-tasks'], queryFn: () => api.myTasks() });
  const pending = useQuery({ queryKey: ['pending-actions'], queryFn: listPending });
  const [note, setNote] = useState('');
  const [busy, setBusy] = useState(false);
  const [info, setInfo] = useState<Info | null>(null);
  const live = useTaskEvents(useCallback(() => {
    void queryClient.invalidateQueries({ queryKey: ['my-tasks'] });
  }, [queryClient]));

  const refreshAll = useCallback(async () => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ['my-tasks'] }),
      queryClient.invalidateQueries({ queryKey: ['pending-actions'] }),
    ]);
  }, [queryClient]);

  const sync = useCallback(async () => {
    setBusy(true);
    try {
      const result = await syncQueue();
      if (result.rejected.length > 0) setInfo({ tone: 'danger', text: `Some saved actions were refused by the server: ${result.rejected.join('; ')}` });
      else if (result.sent > 0) setInfo({ tone: 'success', text: `Sent ${result.sent} saved action${result.sent === 1 ? '' : 's'}.` });
      await refreshAll();
    } catch {
      // Nothing to do: the actions stay saved and the next sync tries again.
    } finally {
      setBusy(false);
    }
  }, [refreshAll]);

  // Send anything saved when the screen opens and every time the app returns to the foreground.
  useEffect(() => {
    void sync();
    const subscription = AppState.addEventListener('change', (state) => {
      if (state === 'active') void sync();
    });
    return () => subscription.remove();
  }, [sync]);

  const act = async (taskId: string, action: TaskActionName) => {
    setBusy(true);
    setInfo(null);
    try {
      const outcome = await performTaskAction(taskId, action, note.trim() || undefined);
      if (action === 'pause' || action === 'complete') setNote('');
      setInfo(outcome === 'sent'
        ? { tone: 'success', text: 'Done.' }
        : { tone: 'muted', text: 'No connection. Saved on this phone and it will be sent automatically.' });
      await refreshAll();
    } catch (error) {
      setInfo({ tone: 'danger', text: error instanceof Error ? error.message : 'Could not complete that action.' });
    } finally {
      setBusy(false);
    }
  };

  const queued = pending.data ?? [];
  const queuedFor = (taskId: string) => queued.filter((item) => item.taskId === taskId).map((item) => item.action);

  return (
    <Screen>
      <Heading subtitle="Tasks assigned to you by the workshop manager.">My tasks</Heading>
      {queued.length > 0 ? <Message>{queued.length} action{queued.length === 1 ? '' : 's'} waiting to be sent.</Message> : null}
      <Message>{LIVE_LABEL[live]}</Message>
      {info ? <Message tone={info.tone}>{info.text}</Message> : null}
      <Field label="Note (used when you pause or complete)" value={note} onChangeText={setNote} placeholder="Optional" />
      <Button title={busy ? 'Working…' : 'Refresh / send saved actions'} variant="secondary" disabled={busy} onPress={() => void sync()} />
      {tasks.isLoading ? <Loading /> : tasks.isError ? <Message tone="danger">{tasks.error instanceof Error ? tasks.error.message : 'Could not load tasks.'}</Message> : (
        <FlatList
          data={(tasks.data ?? []) as Task[]}
          keyExtractor={(task) => task.id}
          contentContainerStyle={{ gap: 10, paddingBottom: 24 }}
          ListEmptyComponent={<Message>No assigned tasks were returned by the API.</Message>}
          renderItem={({ item }) => {
            const waiting = queuedFor(item.id);
            return (
              <Card>
                <Text style={{ color: colors.text, fontWeight: '800', fontSize: 16 }}>{item.title}</Text>
                <Text style={{ color: colors.muted }}>{item.stageName ?? 'Production'} · {item.status}</Text>
                {item.deadlineUtc ? <Text style={{ color: colors.muted, fontSize: 12 }}>Due {new Date(item.deadlineUtc).toLocaleString()}</Text> : null}
                {item.instructions ? <Text style={{ color: colors.muted }}>{item.instructions}</Text> : null}
                {waiting.length > 0 ? <Text style={{ color: colors.primary, fontSize: 12 }}>Waiting to send: {waiting.join(', ')}</Text> : null}
                <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: 8 }}>
                  {ACTIONS.map(({ label, action }) => (
                    <Button key={action} title={label} variant="secondary" disabled={busy} onPress={() => void act(item.id, action)} />
                  ))}
                </View>
              </Card>
            );
          }}
        />
      )}
    </Screen>
  );
}
