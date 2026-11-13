// UX audit (Reviewer C): screenshots of the platform console against a MOCKED API (synthetic data), not a deployed environment.
// Mocks and fixtures are copied from tests/e2e/platform-console.spec.ts and tests/e2e/platform-approvals.spec.ts.
// Run: pnpm --filter platform-admin exec vite --port 4175 --strictPort   (repo root, background)
//      node docs/product-ux-audit/tools/platform-admin-capture.mjs
import { createRequire } from 'node:module';
import { mkdirSync } from 'node:fs';

const require = createRequire('/Volumes/Development/medvya/tests/e2e/package.json');
const { chromium } = require('@playwright/test');
const axePath = require.resolve('axe-core/axe.min.js');
const base = 'http://localhost:4175';
const out = new URL('../screenshots/platform-admin-mocked/', import.meta.url).pathname;

const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==', 'base64');
const owner = over => ({ displayName: 'Ops Admin (synthetic)', role: 'Owner', keyVerified: true, hasKey: true, canRegister: false, ...over });
const team = {
  lastAccessReviewAt: null, accessReviewDueAt: '2026-11-01T00:00:00Z',
  members: [{ id: 'm-1', displayName: 'Ops Admin (synthetic)', email: 'ops.admin@medvya.example', role: 'Owner', hasKey: true, joined: true, lastActiveAt: '2026-11-07T05:00:00Z', accessEndsOn: null }],
};
const audit = [
  { id: 'a2', occurredAt: '2026-11-07T05:01:00Z', action: 'team.invite', resourceType: 'platform_member', resourceId: 'm-2', subjectTenantId: null, actor: 'Ops Admin (synthetic)', reason: 'New licence reviewer joining' },
  { id: 'a1', occurredAt: '2026-11-07T05:00:00Z', action: 'auth.signin', resourceType: 'platform_session', resourceId: 's-1', subjectTenantId: null, actor: 'Ops Admin (synthetic)', reason: null },
];
const doc = (id, kind) => ({ id, kind, contentType: 'image/png', sizeBytes: 2048, uploadedAt: '2026-11-08T05:00:00Z' });
const clinicDocs = [doc('d1', 'RegistrationCertificate'), doc('d2', 'TaxRegistration'), doc('d3', 'OwnerId'), doc('d4', 'MedicalCouncilRegistration')];
const hospitalDocs = [doc('h1', 'HospitalRegistration'), doc('h2', 'TaxRegistration'), doc('h3', 'OwnerId'), doc('h4', 'SignatoryLetter')];
const orgs = [
  { id: 'o-clinic', name: 'Rao Clinic (synthetic)', type: 'Clinic', state: 'Submitted', docs: clinicDocs, tax: '27AAPFU0939F1ZV' },
  { id: 'o-hospital', name: 'Lakeview Hospital (synthetic)', type: 'Hospital', state: 'Submitted', docs: hospitalDocs, tax: '27AAPFU0939F1ZV' },
  { id: 'o-done', name: 'Menon Clinic (synthetic)', type: 'Clinic', state: 'Approved', docs: clinicDocs, tax: '12345' },
];

async function mock(page, me) {
  await page.route('**/v1/me', r => (me ? r.fulfill({ json: me }) : r.fulfill({ status: 401 })));
  await page.route('**/v1/team', r => r.fulfill({ json: team }));
  await page.route('**/v1/audit**', r => r.fulfill({ json: audit }));
  await page.route('**/v1/organisations/?filter=*', r => {
    const filter = new URL(r.request().url()).searchParams.get('filter');
    const keep = o => filter === 'All' || (filter === 'Waiting' ? o.state === 'Submitted' : filter === 'NeedsChanges' ? o.state === 'NeedsChanges' : ['Approved', 'Rejected', 'Suspended'].includes(o.state));
    return r.fulfill({ json: orgs.filter(keep).map(o => ({ tenantId: o.id, name: o.name, country: 'IN', type: o.type, state: o.state, submittedAt: '2026-11-07T05:00:00Z', reason: null, itContactName: null, itContactEmail: null, documents: o.docs })) });
  });
  await page.route(/\/v1\/organisations\/o-[a-z]+$/, r => {
    const o = orgs.find(x => r.request().url().endsWith(x.id));
    return r.fulfill({ json: {
      name: o.name, country: 'IN', type: o.type, state: o.state, submittedAt: '2026-11-07T05:00:00Z', reason: null, address: '4 Lake Road, Surat', contactEmail: 'desk@example.test',
      registrationNumber: 'REG-1001', taxNumber: o.tax, itContactName: o.type === 'Hospital' ? 'IT lead (synthetic)' : null, itContactEmail: o.type === 'Hospital' ? 'it@example.test' : null,
      documents: o.docs, history: o.state === 'Approved' ? [{ at: '2026-11-08T06:00:00Z', actor: 'Ops Reviewer (synthetic)', state: 'Approved', reason: null }] : [],
    } });
  });
  await page.route('**/v1/organisations/*/documents/*', r => r.fulfill({ body: png, contentType: 'image/png' }));
}

// screen, state, path, session, optional locale and steps to reach the state
const shots = [
  ['sign-in', 'signed-out', '/?signin=failed', null],
  ['key-step', 'register-key', '/', owner({ keyVerified: false, hasKey: false, canRegister: true })],
  ['overview', 'default', '/', owner()],
  ['overview', 'reviewer', '/', owner({ role: 'Reviewer', displayName: 'Ops Reviewer (synthetic)' })],
  ['approvals', 'default', '/approvals', owner()],
  ['approval-detail', 'clinic', '/approvals/o-clinic', owner()],
  ['approval-detail', 'hospital-no-dpa', '/approvals/o-hospital', owner()],
  ['approval-detail', 'approved', '/approvals/o-done', owner()],
  ['approval-detail', 'viewer-open', '/approvals/o-clinic', owner(), 'en', async p => { await p.getByRole('button', { name: 'Open Clinic registration certificate' }).click(); }],
  ['approval-detail', 'approve-reason', '/approvals/o-clinic', owner(), 'en', async p => { await p.getByRole('button', { name: 'Approve', exact: true }).click(); }],
  ['approval-detail', 'arabic', '/approvals/o-clinic', owner(), 'ar'],
  ['team', 'default', '/team', owner()],
  ['team', 'invite-error', '/team', owner(), 'en', async p => { await p.getByRole('button', { name: 'Invite' }).click(); await p.getByRole('button', { name: 'Create invitation link' }).click(); }],
  ['audit', 'default', '/audit', owner()],
  ['audit', 'arabic', '/audit', owner(), 'ar'],
];

const browser = await chromium.launch();
const log = [];
for (const [width, height] of [[1440, 900], [375, 812]]) {
  for (const [screen, state, path, me, locale = 'en', steps] of shots) {
    const ctx = await browser.newContext({ viewport: { width, height } });
    const page = await ctx.newPage();
    await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
    await mock(page, me);
    let note = '';
    try {
      await page.goto(base + path, { waitUntil: 'networkidle' });
      if (steps) await steps(page);
      await page.waitForTimeout(300);
      await page.addScriptTag({ path: axePath });
      const m = await page.evaluate(async () => {
        const els = [...document.querySelectorAll('a,button,input,select,textarea,[role=button],[role=tab]')].filter(e => e.getClientRects().length);
        const small = els.filter(e => { const r = e.getBoundingClientRect(); return r.height < 44 || r.width < 44; });
        const axe = (await window.axe.run()).violations.map(v => `${v.id}(${v.nodes.length})`);
        return { h1: document.querySelector('h1')?.textContent ?? '', docH: document.documentElement.scrollHeight, ovf: document.documentElement.scrollWidth > innerWidth,
          targets: els.length, under44: small.length, sample: small.slice(0, 4).map(e => `${(e.textContent || e.getAttribute('aria-label') || e.tagName).trim().slice(0, 24)} ${Math.round(e.getBoundingClientRect().width)}x${Math.round(e.getBoundingClientRect().height)}`), axe };
      });
      note = `h1=${m.h1}|docH=${m.docH}|ovf=${m.ovf}|<44=${m.under44}/${m.targets} [${m.sample.join('; ')}]|axe=${m.axe.join(',')}`;
    } catch (e) { note = `ERROR ${String(e).split('\n')[0]}`; }
    mkdirSync(out + screen, { recursive: true });
    await page.screenshot({ path: `${out}${screen}/${width}-${state}.png`, fullPage: true });
    log.push(`${screen}|${width}|${state}|${note}`);
    await ctx.close();
  }
}
await browser.close();
console.log(log.join('\n'));
