import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';
import { clearChecks, medicine, mockPrescription, penicillinAlert, type RxLine } from './support/prescription';

// MDV-P2-039 (E-05, E-11): the Add medicine panel from the consultation and from the chart's Medicines card. It stages the line on the
// encounter's draft, so the server's checks and override path cover it; the choices land on the existing sig. CI has no API: endpoints are
// mocked in the server's shapes. The sig codes MORNING and NIGHT are proven in PrescriptionTests, the dose maximum for them in SafetyRulesTests.
const formulary = [
  medicine('fi-ator-10', 'Atorvastatin', '10 mg', 'Tablet', ['Lipitor']), medicine('fi-ator-20', 'Atorvastatin', '20 mg', 'Tablet', ['Lipitor']),
  medicine('fi-amox', 'Amoxicillin', '500 mg', 'Capsule', ['Mox']), medicine('fi-salb', 'Salbutamol', '100 mcg/dose', 'Inhaler'),
];
// Amoxicillin against a recorded penicillin allergy: a severe finding on its own line, as the server's rules give it.
const checksOf = (lines: RxLine[]) => {
  const amox = lines.find(l => l.medicine === 'Amoxicillin' && l.included);
  return clearChecks({ allergies: 'Listed', alerts: amox ? [penicillinAlert(amox.id!)] : [], unresolved: amox ? 1 : 0 });
};

const panel = (page: Page, name = 'Add medicine') => page.getByRole('dialog', { name });
const choose = (p: ReturnType<typeof panel>, group: string, option: string) => p.getByRole('group', { name: group }).getByRole('button', { name: option, exact: true }).click();

test('from the consultation: search, strength, how often, when, how long and instructions build the sig line and its quantity', async ({ page }) => {
  const { calls, lines } = await mockPrescription(page, { formulary, checksOf });
  await page.goto('/consult/e-1');
  await page.getByRole('region', { name: 'Prescription' }).getByRole('button', { name: 'Add medicine' }).click();
  const p = panel(page);
  await expect(p.getByRole('region', { name: 'Patient' })).toContainText('Meera Iyer (synthetic)');
  await expect(p.getByRole('region', { name: 'Patient' })).toContainText('54 years · Female');
  await expect(p.getByRole('region', { name: 'Patient' })).toContainText('Allergies: not recorded yet');

  await p.getByLabel('Search medicine').fill('lipi'); // a brand finds the generic
  await p.getByRole('button', { name: /Atorvastatin · Tablet/ }).click();
  await expect.poll(() => calls.added).toEqual([{ formularyItemId: 'fi-ator-10' }]);
  await choose(p, 'Strength', '20 mg'); // another strength is another formulary row: the first line is switched off, not deleted
  await expect.poll(() => calls.added).toEqual([{ formularyItemId: 'fi-ator-10' }, { formularyItemId: 'fi-ator-20' }]);
  expect(calls.included).toEqual([{ included: false }]);

  await expect(p.getByRole('button', { name: 'Add to prescription' })).toBeDisabled();
  await expect(p.getByText('Choose how often, when and for how long.')).toBeVisible();
  await choose(p, 'How often', '0-0-1 · night');
  await choose(p, 'When', 'Bedtime');
  await choose(p, 'For how long', '1 month');
  await p.getByLabel('Instructions for the patient').fill('Recheck lipids in 6 weeks');
  await expect(p.getByText('Quantity: 30')).toBeVisible();
  await expect.poll(() => calls.saved.at(-1)).toEqual({
    dose: '1', doseUnit: 'tablet', route: 'oral', frequency: 'NIGHT', timing: 'bedtime', durationDays: 30, quantity: 30, instructions: 'Recheck lipids in 6 weeks',
  });
  await expect(p.getByText('Atorvastatin 20 mg · Take 1 tablet oral NIGHT bedtime 30d.')).toBeVisible();
  await expect(p.getByRole('region', { name: 'Safety checks' })).toContainText('No alerts');

  await p.getByRole('button', { name: 'Add to prescription' }).click();
  await expect(p).toBeHidden();
  const added = page.getByRole('region', { name: 'Medicines to prescribe' }).getByRole('row', { name: 'Atorvastatin' });
  await expect(added).toHaveCount(1);
  expect(lines.filter(l => l.included).map(l => l.formularyItemId)).toEqual(['fi-ator-20']);
});

test('a severe finding blocks the line until a reason is given; when needed is one pack; closing without adding switches the line off', async ({ page }) => {
  const { calls, lines } = await mockPrescription(page, { formulary, checksOf });
  await page.goto('/consult/e-1');
  await page.getByRole('button', { name: 'Add medicine' }).click();
  const p = panel(page);
  await p.getByLabel('Search medicine').fill('amox');
  await p.getByRole('button', { name: /Amoxicillin · Capsule/ }).click();
  await choose(p, 'How often', '1-1-1 · three times a day');
  await choose(p, 'When', 'After food');
  await choose(p, 'For how long', '5 days');
  await expect(p.getByText('Quantity: 15')).toBeVisible();
  await expect(p.getByText('A serious finding blocks this medicine.', { exact: false })).toBeVisible();
  await expect(p.getByText(/Severe · /)).toBeVisible(); // the existing SafetyChecks wording, so a new alert kind shows without a change here
  await expect(p.getByRole('button', { name: 'Add to prescription' })).toBeDisabled();

  await p.getByRole('button', { name: 'Give a reason to go ahead' }).click();
  await p.getByLabel('Reason to go ahead').fill('Tolerated amoxicillin in 2024 (synthetic)');
  await p.getByRole('button', { name: 'Record reason' }).click();
  expect(calls.overrides).toEqual([{ key: `allergy:${lines.at(-1)!.id}:al-1`, reason: 'Tolerated amoxicillin in 2024 (synthetic)' }]);
  await expect(p.getByText('A serious finding blocks this medicine.', { exact: false })).toBeHidden();
  await expect(p.getByRole('button', { name: 'Add to prescription' })).toBeEnabled();

  await choose(p, 'How often', 'When needed');
  await expect(p.getByText('Quantity: 1 pack')).toBeVisible();
  await expect.poll(() => calls.saved.at(-1)).toMatchObject({ frequency: 'SOS', quantity: null });
  await p.getByRole('button', { name: 'Cancel' }).click();
  await expect(p).toBeHidden();
  await expect.poll(() => calls.included).toEqual([{ included: false }]);
  expect(lines.filter(l => l.included)).toEqual([]);
});

const chart = (openEncounterId: string | null) => ({
  patient: { id: 'p-1', fullName: 'Meera Iyer (synthetic)', mrn: '000001', dateOfBirth: '1972-03-12', ageYears: 54, sexAtBirth: 'Female', preferredLanguage: null },
  treatmentConsent: true, timeZone: 'Asia/Kolkata', lifeStage: 'Adult', historySections: [], pregnancy: { open: null, ruledOut: null },
  chart: {
    allergyState: 'Listed', allergies: [{ substance: 'Penicillin', reaction: 'Rash', status: 'Confirmed', source: 'ClinicianVerified' }], problems: [],
    medicines: [{ name: 'Metformin', dose: '500 mg', state: 'Active', source: 'Imported', stoppedAt: null }], lastVitals: [], lastLabs: [], bmi: null, trends: [], recentEncounters: [], openEncounterId,
    history: [], bloodGroup: null, careTeam: [],
  },
});

test('from the chart with no open consultation, the panel says one must be started and offers nothing to prescribe', async ({ page }) => {
  const { calls } = await mockPrescription(page, { formulary });
  await page.route('**/v1/patients/p-1/chart', r => r.fulfill({ json: chart(null) }));
  await page.goto('/patients/p-1');
  await page.getByRole('region', { name: 'Medicines', exact: true }).getByRole('button', { name: 'Add medicine' }).click();
  const p = panel(page);
  await expect(p).toContainText('Start a consultation first. A medicine is only prescribed within a consultation.');
  await expect(p.getByRole('region', { name: 'Patient' })).toContainText('Penicillin');
  await expect(p.getByLabel('Search medicine')).toHaveCount(0);
  await expect(p.getByRole('button', { name: 'Add to prescription' })).toHaveCount(0);
  await page.keyboard.press('Escape'); // keyboard path: Escape closes and hands focus back
  await expect(p).toBeHidden();
  await expect(page.getByRole('button', { name: 'Add medicine' })).toBeFocused();
  expect(calls.added).toEqual([]);
});

test('from the chart with an open consultation, the line goes on that consultation\'s draft', async ({ page }) => {
  const { calls } = await mockPrescription(page, { formulary, checksOf });
  await page.route('**/v1/patients/p-1/chart', r => r.fulfill({ json: chart('e-1') }));
  await page.goto('/patients/p-1');
  await page.getByRole('button', { name: 'Add medicine' }).click();
  const p = panel(page);
  await p.getByLabel('Search medicine').fill('salb');
  await p.getByRole('button', { name: /Salbutamol · Inhaler/ }).click();
  await choose(p, 'How often', '1-1-1 · three times a day');
  await choose(p, 'When', 'Any time');
  await choose(p, 'For how long', 'Ongoing');
  await expect(p.getByText('Quantity: 1 pack')).toBeVisible(); // an inhaler is not counted in tablets
  await expect.poll(() => calls.saved.at(-1)).toEqual({ dose: null, doseUnit: null, route: null, frequency: 'TDS', timing: null, durationDays: null, quantity: null, instructions: null });
  await p.getByRole('button', { name: 'Add to prescription' }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Salbutamol is on the draft prescription of the open consultation.' })).toBeVisible();
  expect(calls.added).toEqual([{ formularyItemId: 'fi-salb' }]);
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
const axe = async (page: Page) => {
  await page.addScriptTag({ path: axePath });
  return page.evaluate(async () =>
    (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations.map(v => `${v.id} (${v.nodes.length})`));
};

// MDV-P1-088: the clinic gave the generic's row a brand name (Augmentin is the ticket's example pair, a fixture, not a reviewed list).
const withBrand = [...formulary, medicine('fi-coamox', 'Amoxicillin and clavulanate', '625 mg', 'Tablet', ['Augmentin'])];
// The server checks the generic's row whichever name found it (RxAuthorisationTests proves the alert and the refusal to authorise).
const brandChecksOf = (lines: RxLine[]) => {
  const line = lines.find(l => l.formularyItemId === 'fi-coamox' && l.included);
  return clearChecks({ allergies: 'Listed', alerts: line ? [{ ...penicillinAlert(line.id!), medicine: 'Amoxicillin and clavulanate' }] : [], unresolved: line ? 1 : 0 });
};

test('a search by brand offers the generic\'s row with the brand shown, and a penicillin allergy blocks it as it does the generic', async ({ page }) => {
  const { calls } = await mockPrescription(page, { formulary: withBrand, checksOf: brandChecksOf });
  await page.goto('/consult/e-1');
  await page.getByRole('region', { name: 'Prescription' }).getByRole('button', { name: 'Add medicine' }).click();
  const p = panel(page);
  await p.getByLabel('Search medicine').fill('Augmentin');
  const row = p.getByRole('list', { name: 'Medicines found' }).getByRole('button');
  await expect(row).toHaveText(/Amoxicillin and clavulanate · Tablet.*Brands: Augmentin/);
  await p.getByLabel('Search medicine').fill('gmentin'); // never inside a word
  await expect(p.getByText('No medicine found in the formulary.')).toBeVisible();
  await p.getByLabel('Search medicine').fill('augm');
  await row.click();
  await expect.poll(() => calls.added).toEqual([{ formularyItemId: 'fi-coamox' }]); // the generic's own formulary row, so its checks
  await choose(p, 'How often', '1-0-1 · twice a day');
  await choose(p, 'When', 'After food');
  await choose(p, 'For how long', '5 days');
  await expect(p.getByText('A serious finding blocks this medicine.', { exact: false })).toBeVisible();
  await expect(p.getByText(/Severe · /)).toBeVisible();
  await expect(p.getByRole('button', { name: 'Add to prescription' })).toBeDisabled();
});

for (const locale of ['en', 'ar'])
  test(`a name that matches nothing is answered with the closest formulary rows, with no axe violation (${locale})`, async ({ page }) => {
    const { calls } = await mockPrescription(page, { locale, formulary: withBrand, checksOf: brandChecksOf });
    await page.goto('/consult/e-1');
    const ar = locale === 'ar';
    await page.getByRole('button', { name: ar ? 'إضافة دواء' : 'Add medicine' }).click();
    const p = panel(page, ar ? 'إضافة دواء' : 'Add medicine');
    await p.getByLabel(ar ? 'ابحث عن دواء' : 'Search medicine').fill('Augmentn');
    await expect(p.getByText(ar ? 'لم يُعثر على دواء في قائمة الأدوية.' : 'No medicine found in the formulary.')).toBeVisible();
    const suggested = p.getByRole('list', { name: ar ? 'هل تقصد' : 'Did you mean' }).getByRole('button');
    await expect(suggested).toHaveText(/Amoxicillin and clavulanate/); // the one closest row; Amoxicillin shares too little
    expect(await axe(page)).toEqual([]);
    await suggested.click(); // a suggestion is a formulary row: it is staged and checked as any other
    await expect.poll(() => calls.added).toEqual([{ formularyItemId: 'fi-coamox' }]);
    await expect(p.getByRole('group', { name: ar ? 'التركيز' : 'Strength' }).getByRole('button')).toHaveText('625 mg');
  });

test('in the composer the closest formulary rows are offered above Add as free text', async ({ page }) => {
  const { calls } = await mockPrescription(page, { formulary: withBrand, checksOf: brandChecksOf });
  await page.goto('/consult/e-1');
  const rx = page.getByRole('region', { name: 'Prescription' });
  await rx.getByLabel('Medicine', { exact: true }).fill('Amoxicilin'); // one letter short
  const suggested = rx.getByRole('list', { name: 'Did you mean' }).getByRole('button');
  await expect(suggested).toHaveText([/Amoxicillin 500 mg Capsule/, /Amoxicillin and clavulanate 625 mg Tablet/]);
  const freeText = rx.getByRole('button', { name: 'Add as free text' });
  expect((await suggested.last().boundingBox())!.y).toBeLessThan((await freeText.boundingBox())!.y);
  await suggested.first().click();
  await expect.poll(() => calls.added).toEqual([{ formularyItemId: 'fi-amox' }]);
});
for (const locale of ['en', 'ar'])
  test(`axe finds no violations in the Add medicine panel with a blocking finding (${locale})`, async ({ page }) => {
    await mockPrescription(page, { locale, formulary, checksOf });
    await page.goto('/consult/e-1');
    const ar = locale === 'ar';
    await page.getByRole('button', { name: ar ? 'إضافة دواء' : 'Add medicine' }).click();
    const p = panel(page, ar ? 'إضافة دواء' : 'Add medicine');
    if (ar) await expect(p).toHaveAttribute('dir', 'rtl');
    await p.getByLabel(ar ? 'ابحث عن دواء' : 'Search medicine').fill('amox');
    await p.getByRole('button', { name: /Amoxicillin/ }).click();
    await choose(p, ar ? 'كم مرة' : 'How often', ar ? '1-0-1 · مرتين يوميًا' : '1-0-1 · twice a day');
    await expect(p.getByText(ar ? 'نتيجة خطيرة تمنع هذا الدواء' : 'A serious finding blocks this medicine', { exact: false })).toBeVisible();
    await p.getByRole('button', { name: ar ? 'اذكر سبب المتابعة' : 'Give a reason to go ahead' }).click();
    await page.addScriptTag({ path: axePath });
    const violations = await page.evaluate(async () =>
      (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations.map(v => `${v.id} (${v.nodes.length})`));
    expect(violations).toEqual([]);
  });
