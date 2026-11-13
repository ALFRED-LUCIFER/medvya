// Design tokens: src/tokens.json is the only place a value is written. This renders the web CSS variables
// (src/tokens.css, consumed by packages/ui and the public site) and the React Native theme (src/theme.ts).
// Run `pnpm --filter @medvya/tokens generate` after editing tokens.json; the test fails if the outputs are stale.
import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const dir = new URL('./src/', import.meta.url);
const camel = s => s.replace(/-(\w)/g, (_, c) => c.toUpperCase());
const num = v => (/^[\d.]+(px|ms)$/.test(v) ? parseFloat(v) : v);
// Each group is a Tailwind theme namespace (--text-*, --spacing-*, --container-*, ...), so a token is also a utility.
// `space` is not one: it names the allowed steps of Tailwind's own numeric scale, which a --spacing-<n> key would override.
const groups = ['color', 'radius', 'font', 'breakpoint', 'text', 'space', 'spacing', 'container', 'shadow', 'ease', 'transition-duration'];

export function render(source) {
  // A tone names a palette colour per role; it renders as --color-<tone>-<role> (text-danger-text, bg-danger-bg, border-danger-border).
  const tone = Object.fromEntries(Object.entries(source.tone).map(([name, roles]) =>
    [name, Object.fromEntries(Object.entries(roles).map(([role, ref]) => {
      if (!source.color[ref]) throw new Error(`tone ${name}.${role} names a colour that does not exist: ${ref}`);
      return [role, source.color[ref]];
    }))]));
  const toneColors = Object.entries(tone).flatMap(([name, roles]) => Object.entries(roles).map(([role, v]) => [`${name}-${role}`, v]));
  const toneNotes = Object.fromEntries(Object.keys(tone).map(name => [`color-${name}-text`, source.notes[`tone-${name}`]]));
  const t = { ...source, color: { ...source.color, ...Object.fromEntries(toneColors) }, notes: { ...source.notes, ...toneNotes } };
  const cssVar = (g, k) => (g === 'radius' && k === 'default' ? '--radius' : `--${g}-${k}`);
  // A type token is an object: font-size is --text-<name>, the others are Tailwind's --text-<name>--<property> modifiers.
  const decls = (g, k, v) => (typeof v === 'string' ? [[cssVar(g, k), v]]
    : Object.entries(v).map(([p, x]) => [p === 'font-size' ? cssVar(g, k) : `${cssVar(g, k)}--${p}`, x]));
  const lines = groups.flatMap(g => Object.entries(t[g]).flatMap(([k, v]) => decls(g, k, v).map(([name, value], i) =>
    `  ${name}: ${value};${i === 0 && t.notes[`${g}-${k}`] ? ` /* ${t.notes[`${g}-${k}`]} */` : ''}`)));
  const css = `/* Generated from tokens.json by generate.mjs. Do not edit. */\n@theme static {\n${lines.join('\n')}\n}\n`;
  const plain = v => (typeof v === 'string' ? num(v) : obj(v));
  const obj = o => Object.fromEntries(Object.entries(o).map(([k, v]) => [camel(k), plain(v)]));
  const theme = `// Generated from tokens.json by generate.mjs. Do not edit. Plain values for React Native (px and ms become numbers).\nexport const theme = ${JSON.stringify({ ...Object.fromEntries(groups.map(g => [camel(g), obj(t[g])])), tone }, null, 2)} as const;\n`;
  return { css, theme };
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const { css, theme } = render(JSON.parse(readFileSync(new URL('tokens.json', dir), 'utf8')));
  writeFileSync(new URL('tokens.css', dir), css);
  writeFileSync(new URL('theme.ts', dir), theme);
}
