import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useFocusEffect, useRouter } from 'expo-router';
import { useCallback, useState } from 'react';
import { FlatList, Pressable, Text, View } from 'react-native';
import { api } from '@/api/client';
import { Button, Card, Field, Heading, Loading, Message, Screen, colors } from '@/components/ui';

export default function CustomersScreen() {
  const [search, setSearch] = useState('');
  const router = useRouter();
  const queryClient = useQueryClient();
  useFocusEffect(useCallback(() => {
    void queryClient.invalidateQueries({ queryKey: ['customers'] });
  }, [queryClient]));
  const customers = useQuery({ queryKey: ['customers', search], queryFn: () => api.customers(search) });

  return (
    <Screen>
      <Heading subtitle="Customer profiles, garment preferences and measurement records.">Customers</Heading>
      <Field label="Search customers" value={search} onChangeText={setSearch} placeholder="Name, email or phone" returnKeyType="search" />
      <Button title="+  Add customer" onPress={() => router.push('/customers/new')} />
      {customers.isLoading ? <Loading /> : customers.isError ? (
        <Message tone="danger">{customers.error instanceof Error ? customers.error.message : 'Could not load customers.'}</Message>
      ) : (
        <FlatList
          data={customers.data ?? []}
          keyExtractor={(item) => item.id}
          contentContainerStyle={{ gap: 10, paddingBottom: 28 }}
          ListEmptyComponent={<Message>No customers found. Try another search or add your first customer.</Message>}
          renderItem={({ item }) => (
            <Pressable onPress={() => router.push({ pathname: '/customers/[id]', params: { id: item.id } })}>
              <Card>
                <Text style={{ color: colors.text, fontWeight: '800', fontSize: 16 }}>{item.fullName}</Text>
                <Text style={{ color: colors.muted }}>{item.phone || item.email || 'No contact details'}</Text>
                <View style={{ flexDirection: 'row', justifyContent: 'space-between' }}>
                  <Text style={{ color: colors.muted, fontSize: 12 }}>{item.orderCount} order(s)</Text>
                  <Text style={{ color: colors.primary, fontWeight: '700', fontSize: 12 }}>View profile →</Text>
                </View>
              </Card>
            </Pressable>
          )}
        />
      )}
    </Screen>
  );
}
