import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';

// MDV-P2-038: chronic care targets, care gaps due today and care-plan goals in the chart's right column (E-01, design `targets`, `gaps`,
// `goals`). CI has no API: the session, chart and chronic care are mocked in the shapes the server sends. The gap rules (due, not due, done),
// targets, validation, the care relationship, row-level security and audit rows are covered by ChronicCareRulesTests and ChronicCareTests.
const session = (role = 'SoloDoctor') => ({
  user: { id: 'u-rao', displayName: 'Dr. Arjun Rao (synthetic)' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role, flags: [] as string[], country: 'IN',
    capabilities: role === 'SoloDoctor' ? { RegisterBookPatients: 'Read', CheckInQueue: 'Full', Patient360Chart: 'Full', PrescriptionsOrders: 'Sign', VitalsAllergiesEntry: 'Full' }
      : { CheckInQueue: 'Full', Patient360Chart: 'Limited', VitalsAllergiesEntry: 'Full' } },
});

const chart = (openEncounterId: string | null) => ({
  patient: { id: 'p-1', fullName: 'Meera Iyer (synthetic)', mrn: '000001', dateOfBirth: '1972-03-12', ageYears: 54, sexAtBirth: 'Female', preferredLanguage: null },
  treatmentConsent: true, timeZone: 'Asia/Kolkata', lifeStage: 'Adult', historySections: [], pregnancy: { open: null, ruledOut: null },
  chart: {
    allergyState: 'NoKnownAllergies', allergies: [], problems: [{ icd10Code: 'E11.9', description: 'Type 2 diabetes mellitus', verification: 'Verified', onsetOn: null, source: 'ClinicianVerified' }],
    medicines: [], lastVitals: [], trends: [], recentEncounters: [], openEncounterId, lastLabs: [], bmi: null, bloodGroup: null, careTeam: [], history: [],
  },
});

const target = (code: string, unit: string, direction: string, limit: number, latest: number | null, met: boolean | null) =>
  ({ code, unit, direction, limit, isDefault: true, latest, latestAt: latest === null ? null : '2026-09-28T04:30:00Z', met, setById: null, setAt: null, setBy: null });
const gap = (rule: string, action: string, item: string, over: Record<string, unknown> = {}) =>
  ({ rule, action, item, lastDone: null, since: null, vaccine: null, dose: null, referTo: null, ...over });
const care = {
  chronic: true, rulesSignedOff: false, timeZone: 'Asia/Kolkata',
  targets: [target('HbA1c', '%', 'Below', 7, 7.9, false), target('BloodPressureSystolic', 'mmHg', 'Below', 130, 124, true), target('BloodPressureDiastolic', 'mmHg', 'Below', 80, 80, false),
    target('LdlCholesterol', 'mg/dL', 'Below', 100, 122, false), target('Egfr', 'mL/min/1.73 m²', 'AtLeast', 60, 72, true), target('UrineAlbuminCreatinineRatio', 'mg/g', 'Below', 30, null, null)],
  gaps: [gap('DiabeticEyeExam', 'Refer', 'Annual diabetic eye check', { referTo: 'Ophthalmology', lastDone: '2025-03-02' }),
    gap('DiabeticFootExam', 'AddToVisit', 'Annual diabetic foot check'),
    gap('UrineAlbumin', 'Order', 'Urine albumin-creatinine ratio'),
    gap('OrderNotResulted', 'Order', 'Lipid profile', { since: '2026-07-14' })],
  goals: [{ id: 'g-1', text: 'HbA1c below 7 % (synthetic)', target: 'Review in 3 months', progressPercent: 35, createdById: 'u-rao', createdAt: '2026-07-12T04:30:00Z',
    updatedById: 'u-rao', updatedAt: '2026-09-28T04:30:00Z', createdBy: 'Dr. Arjun Rao (synthetic)', updatedBy: 'Dr. Arjun Rao (synthetic)' }],
};
const note = { encounterId: 'e-9', templateKey: 'diabetes-review', templateVersion: 2, status: 'Draft', manual: true, sections: { plan: 'Review in 3 months' }, version: 4, updatedAt: null, recordingConsent: 'Agreed' };

async function open(page: Page, { locale = 'en', role = 'SoloDoctor', openEncounterId = 'e-9' as string | null } = {}) {
  const calls: { method: string; url: string; body: unknown }[] = [];
  const log = (r: import('@playwright/test').Route) => calls.push({ method: r.request().method(), url: new URL(r.request().url()).pathname, body: r.request().postDataJSON() });
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: session(role) }));
  await page.route('**/v1/patients/p-1/chart', r => r.fulfill({ json: chart(openEncounterId) }));
  await page.route('**/v1/patients/p-1/timeline*', r => r.fulfill({ json: { entries: [], next: null, timeZone: 'Asia/Kolkata' } }));
  await page.route('**/v1/patients/p-1/documents', r => r.fulfill({ json: { documents: [], timeZone: 'Asia/Kolkata' } }));
  await page.route('**/v1/patients/p-1/referrals', r => r.fulfill({ json: [] }));
  await page.route('**/v1/patients/p-1/chronic-care', r => { log(r); return r.fulfill({ json: care }); });
  await page.route('**/v1/patients/p-1/care-*', r => { log(r); return r.request().method() === 'POST' && r.request().url().endsWith('care-checks') ? r.fulfill({ status: 204 }) : r.fulfill({ json: care.goals[0] }); });
  await page.route('**/v1/encounters/e-9/**', r => { log(r); return r.request().method() === 'GET' ? r.fulfill({ json: note }) : r.fulfill({ json: r.request().url().includes('/note') ? note : [] }); });
  await page.goto('/patients/p-1');
  return calls;
}

test('each latest value against its target in words, and the rules are a draft', async ({ page }) => {
  await open(page);
  const card = page.getByRole('region', { name: 'Chronic care targets' });
  await expect(card.getByRole('listitem').filter({ hasText: 'HbA1c' })).toContainText(/Not on target.*Latest .?7.9.? % · 28 Sept? 2026.*Target below .?7.? % · default/s);
  await expect(card.getByRole('listitem').filter({ hasText: 'eGFR' })).toContainText(/On target.*at least/s);
  await expect(card.getByRole('listitem').filter({ hasText: 'Urine albumin' })).toContainText('No reading');
  await expect(card).toContainText('are a draft awaiting clinical review');
});

test('a gap goes on the open consultation: an order, a referral, or a line in the plan', async ({ page }) => {
  const calls = await open(page);
  const gaps = page.getByRole('region', { name: 'Due today and care gaps' });
  await expect(gaps.getByRole('listitem').filter({ hasText: 'Diabetic eye check' })).toContainText('Last recorded 02 Mar 2025');
  await expect(gaps.getByRole('listitem').filter({ hasText: 'Lipid profile' })).toContainText('Ordered 14 Jul 2026');

  await gaps.getByRole('button', { name: 'Order: Urine albumin (UACR)' }).click();
  await expect(gaps.getByRole('listitem').filter({ hasText: 'Urine albumin' }).getByRole('status')).toContainText("Added to today's consultation.");
  await gaps.getByRole('button', { name: 'Refer: Diabetic eye check' }).click();
  await expect(gaps.getByRole('listitem').filter({ hasText: 'Diabetic eye check' }).getByRole('status')).toContainText('Added');
  await gaps.getByRole('button', { name: 'Add to visit: Diabetic foot check' }).click();
  await expect(gaps.getByRole('listitem').filter({ hasText: 'Diabetic foot check' }).getByRole('status')).toContainText('Added');

  const writes = calls.filter(c => c.method !== 'GET');
  expect(writes).toEqual([
    { method: 'POST', url: '/v1/encounters/e-9/orders', body: { kind: 'Lab', name: 'Urine albumin-creatinine ratio', note: null } },
    { method: 'POST', url: '/v1/encounters/e-9/referrals', body: { provider: 'Ophthalmology', urgency: 'Routine', reason: 'Annual diabetic eye check', sections: [] } },
    { method: 'PUT', url: '/v1/encounters/e-9/note', body: expect.objectContaining({ baseVersion: 4, templateKey: 'diabetes-review', sections: { plan: 'Review in 3 months\nAnnual diabetic foot check' } }) },
  ]);
});

test('with no open consultation a gap action says so and calls nothing', async ({ page }) => {
  const calls = await open(page, { openEncounterId: null });
  const gaps = page.getByRole('region', { name: 'Due today and care gaps' });
  await gaps.getByRole('button', { name: 'Add to visit: Diabetic foot check' }).click();
  await expect(gaps.getByRole('status')).toContainText('No consultation is open');
  expect(calls.filter(c => c.method !== 'GET')).toEqual([]);
});

test('the doctor records a check, changes a target and keeps goals; a nurse only reads', async ({ page }) => {
  const calls = await open(page);
  const gaps = page.getByRole('region', { name: 'Due today and care gaps' });
  await gaps.getByRole('button', { name: 'Record as done: Diabetic foot check' }).click();
  await gaps.getByLabel('Done on').fill('2026-11-01');
  await gaps.getByRole('button', { name: 'Save' }).click();
  await expect(gaps.getByRole('form')).toHaveCount(0);

  const targets = page.getByRole('region', { name: 'Chronic care targets' });
  await targets.getByRole('button', { name: 'Change target: HbA1c' }).click();
  await targets.getByLabel('Target for HbA1c (%)').fill('7,5');
  await targets.getByRole('button', { name: 'Save target' }).click();
  await expect(targets.getByRole('form')).toHaveCount(0);

  const goals = page.getByRole('region', { name: 'Care plan goals' });
  await expect(goals.getByRole('progressbar', { name: 'HbA1c below 7 % (synthetic)' })).toHaveAttribute('aria-valuenow', '35');
  await expect(goals).toContainText(/35.? % · set by Dr. Arjun Rao/);
  await goals.getByRole('button', { name: 'Add goal' }).click();
  await goals.getByRole('button', { name: 'Save goal' }).click();
  await expect(goals.getByRole('alert').first()).toContainText('Required.');
  await goals.getByLabel('Goal', { exact: true }).fill('Walk 150 min a week (synthetic)');
  await goals.getByLabel('Progress (0–100 %)').fill('40');
  await goals.getByRole('button', { name: 'Save goal' }).click();
  await expect(goals.getByRole('form')).toHaveCount(0);
  expect(calls.filter(c => c.method !== 'GET')).toEqual([
    { method: 'POST', url: '/v1/patients/p-1/care-checks', body: { kind: 'DiabeticFootExam', doneOn: '2026-11-01' } },
    { method: 'PUT', url: '/v1/patients/p-1/care-targets', body: { code: 'HbA1c', limit: 7.5 } },
    { method: 'POST', url: '/v1/patients/p-1/care-goals', body: { text: 'Walk 150 min a week (synthetic)', target: null, progressPercent: 40 } },
  ]);

  await page.unrouteAll();
  await open(page, { role: 'Nurse' });
  await expect(page.getByRole('region', { name: 'Due today and care gaps' })).toContainText('Diabetic foot check');
  await expect(page.getByRole('complementary').getByRole('button')).toHaveCount(0);
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
for (const locale of ['en', 'ar'] as const)
  test(`axe finds no violations with the forms open (${locale}); 44 px targets`, async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 900 });
    await open(page, { locale });
    const aside = page.getByRole('complementary');
    await aside.getByRole('button').nth(0).click(); // change the first target
    await aside.getByRole('button', { name: locale === 'ar' ? 'إضافة هدف' : 'Add goal' }).click();
    if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    for (const b of await aside.getByRole('button').all()) expect((await b.boundingBox())!.height).toBeGreaterThanOrEqual(44);
    await page.addScriptTag({ path: axePath });
    const violations = await page.evaluate(async () =>
      (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations.map(v => `${v.id} (${v.nodes.length})`));
    expect(violations).toEqual([]);
  });
