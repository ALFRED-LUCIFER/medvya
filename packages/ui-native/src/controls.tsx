import MaterialIcons from '@expo/vector-icons/MaterialIcons';
import { theme } from '@medvya/tokens';
import type { ComponentProps } from 'react';
import { ActivityIndicator, Pressable, StyleSheet, TextInput, useWindowDimensions, View, type TextInputProps } from 'react-native';
import { useBrand } from './brand';
import { Text } from './text';

export type IconName = ComponentProps<typeof MaterialIcons>['name'];
/** Icon size follows the system font size so icons grow with their text, capped lower than text (2×) so rows and targets keep their shape. */
const ICON_SCALE_CAP = 1.5;
/** `fixed` keeps the given size, for bars of fixed height such as the tab bar (iOS keeps tab icons fixed too). */
export function Icon({ name, size = 22, color = theme.color.ink600, fixed }: { name: IconName; size?: number; color?: string; fixed?: boolean }) {
  const { fontScale } = useWindowDimensions();
  const scaled = fixed ? size : Math.round(size * Math.min(Math.max(fontScale, 1), ICON_SCALE_CAP));
  // Hidden on both platforms: accessible={false} alone still lets Android merge the glyph into a parent label (audit M-02).
  return <MaterialIcons name={name} size={scaled} color={color} accessible={false} importantForAccessibility="no" accessibilityElementsHidden />;
}

type ButtonProps = { label: string; accessibilityLabel?: string; onPress?: () => void; variant?: 'primary' | 'secondary' | 'ghost' | 'inverse'; icon?: IconName; disabled?: boolean; busy?: boolean; hint?: string };

/** One button for both apps. Disabled buttons stay readable and say why through `hint` (read by screen readers). */
export function Button({ label, accessibilityLabel, onPress, variant = 'primary', icon, disabled, busy, hint }: ButtonProps) {
  const { solid, target } = useBrand();
  const fg = { primary: theme.color.card, secondary: theme.color.ink900, ghost: solid, inverse: solid }[variant];
  const look = {
    primary: { backgroundColor: solid },
    secondary: { backgroundColor: theme.color.card, borderWidth: 1, borderColor: theme.color.input },
    ghost: { backgroundColor: 'transparent' },
    inverse: { backgroundColor: theme.color.card },
  }[variant];
  const off = disabled || busy;
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={accessibilityLabel ?? label}
      accessibilityHint={hint}
      accessibilityState={{ disabled: !!off, busy: !!busy }}
      disabled={off}
      onPress={onPress}
      style={({ pressed }) => [styles.button, { minHeight: Math.max(target, 52) }, look, off && styles.off, pressed && styles.pressed]}
    >
      {busy ? <ActivityIndicator color={fg} /> : icon ? <Icon name={icon} size={20} color={fg} /> : null}
      <Text variant="label" tone={fg}>{label}</Text>
    </Pressable>
  );
}

export function IconButton({ icon, label, onPress, badge, disabled }: { icon: IconName; label: string; onPress?: () => void; badge?: number; disabled?: boolean }) {
  const { target } = useBrand();
  return (
    <Pressable accessibilityRole="button" accessibilityLabel={label} accessibilityState={{ disabled: !!disabled }} disabled={disabled} onPress={onPress} hitSlop={4}
      style={({ pressed }) => [styles.iconButton, { minWidth: target, minHeight: target }, disabled && styles.off, pressed && styles.pressed]}>
      <Icon name={icon} color={theme.color.ink900} />
      {badge ? <View style={styles.badge}><Text variant="overline" tone={theme.color.card}>{String(badge)}</Text></View> : null}
    </Pressable>
  );
}

type FieldProps = TextInputProps & { label: string; error?: string; hint?: string };

/** A labelled input. Errors show as text with an icon, never colour alone, and are announced with the field. */
export function TextField({ label, error, hint, style, ...input }: FieldProps) {
  const { solid, target, body } = useBrand();
  return (
    <View style={styles.field}>
      <Text variant="label">{label}</Text>
      <TextInput
        accessibilityLabel={label}
        accessibilityHint={error ?? hint}
        placeholderTextColor={theme.color.ink600}
        selectionColor={solid}
        style={[styles.input, { minHeight: Math.max(target, 52), fontSize: body + 1 }, error ? { borderColor: theme.color.danger700 } : null, style]}
        {...input}
      />
      {error
        ? <View style={styles.errorRow} accessibilityLiveRegion="polite"><Icon name="error-outline" size={18} color={theme.color.danger700} /><Text variant="caption" tone={theme.color.danger700}>{error}</Text></View>
        : hint ? <Text variant="caption">{hint}</Text> : null}
    </View>
  );
}

/** Segmented tabs inside a screen (Records: Visits · Results · Medicines). */
export function Segmented<K extends string>({ options, value, onChange }: { options: { key: K; label: string }[]; value: K; onChange: (k: K) => void }) {
  const { solid, target } = useBrand();
  return (
    <View style={styles.segmented} accessibilityRole="tablist">
      {options.map(o => {
        const on = o.key === value;
        return (
          <Pressable key={o.key} accessibilityRole="tab" accessibilityState={{ selected: on }} onPress={() => onChange(o.key)}
            style={[styles.segment, { minHeight: target - 4 }, on && styles.segmentOn]}>
            <Text variant="label" tone={on ? solid : theme.color.ink600}>{o.label}</Text>
          </Pressable>
        );
      })}
    </View>
  );
}

const styles = StyleSheet.create({
  button: { flexDirection: 'row', alignItems: 'center', justifyContent: 'center', gap: 8, borderRadius: 14, paddingHorizontal: 20 },
  off: { opacity: 0.45 },
  pressed: { opacity: 0.8 },
  iconButton: { alignItems: 'center', justifyContent: 'center', borderRadius: 12, borderWidth: 1, borderColor: theme.color.input, backgroundColor: theme.color.card },
  badge: { position: 'absolute', top: -4, end: -4, minWidth: 20, height: 20, borderRadius: 10, paddingHorizontal: 5, alignItems: 'center', justifyContent: 'center', backgroundColor: theme.color.danger700 },
  field: { gap: 6 },
  input: { borderWidth: 1, borderColor: theme.color.input, borderRadius: 12, paddingHorizontal: 14, color: theme.color.ink900, backgroundColor: theme.color.card },
  errorRow: { flexDirection: 'row', alignItems: 'center', gap: 6 },
  segmented: { flexDirection: 'row', padding: 4, gap: 4, borderRadius: 14, backgroundColor: theme.color.muted },
  segment: { flex: 1, alignItems: 'center', justifyContent: 'center', borderRadius: 10 },
  segmentOn: { backgroundColor: theme.color.card, shadowColor: theme.color.ink900, shadowOpacity: 0.08, shadowRadius: 4, shadowOffset: { width: 0, height: 1 }, elevation: 1 },
});
