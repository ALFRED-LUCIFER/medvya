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
    expect(cssColors.length).toBe(Object.keys(source.color).length);
    for (const [, name, hex] of cssColors) expect((theme.color as Record<string, string>)[camel(name!)]).toBe(hex);
  });

  it('keeps the public site parser working: one @theme block with no closing brace inside', () => {
    expect(read('tokens.css')).toMatch(/@theme static \{[^}]*\}/);
  });
});
