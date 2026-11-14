import { theme } from '@medvya/tokens';
import { Button, IconButton, TextField } from '@medvya/ui-native';
import { screen } from '@testing-library/react-native';
import { StyleSheet } from 'react-native';
import { renderApp } from './render';

// WCAG relative luminance, the same formula as the web contrast test (packages/ui/src/primitives.test.tsx).
function contrast(a: string, b: string) {
  const lum = (hex: string) => {
    const [r, g, bl] = [1, 3, 5].map(i => parseInt(hex.slice(i, i + 2), 16) / 255)
      .map(c => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4));
    return 0.2126 * r! + 0.7152 * g! + 0.0722 * bl!;
  };
  const [hi, lo] = [lum(a), lum(b)].sort((x, y) => y - x);
  return (hi! + 0.05) / (lo! + 0.05);
}

// Controls sit on the screen (surface) and fill with card; the boundary must show against both.
const surfaces = [theme.color.card, theme.color.surface];

it('draws ui-native control borders at 3:1 and placeholders at 4.5:1 (WCAG 1.4.11, 1.4.3)', async () => {
  await renderApp(<>
    <Button label="Secondary" variant="secondary" />
    <IconButton icon="settings" label="Settings" />
    <TextField label="Work email" placeholder="name@clinic.example" />
  </>);
  const border = (name: string) => StyleSheet.flatten(screen.getByRole('button', { name }).props.style).borderColor as string;
  for (const name of ['Secondary', 'Settings']) for (const bg of surfaces)
    expect(contrast(border(name), bg)).toBeGreaterThanOrEqual(3);
  const input = screen.getByLabelText('Work email');
  for (const bg of surfaces) {
    expect(contrast(StyleSheet.flatten(input.props.style).borderColor as string, bg)).toBeGreaterThanOrEqual(3);
    expect(contrast(input.props.placeholderTextColor as string, bg)).toBeGreaterThanOrEqual(4.5);
  }
});
