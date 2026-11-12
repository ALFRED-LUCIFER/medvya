import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';

// MDV-P2-037: the older adult review on Patient 360 (E-01, design `geri` and `deprescribe`). CI has no API: the session, chart and review are
// mocked in the shapes the server sends. The scales, the flag lines, the care relationship, row-level security and the audit rows are covered by
// OlderAdultRulesTests and OlderAdultReviewTests on PostgreSQL.
const session = {
  user: { id: 'u-rao', displayName: 'Dr. Arjun Rao (synthetic)' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role: 'SoloDoctor' }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role: 'SoloDoctor', flags: [] as string[], country: 'IN',
    capabilities: { RegisterBookPatients: 'Read', CheckInQueue: 'Full', Patient360Chart: 'Full' } },
};

const chart = (ageYears: number, openEncounterId: string | null = null) => ({
  patient: { id: 'p-1', fullName: 'Savitri Krishnan (synthetic)', mrn: '000873', dateOfBirth: '1950-06-22', ageYears, sexAtBirth: 'Female', preferredLanguage: null },
  treatmentConsent: true, timeZone: 'Asia/Kolkata', lifeStage: ageYears >= 65 ? 'OlderAdult' : 'Adult', historySections: ['Function', 'Falls'],
  chart: {
    allergyState: 'NoKnownAllergies', allergies: [], problems: [], medicines: [{ name: 'Alprazolam 0.25 mg', dose: null, state: 'Active', source: 'ClinicianVerified', stoppedAt: null }],
    lastVitals: [], trends: [], recentEncounters: [], openEncounterId, lastLabs: [], bmi: null, bloodGroup: null, careTeam: [], history: [],
  },
});

const assessment = {
  id: 'g-1', assessedAt: '2026-11-05T04:30:00Z', author: 'Nurse S. Thomas (synthetic)', fallsLast12Months: 1, timedUpAndGoSeconds: 14.2, tugAtRisk: true,
  lyingSystolic: 146, lyingDiastolic: 80, standingSystolic: 122, standingDiastolic: 72, systolicDrop: 24, diastolicDrop: 8, orthostatic: true,
  clinicalFrailtyScale: 4, miniCog: 4, adl: 6, iadl: 5, phq2: 1, nutritionNote: 'Lost 3.5 kg in a year (synthetic)',
};

const review = {
  suggestions: [
    { medicineId: 'ms-1', medicine: 'Alprazolam 0.25 mg', rule: 'Benzodiazepine', weeksOnChart: null, egfr: null, potassiumHundredths: null },
    { medicineId: 'ms-2', medicine: 'Pantoprazole 40 mg', rule: 'LongTermPpi', weeksOnChart: 12, egfr: null, potassiumHundredths: null },
    { medicineId: 'ms-3', medicine: 'Losartan 50 mg', rule: 'RaasLowEgfrHighPotassium', weeksOnChart: null, egfr: 52, potassiumHundredths: 540 },
  ],
  notChecked: [{ medicineId: 'ms-4', medicine: 'Synthetic herbal tonic', reason: 'NotOnFormulary' }], medicines: 4, olderAdult: true, rulesSignedOff: false,
};

async function open(page: Page, { locale = 'en', ageYears = 76, openEncounterId = null as string | null, reviewStatus = 200 } = {}) {
  const calls: { method: string; url: string; body: unknown }[] = [];
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: session }));
  await page.route('**/v1/patients/p-1/chart', r => r.fulfill({ json: chart(ageYears, openEncounterId) }));
  await page.route('**/v1/patients/p-1/geriatric-assessments', r => {
    calls.push({ method: r.request().method(), url: r.request().url(), body: r.request().postDataJSON() });
    return r.request().method() === 'POST' ? r.fulfill({ json: { ...assessment, id: 'g-2' } }) : r.fulfill({ json: { assessments: [assessment], timeZone: 'Asia/Kolkata', rulesSignedOff: false } });
  });
  await page.route('**/v1/patients/p-1/medicine-review', r => {
    calls.push({ method: r.request().method(), url: r.request().url(), body: null });
    return reviewStatus === 200 ? r.fulfill({ json: review }) : r.fulfill({ status: reviewStatus, json: { code: 'no_access' } });
  });
  // Anything that could change a medicine: the review must never call it.
  await page.route(/\/v1\/(encounters|prescription-items|intake)\//, r => { calls.push({ method: r.request().method(), url: r.request().url(), body: null }); return r.fulfill({ status: 500, json: {} }); });
  await page.goto('/patients/p-1');
  return calls;
}

test('the latest review shows each flag in words with its line, and the rules are a draft', async ({ page }) => {
  await open(page);
  const card = page.getByRole('region', { name: 'Geriatric assessment' });
  await expect(card).toContainText(/Updated 05 Nov 2026 · Nurse S. Thomas/);
  await expect(card.getByRole('listitem').filter({ hasText: 'Standing blood pressure' })).toContainText('146/80 → 122/72 · drop 24/8 mmHg · orthostatic');
  await expect(card.getByRole('listitem').filter({ hasText: 'Timed Up and Go' })).toContainText('14.2 s · 12 s or more: higher fall risk');
  await expect(card.getByRole('listitem').filter({ hasText: 'Clinical Frailty Scale' })).toContainText('4 of 9');
  await expect(card).toContainText('Lost 3.5 kg in a year');
  await expect(card).toContainText('are a draft awaiting clinical review');
});

test('the medicine review only suggests: "plan change" changes nothing and says to start a consultation', async ({ page }) => {
  const calls = await open(page);
  const card = page.getByRole('region', { name: 'Medicine review · 4 medicines' });
  await expect(card).toContainText('Suggestions, the doctor decides');
  await expect(card.getByRole('listitem').filter({ hasText: 'Pantoprazole' })).toContainText('for more than 8 weeks (about 12)');
  await expect(card.getByRole('listitem').filter({ hasText: 'Losartan' })).toContainText(/eGFR .?52.? and potassium .?5.4.? mmol\/L/);
  await expect(card).toContainText('Synthetic herbal tonic: not reviewed');
  await card.getByRole('button', { name: 'Plan change: Alprazolam 0.25 mg' }).click();
  await expect(card.getByRole('status')).toContainText('Nothing has been changed.');
  expect(calls.filter(c => c.method !== 'GET')).toEqual([]);
});

test('with an open consultation "plan change" opens it', async ({ page }) => {
  await open(page, { openEncounterId: 'e-9' });
  await page.getByRole('link', { name: 'Plan change: Losartan 50 mg' }).click();
  await expect(page).toHaveURL(/\/consult\/e-9#studio-plan$/); // the Plan of the one page (MDV-P2-043)
});

test('a new assessment refuses a score outside its scale, then saves as a new record with blanks as null', async ({ page }) => {
  const calls = await open(page);
  await page.getByRole('button', { name: 'New assessment' }).click();
  const form = page.getByRole('form', { name: 'New geriatric assessment' });
  await form.getByLabel('Clinical Frailty Scale (1–9)').fill('10');
  await form.getByRole('button', { name: 'Save assessment' }).click();
  await expect(form.getByRole('alert').first()).toContainText('Outside the scale.');
  expect(calls.filter(c => c.method === 'POST')).toEqual([]);
  await form.getByLabel('Clinical Frailty Scale (1–9)').fill('5');
  await form.getByLabel('Timed Up and Go (seconds)').fill('13,5');
  await form.getByRole('button', { name: 'Save assessment' }).click();
  await expect(form).toHaveCount(0);
  const posted = calls.filter(c => c.method === 'POST');
  expect(posted).toHaveLength(1);
  expect(posted[0]!.body).toMatchObject({ clinicalFrailtyScale: 5, timedUpAndGoSeconds: 13.5, miniCog: null, lyingSystolic: null, nutritionNote: null });
});

test('an adult (not the older adult life stage) has no older adult cards, and a nurse sees no medicine review', async ({ page }) => {
  const calls = await open(page, { ageYears: 54 });
  await expect(page.getByRole('region', { name: 'Medicines', exact: true })).toBeVisible();
  await expect(page.getByRole('region', { name: 'Geriatric assessment' })).toHaveCount(0);
  expect(calls).toEqual([]);
  await page.unrouteAll();
  await open(page, { reviewStatus: 403 });
  await expect(page.getByRole('region', { name: 'Geriatric assessment' })).toBeVisible();
  await expect(page.getByRole('region', { name: /Medicine review/ })).toHaveCount(0);
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
for (const locale of ['en', 'ar'] as const)
  test(`axe finds no violations with the form open (${locale}); 44 px targets`, async ({ page }) => {
    await page.setViewportSize({ width: 1024, height: 768 });
    await open(page, { locale });
    const record = page.getByRole('button', { name: locale === 'ar' ? 'تقييم جديد' : 'New assessment' });
    await record.click();
    if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    for (const b of await page.getByRole('main').getByRole('button').all()) expect((await b.boundingBox())!.height).toBeGreaterThanOrEqual(44);
    await page.addScriptTag({ path: axePath });
    const violations = await page.evaluate(async () =>
      (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations.map(v => `${v.id} (${v.nodes.length})`));
    expect(violations).toEqual([]);
  });
