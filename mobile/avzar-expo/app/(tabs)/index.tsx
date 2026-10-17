import { useRouter } from 'expo-router';
import { Pressable, ScrollView, Text, View } from 'react-native';
import { useSession } from '@/auth/SessionProvider';
import { Card, Heading, colors } from '@/components/ui';

export default function DashboardScreen() {
  const { user } = useSession();
  const router = useRouter();
  return (
    <ScrollView style={{ flex: 1, backgroundColor: colors.background }} contentContainerStyle={{ padding: 20, gap: 18 }}>
      <Heading subtitle={user?.businessName ?? 'Your tailoring workspace'}>Good day{user?.fullName ? `, ${user.fullName.split(' ')[0]}` : ''}.</Heading>
      <Card>
        <Text style={{ color: colors.muted, fontSize: 13 }}>WORKSPACE</Text>
        <Text style={{ color: colors.text, fontSize: 20, fontWeight: '800' }}>{user?.businessName ?? 'Avzar'}</Text>
        <Text style={{ color: colors.muted }}>Your business records load from the authenticated API.</Text>
      </Card>
      <View style={{ flexDirection: 'row', gap: 12 }}>
        <Pressable style={{ flex: 1 }} onPress={() => router.push('/(tabs)/customers')}>
          <Card>
            <Text style={{ fontSize: 25 }}>♙</Text>
            <Text style={{ fontWeight: '800', color: colors.text }}>Customers</Text>
            <Text style={{ color: colors.muted, fontSize: 12 }}>Profiles & measurements</Text>
          </Card>
        </Pressable>
        <Pressable style={{ flex: 1 }} onPress={() => router.push('/(tabs)/tasks')}>
          <Card>
            <Text style={{ fontSize: 25 }}>✓</Text>
            <Text style={{ fontWeight: '800', color: colors.text }}>Daily tasks</Text>
            <Text style={{ color: colors.muted, fontSize: 12 }}>Assigned workshop jobs</Text>
          </Card>
        </Pressable>
      </View>
      <Card>
        <Text style={{ color: colors.text, fontWeight: '800' }}>Next up</Text>
        <Text style={{ color: colors.muted, lineHeight: 20 }}>This first mobile pass connects sign-in and customer/measurement reads to the existing API. Orders, invoices, scheduling, and offline database synchronization are later implementation passes—not claimed complete here.</Text>
      </Card>
    </ScrollView>
  );
}
