import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { render } from '../generate.mjs';
import { theme } from './theme';

const read = (f: string) => readFileSync(new URL(f, import.meta.url), 'utf8');
const source = JSON.parse(read('tokens.json'));
const camel = (s: string) => s.replace(/-(\w)/g, (_, c: string) => c.toUpperCase());

describe('design tokens', () => {
  it('committed CSS and theme are what the generator renders from tokens.json', () => {
    const { css, theme: ts } = render(source);
    expect(read('tokens.css')).toBe(css);
    expect(read('theme.ts')).toBe(ts);
  });

  it('every CSS colour has the same value in the native theme', () => {
    const cssColors = [...read('tokens.css').matchAll(/--color-([\w-]+):\s*(#[0-9a-fA-F]{6})/g)];
    expect(cssColors.length).toBe(Object.keys(source.color).length + Object.keys(source.tone).length * 3);
    for (const [, name, hex] of cssColors) expect((theme.color as Record<string, string>)[camel(name!)]).toBe(hex);
  });

  it('gives each of the seven tones a text, background and border colour from the palette, and a written meaning', () => {
    expect(Object.keys(theme.tone).sort()).toEqual(['ai', 'brand', 'danger', 'info', 'neutral', 'success', 'warning']);
    for (const [name, roles] of Object.entries(source.tone as Record<string, Record<string, string>>)) {
      expect(Object.keys(roles), name).toEqual(['text', 'bg', 'border']);
      for (const [role, ref] of Object.entries(roles)) expect((theme.tone as Record<string, Record<string, string>>)[name]![role]).toBe(source.color[ref]);
      expect(source.notes[`tone-${name}`], name).toBeTruthy();
    }
    expect(() => render({ ...source, tone: { danger: { text: 'no-such-colour', bg: 'danger-50', border: 'danger-500' } } })).toThrow(/no-such-colour/);
  });

  it('body-large is 16 px on a 24 px line, so a phone browser does not zoom on focus', () => {
    expect(read('tokens.css')).toContain('--text-body-large: 16px;');
    expect(read('tokens.css')).toContain('--text-body-large--line-height: 24px;');
    expect(theme.text.bodyLarge).toEqual({ fontSize: 16, lineHeight: 24, fontWeight: '500' });
  });

  it('names the layout, density, breakpoint and motion values as Tailwind theme variables and native numbers', () => {
    const css = read('tokens.css');
    for (const v of ['--breakpoint-wide: 1024px', '--container-page: 1600px', '--spacing-control: 44px', '--spacing-row-compact: 36px',
      '--ease-out: cubic-bezier(0.23, 1, 0.32, 1)', '--transition-duration-fast: 150ms']) expect(css).toContain(`${v};`);
    expect(theme.transitionDuration.fast).toBe(150);
    expect(theme.spacing.control).toBe(44);
  });

  it('keeps the public site parser working: one @theme block with no closing brace inside', () => {
    expect(read('tokens.css')).toMatch(/@theme static \{[^}]*\}/);
  });
});
