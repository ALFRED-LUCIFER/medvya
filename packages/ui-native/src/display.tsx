import { theme } from '@medvya/tokens';
import type { ComponentProps, ReactNode } from 'react';
import { ActivityIndicator, I18nManager, Pressable, StyleSheet, View } from 'react-native';
import { useBrand } from './brand';
import { Icon, type IconName } from './controls';
import { Text } from './text';

export type Tone = 'danger' | 'warning' | 'success' | 'info' | 'ai' | 'neutral' | 'brand';
const { color: c, space, radius } = theme;
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
    <View accessible={false} style={[styles.avatar, { width: size, height: size, borderRadius: solid ? radius.lg : size / 2, backgroundColor: solid ? brand.solid : brand.tint }]}>
      <Text variant="label" tone={solid ? c.card : brand.solid} style={{ fontSize: size * 0.38 }}>{initials}</Text>
    </View>
  );
}

/** A tappable row: leading avatar or icon, two lines, trailing chips, and a chevron that flips in RTL. Chips are data so their words (allergy, critical) are always in the row's spoken label. */
export function ListRow({ title, subtitle, leading, chips = [], onPress }: { title: string; subtitle?: string; leading?: ReactNode; chips?: ComponentProps<typeof Chip>[]; onPress?: () => void }) {
  const { target } = useBrand();
  const content = (
    <>
      {leading}
      <View style={styles.rowText}>
        <Text variant="label">{title}</Text>
        {subtitle ? <Text variant="caption">{subtitle}</Text> : null}
      </View>
      {chips.length > 0 ? <View style={styles.rowChips}>{chips.map(chip => <Chip key={chip.label} {...chip} />)}</View> : null}
      {onPress ? <Icon name={I18nManager.isRTL ? 'chevron-left' : 'chevron-right'} color={c.ink400} /> : null}
    </>
  );
  return onPress
    ? <Pressable accessibilityRole="button" accessibilityLabel={[title, subtitle, ...chips.map(chip => chip.label)].filter(Boolean).join(', ')} onPress={onPress}
        style={({ pressed }) => [styles.row, { minHeight: target + space[12] }, pressed && styles.pressed]}>{content}</Pressable>
    : <View style={[styles.row, { minHeight: target + space[12] }]}>{content}</View>;
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
  state: { flexGrow: 1, alignItems: 'center', justifyContent: 'center', gap: space[12], padding: space[32] },
  center: { textAlign: 'center' },
  chip: { flexDirection: 'row', alignItems: 'center', gap: space[4], alignSelf: 'flex-start', paddingHorizontal: space[8], paddingVertical: space[4], borderRadius: radius.sm },
  chipText: { fontWeight: '600' },
  avatar: { alignItems: 'center', justifyContent: 'center' },
  row: { flexDirection: 'row', alignItems: 'center', gap: space[12], paddingVertical: space[8] },
  rowText: { flex: 1, gap: space[2] },
  rowChips: { alignItems: 'flex-end', gap: space[4] },
  pressed: { opacity: 0.7 },
  banner: { flexDirection: 'row', alignItems: 'flex-start', gap: space[8], padding: space[12], borderRadius: radius.lg },
  bannerText: { flex: 1 },
  stat: { flex: 1, padding: space[12], gap: space[2], borderRadius: radius.lg, borderWidth: 1, borderColor: theme.color.line, backgroundColor: theme.color.card },
  emblem: { alignItems: 'center', justifyContent: 'center', borderRadius: radius.xl, alignSelf: 'center' },
});
