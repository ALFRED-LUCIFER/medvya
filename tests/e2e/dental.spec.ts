import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';
import { targetPx } from './support/targets';

// MDV-P8-011 and MDV-P8-012: the Dental tab of the patient record (design DN-02 tooth chart, DN-03 plan and estimate). CI has no API: the session and the
// dental answers are mocked in the shapes DentalEndpoints sends. The rules, append-only findings, the plan's status moves, the visit's bill, row-level
// security, the care relationship and the audit rows are covered by DentalRulesTests and DentalTests on PostgreSQL. Synthetic data only.
const session = (flags: string[] = ['clinical.dental'], notes = 'Sign') => ({
  user: { id: 'u-das', displayName: 'Dr. Kiran Das (synthetic)' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role: 'SoloDoctor' }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role: 'SoloDoctor', flags, country: 'IN',
    capabilities: { RegisterBookPatients: 'Read', CheckInQueue: 'Full', Patient360Chart: 'Full', ClinicalNotes: notes, StaffRolesIntegrations: 'Config' } },
});

const finding = (id: string, tooth: string, condition: string, surfaces: string | null, recordedAt: string) =>
  ({ id, tooth, condition, surfaces, note: null, encounterId: 'e-1', recordedBy: 'u-das', recordedAt, author: 'Dr. Kiran Das (synthetic)' });
const item = (id: string, serviceName: string, tooth: string | null, phase: number, estimateMinor: number, status: string, reason: string | null = null) =>
  ({ id, serviceId: `s-${id}`, serviceName, tooth, surfaces: null, phase, estimateMinor, currency: 'INR', status, reason, createdAt: '2026-11-06T04:30:00Z',
    changedAt: '2026-11-06T04:30:00Z', doneAt: status === 'Done' ? '2026-11-06T05:00:00Z' : null, version: 7, consentKey: null as string | null, consent: null as unknown, stages: null as unknown });

const check = (encounterId: string) => ({ id: 'h-1', encounterId, yes: ['BloodThinners'], note: null, confirmedBy: 'u-das', confirmedAt: '2026-11-06T04:40:00Z', author: 'Dr. Kiran Das (synthetic)' });
const signed = { id: 'c-1', planItemId: 'p-5', templateKey: 'extraction', templateVersion: 1, language: 'en', signedBy: 'Farhan Qureshi (synthetic)', signedAt: '2026-11-06T05:02:00Z', withdrawnAt: null, withdrawalReason: null, current: true };
const extraction = (consent: unknown) => ({ ...item('p-5', 'Simple extraction (synthetic)', '48', 1, 80_000, 'Planned'), consentKey: 'extraction', consent });
const chart = (openEncounterId: string | null, acceptanceCurrent = true, historyOn: string | null = openEncounterId, consent: unknown = undefined) => ({
  teeth: [finding('f-3', '36', 'Caries', 'DO', '2026-11-06T04:51:00Z'), finding('f-2', '46', 'Missing', null, '2026-11-06T04:45:00Z')],
  history: [finding('f-3', '36', 'Caries', 'DO', '2026-11-06T04:51:00Z'), finding('f-2', '46', 'Missing', null, '2026-11-06T04:45:00Z'), finding('f-1', '36', 'Caries', 'O', '2026-10-01T04:30:00Z')],
  plan: {
    items: [{ ...item('p-1', 'Root canal, molar (synthetic)', '36', 1, 650_000, 'InProgress'), stages: [
      { name: 'accessAndCleaning', minutes: 60, doneAt: '2026-10-30T05:00:00Z', encounterId: 'e-1' }, { name: 'shaping', minutes: 45, doneAt: null, encounterId: null },
      { name: 'obturation', minutes: 45, doneAt: null, encounterId: null }] }, item('p-2', 'Crown, zirconia (synthetic)', '36', 2, 1_200_000, 'Planned'),
      item('p-3', 'Scaling and polishing (synthetic)', null, 2, 150_000, 'Declined', 'Patient: later, cost (synthetic)'), ...(consent === undefined ? [] : [extraction(consent)])],
    totalMinor: 1_850_000, currency: 'INR',
    acceptance: { id: 'a-1', acceptedBy: 'Farhan Qureshi (synthetic)', method: 'OnTablet', totalMinor: 1_850_000, currency: 'INR', acceptedAt: '2026-11-06T05:08:00Z', recordedBy: 'u-das' },
    acceptanceCurrent,
  },
  openEncounterId, timeZone: 'Asia/Kolkata', rulesSignedOff: false, historyCheck: historyOn ? check(historyOn) : null, openVisitChairId: 'r-1',
});
const services = [
  { id: 's-rct', name: 'Root canal, molar (synthetic)', code: 'DN-END003', durationMinutes: 60, priceMinor: 650_000, currency: 'INR' },
  { id: 's-none', name: 'Unpriced item (synthetic)', code: null, durationMinutes: null, priceMinor: null, currency: 'INR' },
];

type Call = { method: string; url: string; body: unknown };
// A library Section is titled but not a landmark: find it by its own heading (the innermost section that has it).
const section = (within: Page | import('@playwright/test').Locator, heading: string) => {
  const page = 'page' in within && typeof within.page === 'function' ? within.page() : within as Page;
  return within.locator('section').filter({ has: page.getByRole('heading', { name: heading, exact: true }) }).last();
};
async function open(page: Page, { locale = 'en', openEncounterId = 'e-9' as string | null, acceptanceCurrent = true, move = 200, flags = ['clinical.dental'], notes = 'Sign', historyOn = 'same' as string | null, consent = undefined as unknown } = {}) {
  const calls: Call[] = [];
  const log = (r: import('@playwright/test').Route) => calls.push({ method: r.request().method(), url: r.request().url(), body: r.request().postDataJSON() });
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: session(flags, notes) }));
  await page.route('**/v1/patients/p-1/dental', r => { log(r); return r.fulfill({ json: chart(openEncounterId, acceptanceCurrent, historyOn === 'same' ? openEncounterId : historyOn, consent) }); });
  await page.route('**/v1/patients/p-1/dental/plan/*/stages', r => { log(r); return r.fulfill({ json: item('p-1', 'x', '36', 1, 1, 'InProgress') }); });
  await page.route('**/v1/patients/p-1/dental/plan/*/consent', r => { log(r); return r.fulfill({ json: signed }); });
  await page.route('**/v1/patients/p-1/dental/consents/*/withdraw', r => { log(r); return r.fulfill({ json: { ...signed, current: false, withdrawnAt: '2026-11-06T05:30:00Z', withdrawalReason: 'x' } }); });
  await page.route('**/v1/encounters/*/dental/history-check', r => { log(r); return r.fulfill({ json: check(openEncounterId ?? 'e-9') }); });
  await page.route('**/v1/patients/p-1/dental/services', r => r.fulfill({ json: services }));
  await page.route('**/v1/encounters/*/dental/findings', r => { log(r); return r.fulfill({ json: finding('f-4', '37', 'Caries', 'DO', '2026-11-06T05:10:00Z') }); });
  await page.route('**/v1/patients/p-1/dental/plan', r => { log(r); return r.fulfill({ json: item('p-4', 'Root canal, molar (synthetic)', '36', 1, 650_000, 'Planned') }); });
  await page.route('**/v1/patients/p-1/dental/plan/*/move', r => { log(r); return move === 200 ? r.fulfill({ json: item('p-1', 'x', '36', 1, 1, 'Done') }) : r.fulfill({ status: move, json: {} }); });
  await page.route('**/v1/patients/p-1/dental/estimate/acceptance', r => { log(r); return r.fulfill({ json: chart(null).plan.acceptance }); });
  await page.route('**/v1/patients/p-1/dental/pdf', r => { log(r); return r.fulfill({ status: 200, contentType: 'application/pdf', body: Buffer.from('%PDF-1.7\n%synthetic\n') }); });
  await page.goto('/patients/p-1/dental');
  return calls;
}

test('the chart prints each tooth\'s code and says its state in words; an unrecorded tooth is "not recorded"; the rules are a draft', async ({ page }) => {
  await open(page);
  const chartGroup = page.getByRole('group', { name: 'Tooth chart, FDI numbering, shown facing the patient' });
  await expect(chartGroup.getByRole('button', { name: '36 C·DO, Caries (DO)' })).toHaveText('36 C·DO');
  await expect(chartGroup.getByRole('button', { name: '46 X, Missing' })).toHaveText('46 X');
  await expect(chartGroup.getByRole('button', { name: '11, Not recorded' })).toHaveText('11–');
  await expect(page.getByText('The condition list and surface rules are a draft until a dentist signs them off.')).toBeVisible();
  await expect(page.getByRole('link', { name: 'Dental', exact: true })).toHaveAttribute('aria-current', 'page');
});

test('a tooth\'s history keeps every finding, newest first, and says a correction is a newer finding', async ({ page }) => {
  await open(page);
  await page.getByRole('button', { name: '36 C·DO, Caries (DO)' }).click();
  const tooth = page.getByRole('region', { name: 'Tooth 36' });
  await expect(tooth).toContainText('Now: Caries · surfaces DO');
  const history = tooth.locator('section').filter({ has: page.getByRole('heading', { name: 'History of tooth 36' }) }).getByRole('listitem');
  await expect(history).toHaveCount(2);
  await expect(history.first()).toContainText('Caries (DO)');
  await expect(history.last()).toContainText('Caries (O)');
  await expect(tooth).toContainText('A finding is never edited.');
});

/** SAFETY (mutation check: drop findingProblem from the Add button, and this fails: caries could be sent without a surface). */
test('a caries finding needs a surface, then goes in the body with the surfaces in order and the consultation in the path', async ({ page }) => {
  const calls = await open(page);
  await page.getByRole('button', { name: '37, Not recorded' }).click();
  const tooth = page.getByRole('region', { name: 'Tooth 37' });
  await tooth.getByRole('radio', { name: /Caries/ }).check();
  await expect(tooth.getByRole('alert')).toContainText('Choose at least one surface.');
  await expect(tooth.getByRole('button', { name: 'Add finding to tooth 37' })).toBeDisabled();
  await tooth.getByRole('button', { name: 'O · Occlusal or incisal' }).click();
  await tooth.getByRole('button', { name: 'D · Distal' }).click();
  await tooth.getByRole('button', { name: 'Add finding to tooth 37' }).click();
  await expect(tooth.getByRole('status')).toContainText('Finding added to tooth 37.');
  const posted = calls.filter(c => c.method === 'POST');
  expect(posted).toHaveLength(1);
  expect(posted[0]!.url).toMatch(/\/v1\/encounters\/e-9\/dental\/findings$/);
  expect(posted[0]!.body).toEqual({ tooth: '37', condition: 'Caries', surfaces: 'DO', note: '' });
});

test('the medical history check: the risk in words, and treatment starts only once it is confirmed on this visit (MDV-P8-014)', async ({ page }) => {
  const calls = await open(page, { historyOn: 'e-earlier' });
  const card = page.getByRole('region', { name: 'Medical history check' });
  await expect(card).toContainText('Last confirmed');
  await expect(card).toContainText('Answered yes: check before treatment');
  await expect(card.getByRole('listitem')).toHaveText(['Takes blood thinners (anticoagulants or antiplatelets)']);
  const plan = page.getByRole('region', { name: 'Treatment plan' });
  await expect(plan.getByRole('button', { name: 'Start' })).toBeDisabled();
  await expect(plan).toContainText('Confirm the medical history check on this visit before starting treatment.');

  await card.getByRole('button', { name: 'Confirm the history check' }).click();
  await expect(card.getByRole('alert')).toHaveText('Answer every question with yes or no.');
  expect(calls.filter(c => c.url.includes('history-check'))).toHaveLength(0);
  for (const q of ['Diabetes', 'Pregnant or may be pregnant', 'Heart condition or endocarditis risk', 'Takes or has taken bisphosphonates', 'Allergy to local anaesthetic'])
    await card.getByRole('group', { name: q }).getByRole('radio', { name: 'No' }).check();
  await card.getByRole('group', { name: 'Takes blood thinners (anticoagulants or antiplatelets)' }).getByRole('radio', { name: 'Yes' }).check();
  await card.getByRole('button', { name: 'Confirm the history check' }).click();
  await expect.poll(() => calls.filter(c => c.url.includes('history-check')).map(c => c.body)).toEqual([{
    answers: { Diabetes: false, Pregnancy: false, HeartOrEndocarditisRisk: false, Bisphosphonates: false, LocalAnaestheticAllergy: false, BloodThinners: true }, note: '',
  }]);
});

test('an extraction waits for the patient\'s signed consent: read, name, signature on the tablet; then it can be withdrawn (MDV-P8-019)', async ({ page }) => {
  const calls = await open(page, { consent: null });
  const plan = page.getByRole('region', { name: 'Treatment plan' });
  const row = plan.getByRole('listitem').filter({ hasText: 'Simple extraction (synthetic)' });
  await expect(row).toContainText('Consent needed before it starts: Consent for tooth extraction.');
  await expect(row.getByRole('button', { name: 'Start' })).toBeDisabled();
  await row.getByRole('button', { name: 'Sign the consent on the tablet' }).click();
  await expect(row).toContainText('The dentist will remove the tooth named above under local anaesthetic.');
  await expect(row).toContainText('Draft wording: awaiting review by a lawyer and a dentist');
  const record = row.getByRole('button', { name: 'Record the signed consent' });
  await expect(record).toBeDisabled(); // no name, no signature yet
  await row.getByLabel('Signed by (patient or guardian)').fill('Farhan Qureshi (synthetic)');
  const pad = row.getByRole('img', { name: 'Patient\'s signature' });
  await pad.scrollIntoViewIfNeeded();
  const box = (await pad.boundingBox())!;
  await page.mouse.move(box.x + 20, box.y + 40);
  await page.mouse.down();
  await page.mouse.move(box.x + 120, box.y + 80, { steps: 8 });
  await page.mouse.move(box.x + 220, box.y + 30, { steps: 8 });
  await page.mouse.up();
  await expect(record).toBeEnabled();
  await record.click();
  await expect.poll(() => calls.filter(c => c.url.endsWith('/consent')).length).toBe(1);
  const body = calls.find(c => c.url.endsWith('/consent'))!.body as { encounterId: string; language: string; signedBy: string; signature: string };
  expect([body.encounterId, body.language, body.signedBy]).toEqual(['e-9', 'en', 'Farhan Qureshi (synthetic)']);
  expect(Buffer.from(body.signature, 'base64').subarray(1, 4).toString()).toBe('PNG'); // the drawn signature, as an image
});

test('a signed consent shows who and when, the stored form, and a withdrawal needs its reason (MDV-P8-019)', async ({ page }) => {
  const calls = await open(page, { consent: signed });
  const row = page.getByRole('region', { name: 'Treatment plan' }).getByRole('listitem').filter({ hasText: 'Simple extraction (synthetic)' });
  await expect(row).toContainText('Consent signed by Farhan Qureshi (synthetic)');
  await expect(row.getByRole('button', { name: 'Start' })).toBeEnabled();
  await expect(row.getByRole('button', { name: 'Signed form' })).toBeVisible();
  await row.getByRole('button', { name: 'Withdraw consent' }).click();
  const confirm = row.getByRole('button', { name: 'Record the withdrawal' });
  await expect(confirm).toBeDisabled();
  await row.getByLabel('Why is consent withdrawn?').fill('Patient wants to think it over (synthetic)');
  await confirm.click();
  await expect.poll(() => calls.filter(c => c.url.endsWith('/withdraw')).map(c => c.body)).toEqual([{ reason: 'Patient wants to think it over (synthetic)' }]);
});

test('a root canal across visits: the stage done, the next with its length, mark it on this visit, book the next one filled in (MDV-P8-013)', async ({ page }) => {
  const calls = await open(page);
  const row = page.getByRole('region', { name: 'Treatment plan' }).getByRole('listitem').filter({ hasText: 'Root canal, molar (synthetic)' });
  const stages = row.getByRole('list', { name: 'Stages: 1 of 3 done' });
  await expect(stages.getByRole('listitem')).toHaveText(['Access and cleaning done', 'Next: Shaping (45 min)', 'Obturation (45 min)']);
  await expect(row.getByRole('link', { name: 'Book the next stage (45 min)' })).toHaveAttribute('href', '/schedule?patient=p-1&doctor=u-das&minutes=45&chair=r-1');
  await row.getByRole('button', { name: 'Mark Shaping done on this visit' }).click();
  await expect.poll(() => calls.filter(c => c.url.endsWith('/stages')).map(c => c.body)).toEqual([{ encounterId: 'e-9', stage: 1 }]);
});

test('without an open consultation a finding cannot be added and Start and Mark done wait for one', async ({ page }) => {
  const calls = await open(page, { openEncounterId: null });
  await page.getByRole('button', { name: '36 C·DO, Caries (DO)' }).click();
  await expect(page.getByRole('region', { name: 'Tooth 36' })).toContainText('Start this patient\'s consultation to add one.');
  const plan = page.getByRole('region', { name: 'Treatment plan' });
  await expect(plan.getByRole('button', { name: 'Mark done' })).toBeDisabled();
  await expect(plan.getByRole('button', { name: 'Start' })).toBeDisabled();
  await expect(plan).toContainText('Starting or finishing an item needs this patient\'s open consultation.');
  expect(calls.filter(c => c.method === 'POST')).toEqual([]);
});

test('the plan reads by phase with each state in words; Mark done passes the consultation and the version; declining needs a reason', async ({ page }) => {
  const calls = await open(page);
  const plan = page.getByRole('region', { name: 'Treatment plan' });
  await expect(section(plan, 'Phase 1')).toContainText('Root canal, molar (synthetic)');
  await expect(section(plan, 'Phase 2').getByRole('listitem')).toHaveCount(2);
  await expect(plan).toContainText('Reason: Patient: later, cost (synthetic)');
  await expect(section(plan, 'Estimate')).toContainText(/Total \u2066?₹18,500\.00/); // the amount is isolated left to right (Arabic)
  await plan.getByRole('button', { name: 'Mark done' }).click();
  await expect.poll(() => calls.filter(c => c.url.endsWith('/move')).length).toBe(1);
  expect(calls.find(c => c.url.endsWith('/move'))!.body).toEqual({ to: 'Done', reason: null, encounterId: 'e-9', version: 7 });

  await section(plan, 'Phase 2').getByRole('button', { name: 'Decline' }).click();
  await expect(plan.getByRole('button', { name: 'Decline with this reason' })).toBeDisabled();
  await plan.getByLabel('Why is it declined or postponed?').fill('Patient travelling (synthetic)');
  await plan.getByRole('button', { name: 'Decline with this reason' }).click();
  await expect.poll(() => calls.filter(c => c.url.endsWith('/move')).length).toBe(2);
  expect(calls.filter(c => c.url.endsWith('/move'))[1]!.body).toEqual({ to: 'Declined', reason: 'Patient travelling (synthetic)', encounterId: null, version: 7 });
});

test('someone else moved the item: the plan reloads and says so', async ({ page }) => {
  const calls = await open(page, { move: 409 });
  await page.getByRole('region', { name: 'Treatment plan' }).getByRole('button', { name: 'Mark done' }).click();
  await expect(page.getByRole('region', { name: 'Treatment plan' }).getByRole('alert')).toContainText('Someone changed this item. The plan has been reloaded');
  await expect.poll(() => calls.filter(c => c.method === 'GET' && c.url.endsWith('/dental')).length).toBe(2);
});

test('a plan changed since acceptance says so and records a new acceptance with who and how', async ({ page }) => {
  const calls = await open(page, { acceptanceCurrent: false });
  const estimate = section(page, 'Estimate');
  await expect(estimate).toContainText('Accepted by Farhan Qureshi (synthetic), on the tablet');
  await expect(estimate).toContainText('Changed since accepted: ask the patient to accept again.');
  await estimate.getByLabel('Accepted by (patient or guardian)').fill('Farhan Qureshi (synthetic)');
  await estimate.getByRole('radio', { name: 'printed and signed' }).check();
  await estimate.getByRole('button', { name: 'Record acceptance' }).click();
  await expect.poll(() => calls.filter(c => c.url.endsWith('/acceptance')).length).toBe(1);
  expect(calls.find(c => c.url.endsWith('/acceptance'))!.body).toEqual({ acceptedBy: 'Farhan Qureshi (synthetic)', method: 'PrintedAndSigned' });
});

test('a procedure is added from the catalogue on the picked tooth; an unpriced one cannot be chosen', async ({ page }) => {
  const calls = await open(page);
  await page.getByRole('button', { name: '36 C·DO, Caries (DO)' }).click();
  const plan = page.getByRole('region', { name: 'Treatment plan' });
  await plan.getByLabel('Procedure').click();
  await expect(page.getByRole('option', { name: 'Unpriced item (synthetic) · not priced' })).toBeDisabled();
  await page.getByRole('option', { name: /Root canal, molar \(synthetic\)/ }).click();
  await expect(plan.getByLabel('Tooth')).toHaveValue('36');
  await plan.getByRole('button', { name: 'Add to plan' }).click();
  await expect.poll(() => calls.filter(c => c.url.endsWith('/dental/plan')).length).toBe(1);
  expect(calls.find(c => c.url.endsWith('/dental/plan'))!.body).toEqual({ serviceId: 's-rct', tooth: '36', surfaces: '', phase: 1 });
});

test('the chart and estimate download as a PDF', async ({ page }) => {
  await open(page);
  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Print chart and estimate' }).click();
  expect((await download).suggestedFilename()).toBe('dental-chart-and-estimate.pdf');
});

test('a nurse reads the chart but gets no forms, and without the dental switch the record has no Dental tab', async ({ page }) => {
  await open(page, { notes: 'Read' });
  await page.getByRole('button', { name: '36 C·DO, Caries (DO)' }).click();
  await expect(page.getByRole('region', { name: 'Tooth 36' }).getByRole('radio')).toHaveCount(0);
  await expect(page.getByRole('region', { name: 'Treatment plan' }).getByRole('button', { name: 'Mark done' })).toHaveCount(0);
  await page.unrouteAll();
  await open(page, { flags: [] });
  await expect(page.getByRole('link', { name: 'Dental', exact: true })).toHaveCount(0);
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
for (const locale of ['en', 'ar'] as const)
  test(`axe finds no violations with a tooth picked (${locale}); the chart stays left to right; control-size targets`, async ({ page }) => {
    await page.setViewportSize({ width: 1024, height: 768 });
    await open(page, { locale });
    await page.locator('[data-tooth="36"]').click();
    if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    await expect(page.locator('[data-slot=tooth-chart]')).toHaveAttribute('dir', 'ltr');
    const first = await page.locator('[data-tooth="18"]').boundingBox(), last = await page.locator('[data-tooth="28"]').boundingBox();
    expect(first!.x).toBeLessThan(last!.x); // the patient's right on the viewer's left, in Arabic too
    for (const b of await page.getByRole('main').getByRole('button').all()) {
      if (!(await b.isVisible())) continue;
      expect((await b.boundingBox())!.height).toBeGreaterThanOrEqual(await targetPx(page));
    }
    await page.addScriptTag({ path: axePath });
    const violations = await page.evaluate(async () =>
      (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations.map(v => `${v.id} (${v.nodes.length})`));
    expect(violations).toEqual([]);
  });

test('Settings › Dental: the owner switch, then the dental list loads once and says what it added', async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('medvya.locale', 'en'));
  await page.route('**/v1/me/context', r => r.fulfill({ json: session(['clinical.dental', 'billing.invoices']) }));
  await page.route('**/v1/settings/dental', r => r.fulfill({ json: { enabled: true } }));
  let loads = 0;
  await page.route('**/v1/settings/services/dental-starter', r => { loads++; return r.fulfill({ json: { added: 76, skipped: 0 } }); });
  await page.goto('/settings?tab=dental');
  await expect(page.getByRole('region', { name: 'Dental chart and plan' })).toBeVisible();
  const list = page.getByRole('region', { name: 'Dental services' });
  await list.getByRole('button', { name: 'Load the dental list' }).click();
  await expect(list.getByRole('status')).toContainText('76 services added; 0 were already in your list.');
  expect(loads).toBe(1);
});

test('Settings › Dental: the owner makes the dental template the clinic default for new notes (MDV-P8-015)', async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('medvya.locale', 'en'));
  await page.route('**/v1/me/context', r => r.fulfill({ json: session(['clinical.dental']) }));
  await page.route('**/v1/settings/dental', r => r.fulfill({ json: { enabled: true } }));
  const saved: unknown[] = [];
  await page.route('**/v1/settings/note-templates/', r => {
    if (r.request().method() === 'PUT') { saved.push(r.request().postDataJSON()); return r.fulfill({ json: ['dental'] }); }
    return r.fulfill({ json: ['general-opd'] });
  });
  await page.goto('/settings?tab=dental');
  const card = page.getByRole('region', { name: 'Dental note template' });
  const toggle = card.getByRole('switch', { name: 'Start new notes on the dental template' });
  await expect(toggle).toHaveAttribute('aria-checked', 'false');
  await toggle.click();
  await expect(toggle).toHaveAttribute('aria-checked', 'true');
  expect(saved).toEqual([{ keys: ['dental'] }]);
});
