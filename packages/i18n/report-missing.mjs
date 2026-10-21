// Prints translation keys missing per locale (they fall back to English). Report only; never fails CI.
import { readFileSync } from 'node:fs';

const load = l => JSON.parse(readFileSync(new URL(`./messages/${l}.json`, import.meta.url), 'utf8'));
const en = Object.keys(load('en'));
for (const loc of ['hi', 'ar']) {
  const cat = load(loc), missing = en.filter(k => !(k in cat));
  console.log(`${loc}: ${missing.length ? `${missing.length} missing → ${missing.join(', ')}` : 'complete'}`);
}
