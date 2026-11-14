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
const NATIVE = ['mobile/clinician/src/', 'mobile/patient/src/', 'packages/ui-native/src/'];
const BASELINE = fileURLToPath(new URL('baseline.json', import.meta.url));

const matches = re => text => (text.match(re) ?? []).length;
const inApps = f => APPS.some(root => f.startsWith(root));
const inNative = f => NATIVE.some(root => f.startsWith(root));

const HEX = /["'[]#(?:[0-9a-fA-F]{3,4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})["'\]]/g;

export const RULES = {
  // A height patch on a library control reached zero in MDV-P1-078; oxlint's medvya-ui/no-height-patch rule now fails it (tools/oxlint-plugin).
  // An arbitrary font size and an sm:grid-cols reached zero in MDV-P2-057: medvya-ui/no-arbitrary-text-size and medvya-ui/no-sm-columns now fail them.
  // Colours come from the tokens: no hex in a class name (bg-[#…]) or a string ('#…') (MDV-P1-080).
  // At zero since MDV-P1-080, so any hex in packages/ui fails CI.
  // shortcut: stays here, not in oxlint, because oxlint 1.90 has no no-restricted-syntax; move it when oxlint can express it.
  rawHexLibrary: { in: f => f.startsWith(LIBRARY), count: matches(HEX) },
  rawHexApps: { in: inApps, count: matches(HEX) },
  // The Expo apps and packages/ui-native take every colour from theme (packages/tokens); at zero from the start (MDV-P5-032).
  rawHexNative: { in: inNative, count: matches(HEX) },
  // A local Field reached zero in MDV-P2-058 (medvya-ui/no-local-field). A raw h1, an inline "border bg-card" and a local Message, Card or Empty
  // reached zero in MDV-P2-059: react/forbid-elements (h1), medvya-ui/no-inline-card and medvya-ui/no-local-surface now fail them (tools/oxlint-plugin).
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
    const Tint = () => <div className="rounded-xl bg-[#E7F6F4]" style={{ color: '#fff' }} />;
    <div className="text-[13px] sm:grid-cols-3" />`;
  const at = files => measure(files);
  const base = at([[page, sample]]);
  const totals = Object.fromEntries(Object.entries(base).map(([k, v]) => [k, total(v)]));
  assert(JSON.stringify(totals) === JSON.stringify({ rawHexLibrary: 0, rawHexApps: 2, rawHexNative: 0 }), `a rule's pattern changed: ${JSON.stringify(totals)}`);

  // Scope: tests and other folders are not counted; the library has its own count.
  assert(total(at([['web/clinic/src/app/App.tsx', sample]]).rawHexApps) === 0, 'clinic app/ is outside the scope');
  const library = at([['packages/ui/src/primitives.tsx', sample]]);
  assert(total(library.rawHexLibrary) === 2 && total(library.rawHexApps) === 0, 'the library counts as the library');
  // Native: both Expo apps and ui-native, at a zero baseline, so one React Native colour string fails and names the file.
  const rn = `<View style={{ backgroundColor: '#0f766e', borderColor: "#FFF" }} />`;
  for (const f of ['mobile/clinician/src/features/x/components/Row.tsx', 'mobile/patient/src/app/_layout.tsx', 'packages/ui-native/src/controls.tsx']) {
    const native = at([[f, rn]]);
    assert(total(native.rawHexNative) === 2 && total(native.rawHexApps) === 0 && total(native.rawHexLibrary) === 0, `${f} counts as native`);
    assert(compare({}, native).rose[0]?.startsWith(`rawHexNative: ${f} has 2, baseline 0`), `a raw hex in ${f} fails against the zero baseline`);
  }
  assert(total(at([['mobile/clinician/app.json', rn]]).rawHexNative) === 0, 'only src/ is native code');

  // The guard itself: at the baseline it passes, one more hit fails and names the file, one fewer asks for the baseline to be lowered.
  const same = compare(base, at([[page, sample]]));
  assert(same.rose.length === 0 && !same.stale, 'a sample at the baseline passes');
  const other = 'web/clinic/src/features/y/components/New.tsx';
  const more = compare(base, at([[page, sample], [other, '<p className="text-[#abc]" />']]));
  assert(more.rose.length === 1 && more.rose[0].startsWith(`rawHexApps: ${other} has 1, baseline 0`), 'one extra raw hex fails and names the file');
  const lowered = at([[page, sample.replace("'#fff'", "'white'")]]);
  const fewer = compare(base, lowered);
  assert(fewer.rose.length === 0 && fewer.stale, 'after a fall nothing rose, but the baseline must be lowered');
  assert(compare(lowered, base).rose.length === 1, 'once the baseline is lowered the old number fails');
  console.log('self-test ok');
}

if (process.argv.includes('--self-test')) {
  selfTest();
  process.exit(0);
}

const files = [...APPS, LIBRARY, ...NATIVE]
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
