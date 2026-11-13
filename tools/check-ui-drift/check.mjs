// Counts UI patterns that DESIGN.md says belong in the shared library, and fails when a count rises (MDV-P1-076).
// A count may only fall, and the baseline is lowered in the pull request that lowers it:
//
//   node tools/check-ui-drift/check.mjs            compare the code with baseline.json (run from the repo root)
//   node tools/check-ui-drift/check.mjs --update   rewrite baseline.json; refused when any count rose
//   node tools/check-ui-drift/check.mjs --self-test
//
// When a count reaches zero, the ticket that got it there turns the matching oxlint rule to error and deletes the rule here.
// shortcut: regex over source text, so a class name built in a variable or helper is not seen; replace a rule with oxlint when it can express it.
import { readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const APPS = ['web/clinic/src/features/', 'web/platform-admin/src/'];
const LIBRARY = 'packages/ui/src/';
const BASELINE = fileURLToPath(new URL('baseline.json', import.meta.url));

const matches = re => text => (text.match(re) ?? []).length;
const inApps = f => APPS.some(root => f.startsWith(root));
const localComponent = name => ({
  in: f => inApps(f) && /\/features\/[^/]+\/components\//.test(f),
  count: matches(new RegExp(`\\b(?:function|const)\\s+${name}\\b`, 'g')),
});

// The text of the JSX opening tag that starts at `from`: it ends at the first '>' outside {…}, so `() => x` in a prop does not end it.
function openingTag(text, from) {
  let depth = 0;
  for (let i = from; i < text.length; i++) {
    if (text[i] === '{') depth++;
    else if (text[i] === '}') depth--;
    else if (text[i] === '>' && depth === 0) return text.slice(from, i);
  }
  return text.slice(from);
}
const HEX = /["'[]#(?:[0-9a-fA-F]{3,4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})["'\]]/g;

export const RULES = {
  // The library control is 44 px by default; a call site must not patch its height (falls in MDV-P1-077, MDV-P1-078).
  heightPatch: {
    in: inApps,
    count: text => [...text.matchAll(/<(?:Input|SelectTrigger|Textarea|Button)\b/g)]
      .filter(m => /(?<![\w-])(?:min-)?h-11(?![\w-])/.test(openingTag(text, m.index))).length,
  },
  // Type sizes come from the scale. The queue display and the print pages are read from a distance or on paper (MDV-P2-057).
  arbitraryFontSize: {
    in: f => inApps(f) && !/\/queue-tv\/|PrintPage\.tsx$/.test(f),
    count: matches(/\btext-\[[\d.]+(?:px|rem|em)\]/g),
  },
  // Colours come from the tokens: no hex in a class name (bg-[#…]) or a string ('#…') (MDV-P1-080).
  // At zero since MDV-P1-080, so any hex in packages/ui fails CI.
  // shortcut: stays here, not in oxlint, because oxlint 1.90 has no no-restricted-syntax; move it when oxlint can express it.
  rawHexLibrary: { in: f => f.startsWith(LIBRARY), count: matches(HEX) },
  rawHexApps: { in: inApps, count: matches(HEX) },
  // Columns change at the named tiers, not at the 360 px sm: step (MDV-P2-057).
  smGridCols: { in: inApps, count: matches(/\bsm:grid-cols-/g) },
  // A page title is PageHeader's (MDV-P2-059).
  rawH1: { in: inApps, count: matches(/<h1\b/g) },
  // A card is Card or Section from the library (MDV-P2-059).
  inlineCard: { in: inApps, count: matches(/\bborder bg-card\b/g) },
  localMessage: localComponent('Message'),
  localField: localComponent('Field'),
  localCard: localComponent('Card'),
  localEmpty: localComponent('Empty'),
};

// files: [path, text] pairs. Returns { rule: { path: hits } }, holding only files with hits.
export function measure(files) {
  const out = {};
  for (const [name, rule] of Object.entries(RULES)) {
    out[name] = {};
    for (const [path, text] of files) {
      const n = rule.in(path) ? rule.count(text) : 0;
      if (n) out[name][path] = n;
    }
  }
  return out;
}

const total = perFile => Object.values(perFile ?? {}).reduce((a, b) => a + b, 0);

// rose: one line per file behind a count that went up. stale: the code no longer equals the baseline, though nothing rose.
export function compare(baseline, measured) {
  const rose = [];
  let stale = false;
  for (const name of Object.keys(RULES)) {
    const was = baseline[name] ?? {}, now = measured[name];
    if (total(now) > total(was)) {
      for (const [path, n] of Object.entries(now)) {
        if (n > (was[path] ?? 0)) rose.push(`${name}: ${path} has ${n}, baseline ${was[path] ?? 0} (total ${total(now)}, baseline ${total(was)})`);
      }
    } else if (JSON.stringify(Object.entries(now).sort()) !== JSON.stringify(Object.entries(was).sort())) stale = true;
  }
  return { rose, stale };
}

function selfTest() {
  const assert = (c, msg) => { if (!c) { console.error('self-test failed:', msg); process.exit(1); } };
  const page = 'web/clinic/src/features/x/components/Page.tsx';
  const sample = `
    function Message() { return <h1 className="text-[13px] sm:grid-cols-2">x</h1>; }
    const Empty = () => <div className="rounded-xl border bg-card bg-[#E7F6F4]" style={{ color: '#fff' }} />;
    <Input onChange={e => set(e.target.value)} className="min-h-11" />
    <Button
      onClick={() => go()}
      className="h-11 w-full">go</Button>
    <Button className="max-h-11" /> <div className="h-11" /> <Input />`;
  const at = files => measure(files);
  const base = at([[page, sample]]);
  const totals = Object.fromEntries(Object.entries(base).map(([k, v]) => [k, total(v)]));
  assert(JSON.stringify(totals) === JSON.stringify({ heightPatch: 2, arbitraryFontSize: 1, rawHexLibrary: 0, rawHexApps: 2, smGridCols: 1, rawH1: 1, inlineCard: 1, localMessage: 1, localField: 0, localCard: 0, localEmpty: 1 }), `a rule's pattern changed: ${JSON.stringify(totals)}`);

  // Scope: tests and other folders are not counted; the library counts hex only; the queue display may size its own text.
  assert(total(at([['web/clinic/src/app/App.tsx', sample]]).rawH1) === 0, 'clinic app/ is outside the scope');
  assert(total(at([['web/clinic/src/features/x/hooks/useX.ts', sample]]).localMessage) === 0, 'local components are counted in components/ only');
  const library = at([['packages/ui/src/primitives.tsx', sample]]);
  assert(total(library.rawHexLibrary) === 2 && total(library.rawH1) === 0, 'the library counts raw hex only');
  assert(total(at([['web/clinic/src/features/queue-tv/components/QueueTvPage.tsx', sample]]).arbitraryFontSize) === 0, 'queue display is allow-listed');

  // The guard itself: at the baseline it passes, one more hit fails and names the file, one fewer asks for the baseline to be lowered.
  const same = compare(base, at([[page, sample]]));
  assert(same.rose.length === 0 && !same.stale, 'a sample at the baseline passes');
  const other = 'web/clinic/src/features/y/components/New.tsx';
  const more = compare(base, at([[page, sample], [other, 'function Message() {}']]));
  assert(more.rose.length === 1 && more.rose[0].startsWith(`localMessage: ${other} has 1, baseline 0`), 'one extra local Message fails and names the file');
  const patched = compare(base, at([[page, sample + '<Input className="h-11" />']]));
  assert(patched.rose.length === 1 && patched.rose[0].startsWith(`heightPatch: ${page} has 3, baseline 2`), 'one extra h-11 patch fails and names the file');
  const lowered = at([[page, sample.replace('min-h-11', '')]]);
  const fewer = compare(base, lowered);
  assert(fewer.rose.length === 0 && fewer.stale, 'after a fall nothing rose, but the baseline must be lowered');
  assert(compare(lowered, base).rose.length === 1, 'once the baseline is lowered the old number fails');
  console.log('self-test ok');
}

if (process.argv.includes('--self-test')) {
  selfTest();
  process.exit(0);
}

const files = [...APPS, LIBRARY]
  .flatMap(root => readdirSync(root, { recursive: true }).map(f => root + f.replaceAll('\\', '/')))
  .filter(f => /\.tsx?$/.test(f) && !/\.test\.tsx?$/.test(f))
  .sort()
  .map(f => [f, readFileSync(f, 'utf8')]);
const measured = measure(files);
const baseline = JSON.parse(readFileSync(BASELINE, 'utf8'));
const { rose, stale } = compare(baseline, measured);

for (const name of Object.keys(RULES)) console.log(`${name.padEnd(18)} ${String(total(measured[name])).padStart(4)}  (baseline ${total(baseline[name])})`);
if (rose.length) {
  for (const line of rose) console.error(line);
  console.error('A count rose. Use the shared component or token (DESIGN.md); a count may only fall.');
  process.exit(1);
}
if (process.argv.includes('--update')) {
  writeFileSync(BASELINE, JSON.stringify(measured, null, 2) + '\n');
  console.log('baseline.json written');
} else if (stale) {
  console.error('The code no longer matches baseline.json. Run: pnpm check:ui-drift --update, and commit the lowered baseline.');
  process.exit(1);
} else console.log(`${files.length} files checked, no count rose`);
