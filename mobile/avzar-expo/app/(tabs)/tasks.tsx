import { useQuery } from '@tanstack/react-query';
import { FlatList, Text } from 'react-native';
import { api } from '@/api/client';
import { Card, Heading, Loading, Message, Screen, colors } from '@/components/ui';

type Task = { id: string; title: string; stageName?: string; status: string; deadlineUtc?: string | null; instructions?: string | null };

export default function TasksScreen() {
  const tasks = useQuery({ queryKey: ['my-tasks'], queryFn: () => api.myTasks() });
  return <Screen><Heading subtitle="Tasks assigned to you by the workshop manager.">My tasks</Heading>{tasks.isLoading ? <Loading /> : tasks.isError ? <Message tone="danger">{tasks.error instanceof Error ? tasks.error.message : 'Could not load tasks.'}</Message> : <FlatList data={(tasks.data ?? []) as Task[]} keyExtractor={(task) => task.id} contentContainerStyle={{ gap: 10, paddingBottom: 24 }} ListEmptyComponent={<Message>No assigned tasks were returned by the API.</Message>} renderItem={({ item }) => <Card><Text style={{ color: colors.text, fontWeight: '800', fontSize: 16 }}>{item.title}</Text><Text style={{ color: colors.muted }}>{item.stageName ?? 'Production'} · {item.status}</Text>{item.deadlineUtc ? <Text style={{ color: colors.muted, fontSize: 12 }}>Due {new Date(item.deadlineUtc).toLocaleString()}</Text> : null}{item.instructions ? <Text style={{ color: colors.muted }}>{item.instructions}</Text> : null}</Card>} />}</Screen>;
}
