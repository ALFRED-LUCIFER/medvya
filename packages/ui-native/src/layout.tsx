import { theme } from '@medvya/tokens';
import type { ReactNode } from 'react';
import { KeyboardAvoidingView, RefreshControl, ScrollView, StyleSheet, View, type ViewProps } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { Avatar } from './display';
import { IconButton, type IconName } from './controls';
import { Text } from './text';

/** A full screen: safe area at the top, scrolling content, optional pinned footer for the primary action (thumb reach) that rides above the keyboard. */
export function Screen({ header, footer, children, scroll = true, padded = true, refresh }: { header?: ReactNode; footer?: ReactNode; children: ReactNode; scroll?: boolean; padded?: boolean; refresh?: { refreshing: boolean; onRefresh: () => void } }) {
  const body = padded ? styles.padded : undefined;
  return (
    <SafeAreaView edges={['top']} style={styles.screen}>
      {header}
      {/* Screens sit at the window top (no native header), so the default offset of 0 is right. */}
      <KeyboardAvoidingView behavior="padding" style={styles.screen}>
        {scroll
          ? <ScrollView contentContainerStyle={[body, styles.grow]} keyboardShouldPersistTaps="handled"
              refreshControl={refresh ? <RefreshControl refreshing={refresh.refreshing} onRefresh={refresh.onRefresh} /> : undefined}>{children}</ScrollView>
          : <View style={[body, styles.grow]}>{children}</View>}
        {footer ? <View style={styles.footer}>{footer}</View> : null}
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}

/** The top bar from the design: monogram, title, context line, one action. */
export function AppHeader({ title, subtitle, initials, action }: { title: string; subtitle?: string; initials: string; action?: { icon: IconName; label: string; onPress: () => void } }) {
  return (
    <View style={styles.header}>
      <Avatar initials={initials} solid />
      <View style={styles.headerText}>
        <Text variant="heading" accessibilityRole="header" numberOfLines={1}>{title}</Text>
        {subtitle ? <Text variant="caption" numberOfLines={1}>{subtitle}</Text> : null}
      </View>
      {action ? <IconButton icon={action.icon} label={action.label} onPress={action.onPress} /> : null}
    </View>
  );
}

export function Card({ style, accent, ...rest }: ViewProps & { accent?: string }) {
  return <View style={[styles.card, accent ? { borderColor: accent, borderWidth: 1.5 } : null, style]} {...rest} />;
}

export function Section({ title, aside, children }: { title: string; aside?: string; children: ReactNode }) {
  return (
    <View style={styles.section}>
      <View style={styles.sectionHead}>
        <Text variant="overline" tone={theme.color.ink600} accessibilityRole="header">{title}</Text>
        {aside ? <Text variant="caption">{aside}</Text> : null}
      </View>
      {children}
    </View>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: theme.color.surface },
  padded: { padding: 20, gap: 16 },
  grow: { flexGrow: 1 },
  footer: { paddingHorizontal: 20, paddingTop: 12, paddingBottom: 20, gap: 8, backgroundColor: theme.color.surface },
  header: { flexDirection: 'row', alignItems: 'center', gap: 12, paddingHorizontal: 20, paddingVertical: 12, backgroundColor: theme.color.card, borderBottomWidth: StyleSheet.hairlineWidth, borderBottomColor: theme.color.line },
  headerText: { flex: 1 },
  card: { backgroundColor: theme.color.card, borderRadius: 16, borderWidth: 1, borderColor: theme.color.line, padding: 16, gap: 10 },
  section: { gap: 10 },
  sectionHead: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'baseline' },
});
