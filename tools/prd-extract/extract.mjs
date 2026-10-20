// Runs the data script embedded in a Claude Design .dc.html file and dumps renderVals() as JSON.
// Usage: node tools/prd-extract/extract.mjs "design/Medvya PRD v2.dc.html" > out.json
import { readFileSync } from 'node:fs';
const src = readFileSync(process.argv[2], 'utf8');
// data-props values may contain '>', so locate the tag end as the first '>' followed by a newline.
const m = src.match(/<script type="text\/x-dc"[\s\S]*?>\n([\s\S]*?)<\/script>/);
if (!m) throw new Error('no dc script');
const React = { createElement: () => null };
class DCLogic { constructor() { this.props = {}; } setState() {} }
const Component = new Function('DCLogic', 'React', m[1] + '\n;return Component;')(DCLogic, React);
const c = new Component();
const strip = v => typeof v === 'function' ? undefined : v;
process.stdout.write(JSON.stringify(c.renderVals(), (k, v) => strip(v), 1));
