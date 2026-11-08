import { theme } from '@medvya/tokens';
import { StyleSheet, Text as RNText, type TextProps } from 'react-native';
import { useBrand } from './brand';

export type TextVariant = 'display' | 'title' | 'heading' | 'body' | 'label' | 'caption' | 'overline';

/** The type scale. Sizes are a base; system font scaling (Dynamic Type, Android font size) still applies up to 200 %. */
export function Text({ variant = 'body', tone, style, ...rest }: TextProps & { variant?: TextVariant; tone?: string }) {
  const { body, solid } = useBrand();
  const size = { display: 30, title: 24, heading: body + 2, body, label: body, caption: body - 2, overline: 12 }[variant];
  return (
    <RNText
      maxFontSizeMultiplier={2}
      style={[styles[variant], { fontSize: size, lineHeight: Math.round(size * 1.35) }, variant === 'overline' && { color: solid }, tone ? { color: tone } : null, style]}
      {...rest}
    />
  );
}

const styles = StyleSheet.create({
  display: { fontWeight: '700', color: theme.color.ink900, letterSpacing: -0.4 },
  title: { fontWeight: '700', color: theme.color.ink900, letterSpacing: -0.2 },
  heading: { fontWeight: '700', color: theme.color.ink900 },
  body: { color: theme.color.ink700 },
  label: { fontWeight: '600', color: theme.color.ink900 },
  caption: { color: theme.color.ink600 },
  overline: { fontWeight: '700', letterSpacing: 0.8, textTransform: 'uppercase' },
});
