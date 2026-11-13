// Deletes `h-11` and `min-h-11` from Input, SelectTrigger, Textarea and Button call sites and buttonVariants() calls: the library default is 44 px (MDV-P1-077, MDV-P1-078).
// Re-runnable on any branch in seconds; it only removes those two class tokens inside the opening tag or call and tidies the string they were in.
//
//   node tools/codemods/remove-height-patches.mjs              rewrite the files, print a count per file
//   node tools/codemods/remove-height-patches.mjs --check      print what would change, write nothing
//   node tools/codemods/remove-height-patches.mjs --self-test
//
// Input, SelectTrigger and Textarea are 44 px by default and every Button size is 44 px tall, so the patch is redundant.
// shortcut: a class held in a variable or a spread prop (const control = 'min-h-11', {...a}) is not seen; those are fixed by hand, and check-ui-drift does not count them either.
import { readdirSync, readFileSync, writeFileSync } from 'node:fs';

const ROOTS = ['web/clinic/src/features/', 'web/platform-admin/src/'];
const TAG = /<(?:Input|SelectTrigger|Textarea|Button)\b|\bbuttonVariants\(/g;
const PATCH = /^(?:min-)?h-11$/;
const EMPTIED = '\u0000'; // marks a string the codemod emptied, so only those strings are tidied away

// The end of the JSX opening tag that starts at `from` (the first '>' outside {…}, as tools/check-ui-drift reads it), or of the buttonVariants( call.
function tagEnd(text, from) {
  const call = text[from] !== '<';
  let depth = 0;
  for (let i = from; i < text.length; i++) {
    if (text[i] === '{' || text[i] === '(') depth++;
    else if (text[i] === '}' || text[i] === ')') { depth--; if (call && depth === 0) return i + 1; }
    else if (!call && text[i] === '>' && depth === 0) return i;
  }
  return text.length;
}

// Removes the patch tokens from every string literal in one opening tag; returns [newTag, removed].
function fixTag(tag) {
  let removed = 0;
  const out = tag.replace(/(["'`])((?:(?!\1)[^\\]|\\.)*)\1/g, (whole, q, body) => {
    if (q === '`' && body.includes('${')) {
      // Only the plain text between ${…} parts is class text.
      const next = body.replace(/(^|\})([^$}]*)(?=\$\{|$)/g, (m, lead, part, at) => {
        let t = tidy(part, n => (removed += n), true);
        if (t === part) return m;
        if (at === 0) t = t.trimStart(); // the template's own edges are not next to ${…}
        if (at + m.length === body.length) t = t.trimEnd();
        return lead + t;
      });
      return q + next + q;
    }
    const next = tidy(body, n => (removed += n), false);
    return next === '' && body !== '' ? EMPTIED : q + next + q;
  });
  // An attribute, cn() argument or buttonVariants className that the codemod emptied is dropped.
  const tidied = out
    .replace(/\s+className=\{?\u0000\}?/g, '')
    .replace(/,\s*className:\s*\u0000/g, '').replace(/className:\s*\u0000,?\s*/g, '').replace(/\(\{\s*\}\)/g, '()')
    .replace(/\u0000,\s*/g, '').replace(/,\s*\u0000/g, '');
  return [tidied.replaceAll(EMPTIED, "''"), removed];
}

function tidy(s, count, inTemplate) {
  const tokens = s.split(/(\s+)/);
  const kept = tokens.filter(t => !PATCH.test(t));
  const n = tokens.length - kept.length;
  if (!n) return s;
  count(n);
  let joined = kept.join('').replace(/\s{2,}/g, ' ');
  // Inside a template a space next to ${…} separates classes, so keep the edges there.
  if (!inTemplate) joined = joined.trim();
  return joined;
}

export function rewrite(text) {
  let out = '', last = 0, removed = 0;
  for (const m of text.matchAll(TAG)) {
    if (m.index < last) continue;
    const end = tagEnd(text, m.index);
    const [tag, n] = fixTag(text.slice(m.index, end));
    out += text.slice(last, m.index) + tag;
    last = end;
    removed += n;
  }
  return [out + text.slice(last), removed];
}

function selfTest() {
  const assert = (c, msg) => { if (!c) { console.error('self-test failed:', msg); process.exit(1); } };
  const cases = [
    ['<Input className="min-h-11" />', '<Input />'],
    ['<Input id="a" className="h-11 w-full" />', '<Input id="a" className="w-full" />'],
    ["<Button variant=\"outline\" className=\"min-h-11 gap-2\" onClick={() => go(a > b)}>x</Button>", "<Button variant=\"outline\" className=\"gap-2\" onClick={() => go(a > b)}>x</Button>"],
    ["<SelectTrigger className={cn('h-11', wide && 'w-full')}>", "<SelectTrigger className={cn(wide && 'w-full')}>"],
    ["<SelectTrigger className={cn('w-40', 'min-h-11')}>", "<SelectTrigger className={cn('w-40')}>"],
    ['<Input className={`min-h-11 ${x} w-20`} />', '<Input className={`${x} w-20`} />'],
    ['<Button size="sm" className="min-h-11">x</Button>', '<Button size="sm">x</Button>'],
    ['<Input className="max-h-11 md:h-12" /> <div className="h-11" />', '<Input className="max-h-11 md:h-12" /> <div className="h-11" />'],
    ['<Input className="h-auto min-h-11 py-2" />', '<Input className="h-auto py-2" />'],
    ["<Input className=\"min-h-11\" onChange={() => set('', x)} />", "<Input onChange={() => set('', x)} />"],
    ["<Link className={buttonVariants({ className: 'min-h-11' })}>", '<Link className={buttonVariants()}>'],
    ["buttonVariants({ variant: 'outline', className: 'min-h-11' })", "buttonVariants({ variant: 'outline' })"],
    ["buttonVariants({ className: 'min-h-11 gap-2', variant: 'ghost' })", "buttonVariants({ className: 'gap-2', variant: 'ghost' })"],
    ["cn(buttonVariants({ variant: 'ghost' }), 'h-11 px-2')", "cn(buttonVariants({ variant: 'ghost' }), 'h-11 px-2')"],
  ];
  for (const [input, want] of cases) {
    const [got] = rewrite(input);
    assert(got === want, `${input}\n  got  ${got}\n  want ${want}`);
  }
  assert(rewrite('<Input className="h-11 min-h-11" />')[1] === 2, 'counts every token removed');
  const [once] = rewrite('<Button className="min-h-11 w-full">x</Button>');
  assert(rewrite(once)[1] === 0, 'a second run changes nothing');
  console.log('self-test ok');
}

if (process.argv.includes('--self-test')) {
  selfTest();
  process.exit(0);
}
const check = process.argv.includes('--check');
let total = 0;
for (const root of ROOTS) {
  for (const f of readdirSync(root, { recursive: true }).map(f => root + f.replaceAll('\\', '/')).filter(f => f.endsWith('.tsx')).sort()) {
    const text = readFileSync(f, 'utf8');
    const [next, n] = rewrite(text);
    if (!n) continue;
    total += n;
    console.log(`${String(n).padStart(3)}  ${f}`);
    if (!check) writeFileSync(f, next);
  }
}
console.log(`${total} patch${total === 1 ? '' : 'es'} ${check ? 'would be removed' : 'removed'}`);
