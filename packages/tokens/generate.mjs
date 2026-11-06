// Design tokens: src/tokens.json is the only place a value is written. This renders the web CSS variables
// (src/tokens.css, consumed by packages/ui and the public site) and the React Native theme (src/theme.ts).
// Run `pnpm --filter @medvya/tokens generate` after editing tokens.json; the test fails if the outputs are stale.
import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const dir = new URL('./src/', import.meta.url);
const camel = s => s.replace(/-(\w)/g, (_, c) => c.toUpperCase());
const px = v => (v.endsWith('px') ? Number(v.slice(0, -2)) : v);

export function render(t) {
  const cssVar = (g, k) => (g === 'radius' && k === 'default' ? '--radius' : `--${g}-${k}`);
  const groups = ['color', 'radius', 'font', 'breakpoint'];
  const lines = groups.flatMap(g =>
    Object.entries(t[g]).map(([k, v]) => `  ${cssVar(g, k)}: ${v};${t.notes[`${g}-${k}`] ? ` /* ${t.notes[`${g}-${k}`]} */` : ''}`));
  const css = `/* Generated from tokens.json by generate.mjs. Do not edit. */\n@theme static {\n${lines.join('\n')}\n}\n`;
  const obj = g => Object.fromEntries(Object.entries(t[g]).map(([k, v]) => [camel(k), px(v)]));
  const theme = `// Generated from tokens.json by generate.mjs. Do not edit. Plain values for React Native (px become numbers).\nexport const theme = ${JSON.stringify(Object.fromEntries(groups.map(g => [g, obj(g)])), null, 2)} as const;\n`;
  return { css, theme };
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const { css, theme } = render(JSON.parse(readFileSync(new URL('tokens.json', dir), 'utf8')));
  writeFileSync(new URL('tokens.css', dir), css);
  writeFileSync(new URL('theme.ts', dir), theme);
}
