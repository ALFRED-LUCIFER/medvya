// Fails if a tracked file contains a national ID that passes its real checksum.
// Synthetic data must use invalid-by-construction IDs (docs/compliance/secure-development.md).
// Usage: node tools/check-synthetic/check.mjs   (--self-test runs the built-in checks)
import { execFileSync } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';

const D = [[0,1,2,3,4,5,6,7,8,9],[1,2,3,4,0,6,7,8,9,5],[2,3,4,0,1,7,8,9,5,6],[3,4,0,1,2,8,9,5,6,7],[4,0,1,2,3,9,5,6,7,8],[5,9,8,7,6,0,4,3,2,1],[6,5,9,8,7,1,0,4,3,2],[7,6,5,9,8,2,1,0,4,3],[8,7,6,5,9,3,2,1,0,4],[9,8,7,6,5,4,3,2,1,0]];
const P = [[0,1,2,3,4,5,6,7,8,9],[1,5,7,6,2,8,3,0,9,4],[5,8,0,3,7,9,6,1,4,2],[8,9,1,6,0,4,3,5,2,7],[9,4,5,3,1,2,7,6,8,0],[4,2,8,6,5,7,3,9,0,1],[2,7,9,3,8,0,6,4,1,5],[7,0,4,6,9,1,3,2,5,8]];
export const verhoeff = s => [...s].reverse().reduce((c, d, i) => D[c][P[i % 8][+d]], 0) === 0;
export const luhn = s => [...s].reverse().reduce((sum, d, i) => { let n = +d; if (i % 2) { n *= 2; if (n > 9) n -= 9; } return sum + n; }, 0) % 10 === 0;

// Aadhaar: 12 digits, first digit 2-9, Verhoeff-valid. Emirates ID: 784-YYYY-NNNNNNN-N, Luhn-valid.
export function findRealLooking(text) {
  const hits = [];
  for (const m of text.matchAll(/\b([2-9]\d{3})[ -]?(\d{4})[ -]?(\d{4})\b/g)) {
    const n = m[1] + m[2] + m[3];
    if (verhoeff(n)) hits.push(`Aadhaar-like ${m[0]}`);
  }
  for (const m of text.matchAll(/\b784-?(\d{4})-?(\d{7})-?(\d)\b/g)) {
    if (luhn('784' + m[1] + m[2] + m[3])) hits.push(`Emirates-ID-like ${m[0]}`);
  }
  return hits;
}

if (process.argv.includes('--self-test')) {
  const assert = (c, msg) => { if (!c) { console.error('self-test failed:', msg); process.exit(1); } };
  assert(verhoeff('2363'), 'verhoeff known-valid');          // classic Verhoeff example
  assert(!verhoeff('2364'), 'verhoeff known-invalid');
  assert(luhn('79927398713'), 'luhn known-valid');
  assert(!luhn('79927398710'), 'luhn known-invalid');
  assert(findRealLooking('784-1988-1234567-1').length === 0, 'design sample EID is not Luhn-valid');
  assert(findRealLooking('no ids here 1234').length === 0, 'plain text');
  const withCheck = (body, ok) => body + [...'0123456789'].find(d => ok(body + d));
  assert(findRealLooking(withCheck('23456789012', verhoeff)).length === 1, 'flags a checksum-valid Aadhaar-like number');
  const eid = withCheck('78419881234567', luhn);
  assert(findRealLooking(`${eid.slice(0, 3)}-${eid.slice(3, 7)}-${eid.slice(7, 14)}-${eid[14]}`).length === 1, 'flags a checksum-valid EID');
  console.log('self-test ok');
  process.exit(0);
}

// Imported design files are read-only references; everything else must stay synthetic.
const files = execFileSync('git', ['ls-files'], { encoding: 'utf8' }).split('\n')
  // existsSync: skip files still tracked but already deleted in the working tree.
  .filter(f => f && existsSync(f) && !f.startsWith('design/') && !/\.(png|jpe?g|gif|ico|woff2?|lock)$/.test(f) && f !== 'pnpm-lock.yaml');
let bad = 0;
for (const f of files) {
  for (const hit of findRealLooking(readFileSync(f, 'utf8'))) { console.error(`${f}: ${hit}`); bad++; }
}
if (bad) { console.error(`${bad} real-looking national ID(s) found. Use invalid-by-construction synthetic IDs.`); process.exit(1); }
console.log(`checked ${files.length} files, no real-looking national IDs`);
