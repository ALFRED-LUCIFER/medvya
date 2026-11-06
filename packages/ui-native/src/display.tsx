import { theme } from '@medvya/tokens';
import type { ReactNode } from 'react';
import { ActivityIndicator, I18nManager, Pressable, StyleSheet, View } from 'react-native';
import { useBrand } from './brand';
import { Icon, type IconName } from './controls';
import { Text } from './text';

export type Tone = 'danger' | 'warning' | 'success' | 'info' | 'ai' | 'neutral' | 'brand';
const c = theme.color;
const tones: Record<Exclude<Tone, 'brand'>, { fg: string; bg: string }> = {
  danger: { fg: c.danger700, bg: c.danger50 },
  warning: { fg: c.warning700, bg: c.warning50 },
  success: { fg: c.success700, bg: c.success50 },
  info: { fg: c.action600, bg: c.accent },
  ai: { fg: c.ai700, bg: c.ai50 },
  neutral: { fg: c.ink600, bg: c.muted },
};
function useTone(tone: Tone) {
  const { solid, tint } = useBrand();
  return tone === 'brand' ? { fg: solid, bg: tint } : tones[tone];
}

/** A status chip. Always text (plus an icon for clinical flags): status is never shown by colour alone. */
export function Chip({ label, tone = 'neutral', icon }: { label: string; tone?: Tone; icon?: IconName }) {
  const { fg, bg } = useTone(tone);
  return (
    <View style={[styles.chip, { backgroundColor: bg }]}>
      {icon ? <Icon name={icon} size={15} color={fg} /> : null}
      <Text variant="caption" tone={fg} style={styles.chipText}>{label}</Text>
    </View>
  );
}

export function Avatar({ initials, solid, size = 40 }: { initials: string; solid?: boolean; size?: number }) {
  const brand = useBrand();
  return (
    <View accessible={false} style={[styles.avatar, { width: size, height: size, borderRadius: solid ? 12 : size / 2, backgroundColor: solid ? brand.solid : brand.tint }]}>
      <Text variant="label" tone={solid ? c.card : brand.solid} style={{ fontSize: size * 0.38 }}>{initials}</Text>
    </View>
  );
}

/** A tappable row: leading avatar or icon, two lines, a trailing chip or value, and a chevron that flips in RTL. */
export function ListRow({ title, subtitle, leading, trailing, onPress, label }: { title: string; subtitle?: string; leading?: ReactNode; trailing?: ReactNode; onPress?: () => void; label?: string }) {
  const { target } = useBrand();
  const content = (
    <>
      {leading}
      <View style={styles.rowText}>
        <Text variant="label">{title}</Text>
        {subtitle ? <Text variant="caption">{subtitle}</Text> : null}
      </View>
      {trailing}
      {onPress ? <Icon name={I18nManager.isRTL ? 'chevron-left' : 'chevron-right'} color={c.ink400} /> : null}
    </>
  );
  return onPress
    ? <Pressable accessibilityRole="button" accessibilityLabel={label ?? [title, subtitle].filter(Boolean).join(', ')} onPress={onPress}
        style={({ pressed }) => [styles.row, { minHeight: target + 12 }, pressed && styles.pressed]}>{content}</Pressable>
    : <View style={[styles.row, { minHeight: target + 12 }]}>{content}</View>;
}

export function Banner({ text, tone = 'info', icon = 'info-outline' }: { text: string; tone?: Tone; icon?: IconName }) {
  const { fg, bg } = useTone(tone);
  return (
    <View style={[styles.banner, { backgroundColor: bg }]} accessibilityRole="summary">
      <Icon name={icon} size={20} color={fg} />
      <Text variant="caption" tone={fg} style={styles.bannerText}>{text}</Text>
    </View>
  );
}

export function Stat({ label, value }: { label: string; value: string }) {
  return (
    <View style={styles.stat} accessible accessibilityLabel={`${label}: ${value}`}>
      <Text variant="caption">{label}</Text>
      <Text variant="title">{value}</Text>
    </View>
  );
}

/** An icon in a soft square, for empty states, onboarding and the unlock prompt. */
export function Emblem({ icon, size = 72 }: { icon: IconName; size?: number }) {
  const { solid, tint } = useBrand();
  return <View style={[styles.emblem, { width: size, height: size, backgroundColor: tint }]}><Icon name={icon} size={size * 0.5} color={solid} /></View>;
}

/** Loading, empty, error and not-connected states: an emblem, a title, one line and an optional action. */
export function StateMessage({ icon, title, body, action }: { icon: IconName; title: string; body?: string; action?: ReactNode }) {
  return (
    <View style={styles.state} accessibilityLiveRegion="polite">
      <Emblem icon={icon} size={64} />
      <Text variant="heading" style={styles.center}>{title}</Text>
      {body ? <Text variant="caption" style={styles.center}>{body}</Text> : null}
      {action}
    </View>
  );
}

export function Loading({ label }: { label: string }) {
  const { solid } = useBrand();
  return <View style={styles.state}><ActivityIndicator size="large" color={solid} accessibilityLabel={label} /></View>;
}

const styles = StyleSheet.create({
  state: { flexGrow: 1, alignItems: 'center', justifyContent: 'center', gap: 10, padding: 32 },
  center: { textAlign: 'center' },
  chip: { flexDirection: 'row', alignItems: 'center', gap: 4, alignSelf: 'flex-start', paddingHorizontal: 8, paddingVertical: 3, borderRadius: 8 },
  chipText: { fontWeight: '600' },
  avatar: { alignItems: 'center', justifyContent: 'center' },
  row: { flexDirection: 'row', alignItems: 'center', gap: 12, paddingVertical: 8 },
  rowText: { flex: 1, gap: 2 },
  pressed: { opacity: 0.7 },
  banner: { flexDirection: 'row', alignItems: 'flex-start', gap: 10, padding: 12, borderRadius: 12 },
  bannerText: { flex: 1 },
  stat: { flex: 1, padding: 12, gap: 2, borderRadius: 14, borderWidth: 1, borderColor: theme.color.line, backgroundColor: theme.color.card },
  emblem: { alignItems: 'center', justifyContent: 'center', borderRadius: 22, alignSelf: 'center' },
});
