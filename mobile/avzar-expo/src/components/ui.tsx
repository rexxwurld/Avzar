import React from 'react';
import { ActivityIndicator, Pressable, StyleSheet, Text, TextInput, View, type TextInputProps, type ViewProps } from 'react-native';

export const colors = {
  background: '#F7F7FB', surface: '#FFFFFF', text: '#18181B', muted: '#71717A',
  primary: '#6D28D9', primarySoft: '#EDE9FE', border: '#E4E4E7', danger: '#B91C1C', success: '#166534',
};

export function Screen({ children, ...props }: ViewProps) {
  return <View {...props} style={[styles.screen, props.style]}>{children}</View>;
}

export function Heading({ children, subtitle }: { children: React.ReactNode; subtitle?: string }) {
  return <View style={styles.heading}><Text style={styles.title}>{children}</Text>{subtitle ? <Text style={styles.subtitle}>{subtitle}</Text> : null}</View>;
}

export function Button({ title, onPress, disabled, variant = 'primary' }: { title: string; onPress: () => void; disabled?: boolean; variant?: 'primary' | 'secondary' }) {
  return <Pressable accessibilityRole="button" onPress={onPress} disabled={disabled} style={({ pressed }) => [styles.button, variant === 'secondary' && styles.buttonSecondary, disabled && styles.disabled, pressed && styles.pressed]}><Text style={[styles.buttonText, variant === 'secondary' && styles.buttonSecondaryText]}>{title}</Text></Pressable>;
}

export function Field({ label, ...props }: TextInputProps & { label: string }) {
  return <View style={styles.field}><Text style={styles.label}>{label}</Text><TextInput placeholderTextColor="#A1A1AA" {...props} style={[styles.input, props.style]} /></View>;
}

export function Card({ children, ...props }: ViewProps) {
  return <View {...props} style={[styles.card, props.style]}>{children}</View>;
}

export function Loading() {
  return <View style={styles.loading}><ActivityIndicator size="large" color={colors.primary} /></View>;
}

export function Message({ children, tone = 'muted' }: { children: React.ReactNode; tone?: 'muted' | 'danger' | 'success' }) {
  return <Text style={[styles.message, tone === 'danger' && { color: colors.danger }, tone === 'success' && { color: colors.success }]}>{children}</Text>;
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: colors.background, padding: 20, gap: 16 },
  heading: { gap: 5, marginBottom: 4 }, title: { fontSize: 27, lineHeight: 33, fontWeight: '800', color: colors.text },
  subtitle: { color: colors.muted, fontSize: 14, lineHeight: 20 },
  button: { minHeight: 48, borderRadius: 13, backgroundColor: colors.primary, paddingHorizontal: 16, alignItems: 'center', justifyContent: 'center' },
  buttonText: { color: '#FFFFFF', fontSize: 15, fontWeight: '700' }, buttonSecondary: { backgroundColor: colors.primarySoft }, buttonSecondaryText: { color: colors.primary },
  disabled: { opacity: 0.55 }, pressed: { opacity: 0.85 },
  field: { gap: 7 }, label: { color: colors.text, fontSize: 13, fontWeight: '700' },
  input: { minHeight: 48, borderWidth: 1, borderColor: colors.border, borderRadius: 12, paddingHorizontal: 13, paddingVertical: 11, backgroundColor: colors.surface, color: colors.text, fontSize: 15 },
  card: { borderWidth: 1, borderColor: colors.border, borderRadius: 16, padding: 15, backgroundColor: colors.surface, gap: 8 },
  loading: { flex: 1, alignItems: 'center', justifyContent: 'center' }, message: { color: colors.muted, fontSize: 13, lineHeight: 19 },
});
