import { Tabs } from 'expo-router';
import { colors } from '@/components/ui';
export default function TabLayout() {
  return <Tabs screenOptions={{ headerStyle: { backgroundColor: colors.surface }, headerTintColor: colors.text, tabBarActiveTintColor: colors.primary, tabBarStyle: { backgroundColor: colors.surface, borderTopColor: colors.border } }}><Tabs.Screen name="index" options={{ title: 'Home', tabBarLabel: 'Home' }} /><Tabs.Screen name="customers" options={{ title: 'Customers', tabBarLabel: 'Customers' }} /><Tabs.Screen name="tasks" options={{ title: 'My tasks', tabBarLabel: 'Tasks' }} /><Tabs.Screen name="settings" options={{ title: 'Settings', tabBarLabel: 'Settings' }} /></Tabs>;
}
