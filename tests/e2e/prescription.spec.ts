import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';
import { clearChecks, line, mockPrescription, penicillinAlert, stopLine } from './support/prescription';

// MDV-P2-015 (E-05, E-11): the doctor adds a formulary medicine and builds its dose, which autosaves and shows the instruction in the chosen
// print language; a free-text medicine is flagged; a reconciliation stop is a discontinue line. CI has no API: endpoints are mocked in the
// shapes the server sends. The readable instruction per language, the audit rows, access and the tenant are proven in PrescriptionTests.
const card = (page: Page, name: string) => page.getByRole('list', { name: 'Medicines to prescribe' }).getByRole('listitem', { name });
const pick = async (page: Page, scope: ReturnType<typeof card>, label: string, option: string) => {
  await scope.getByRole('combobox', { name: label }).click();
  await page.getByRole('option', { name: option, exact: true }).click();
};

test('a formulary medicine gets a dose that autosaves and reads in the chosen print language; nothing is authorised', async ({ page }) => {
  const { calls } = await mockPrescription(page);
  await page.goto('/consult/e-1');
  const rx = page.getByRole('region', { name: 'Prescription' });
  await expect(rx.getByText('No medicines added yet.')).toBeVisible();
  await expect(rx).toContainText('Draft, not authorised');

  await rx.getByLabel('Medicine', { exact: true }).fill('metf');
  await rx.getByRole('button', { name: 'Add Metformin 500 mg Tablet' }).click();
  expect(calls.added).toEqual([{ formularyItemId: 'fi-metformin' }]);
  const metformin = card(page, 'Metformin');
  await expect(metformin).toContainText('Added by you');
  await expect(metformin).toContainText('Fill in the dose to build the instruction.');

  await metformin.getByLabel('Dose').fill('1');
  await pick(page, metformin, 'Unit', 'Tablet');
  await pick(page, metformin, 'Route', 'By mouth');
  await pick(page, metformin, 'How often', 'Twice a day (BD)');
  await pick(page, metformin, 'When', 'After food');
  await metformin.getByLabel('Days').fill('5');
  await metformin.getByLabel('Quantity').fill('10');
  await expect(metformin.getByText('Saved')).toBeVisible();
  expect(calls.saved.at(-1)).toEqual({ dose: '1', doseUnit: 'tablet', route: 'oral', frequency: 'BD', timing: 'after_food', durationDays: 5, quantity: 10, instructions: null });
  await expect(metformin).toContainText('Instruction (English):');
  await expect(metformin.locator('[lang="en"]')).toHaveText('Take 1 tablet oral BD after_food 5d.');

  await pick(page, rx, 'Print language', 'Arabic');
  const arabic = metformin.locator('[lang="ar"]');
  await expect(arabic).toHaveText('تناول 1 tablet oral BD after_food 5d.');
  await expect(arabic).toHaveAttribute('dir', 'rtl');
  // Authorising is the doctor's own, separate action (MDV-P2-017): building the draft never authorises it.
  await expect(rx.getByRole('button', { name: 'Authorise prescription' })).toBeVisible();
  expect(calls.authorised).toBe(0);
  await expect(rx.getByRole('link', { name: 'Print prescription' })).toHaveCount(0);
});

test('an invalid dose is named and not saved; a switched-off line stays listed', async ({ page }) => {
  const { calls } = await mockPrescription(page, { lines: [line('rx-1', 'Metformin')] });
  await page.goto('/consult/e-1');
  const metformin = card(page, 'Metformin');
  await metformin.getByLabel('Dose').fill('0');
  await expect(metformin.getByText('Enter a number above 0, for example 1 or 0.5.')).toBeVisible();
  await metformin.getByLabel('Days').fill('5'); // held back too: the line still has an invalid dose
  await metformin.getByLabel('Dose').fill('2');
  await expect(metformin.getByText('Saved')).toBeVisible();
  expect(calls.saved).toEqual([{ dose: '2', doseUnit: null, route: null, frequency: null, timing: null, durationDays: 5, quantity: null, instructions: null }]);

  await metformin.getByRole('switch', { name: 'Include Metformin' }).click();
  expect(calls.included).toEqual([{ included: false }]);
  await expect(metformin).toContainText('Not included');
  await expect(metformin.getByLabel('Dose')).toBeDisabled();
});

test('a free-text medicine is flagged and a reconciliation stop is a discontinue line', async ({ page }) => {
  const { calls } = await mockPrescription(page, { lines: [stopLine('Atorvastatin', 'Muscle pain (synthetic)')] });
  await page.goto('/consult/e-1');
  const stops = page.getByRole('list', { name: 'Medicines to stop' });
  await expect(stops).toContainText('Stop');
  await expect(stops).toContainText('Atorvastatin');
  await expect(stops).toContainText('Chart reconciliation');
  await expect(stops).toContainText('Reason: Muscle pain (synthetic)');
  await expect(stops).toContainText('Stop this medicine.');

  const rx = page.getByRole('region', { name: 'Prescription' });
  await rx.getByLabel('Medicine', { exact: true }).fill('Herbal tonic X');
  await rx.getByRole('button', { name: 'Add as free text' }).click();
  expect(calls.added).toEqual([{ medicine: 'Herbal tonic X' }]);
  await expect(card(page, 'Herbal tonic X')).toContainText('Free-text medicine, not from the formulary. Review it: it is left out of interaction checks.');
});

test('an ended consultation shows the draft and cannot change it; a role that may not prescribe sees no panel', async ({ page }) => {
  await mockPrescription(page, { ended: true, lines: [line('rx-1', 'Metformin')] });
  await page.goto('/consult/e-1');
  await expect(page.getByText('This consultation has ended. The draft prescription can be read, not changed.')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Add as free text' })).toHaveCount(0);
  await expect(card(page, 'Metformin').getByLabel('Dose')).toBeDisabled();

  await page.unrouteAll({ behavior: 'ignoreErrors' });
  await mockPrescription(page, { allowed: false });
  await page.reload();
  await expect(page.getByRole('region', { name: 'Patient identity' })).toBeVisible();
  await expect(page.getByRole('region', { name: 'Prescription' })).toHaveCount(0);
});

// MDV-P2-016 (E-11): the checks' result in words with its evidence; a severe alert takes a reason; what was not checked is said plainly.
test('a severe allergy alert is in words with its evidence and needs a reason; what was not checked is named', async ({ page }) => {
  const { calls } = await mockPrescription(page, {
    lines: [line('rx-1', 'Amoxicillin'), line('rx-2', 'Herbal tonic X', { formularyItemId: null, strength: null, form: null, freeText: true })],
    checks: clearChecks({ allergies: 'Listed', unresolved: 1, alerts: [penicillinAlert('rx-1')], notChecked: [{ id: 'rx-2', medicine: 'Herbal tonic X', onChart: false, reason: 'FreeText' }] }),
  });
  await page.goto('/consult/e-1');
  const checks = page.getByRole('region', { name: 'Safety checks' });
  await expect(checks).toContainText('Allergies: listed on the chart');
  const alert = checks.getByRole('list', { name: 'Safety alerts' }).getByRole('alert');
  await expect(alert).toContainText('Severe · Allergy: Penicillin · verified. Amoxicillin: penicillin class.');
  await expect(checks).toContainText('Severe alerts without a reason: 1.');
  await expect(card(page, 'Amoxicillin')).toContainText('Safety: severe alert');
  await expect(card(page, 'Herbal tonic X')).toContainText('Safety: not fully checked');
  await expect(checks).toContainText('Herbal tonic X: typed as free text, so not checked for allergies, interactions or dose.');

  await checks.getByRole('button', { name: 'Give a reason to go ahead' }).click();
  await checks.getByRole('button', { name: 'Record reason' }).click();
  await expect(checks.getByText('Give a reason, up to 200 characters.')).toBeVisible();
  expect(calls.overrides).toEqual([]);
  await checks.getByLabel('Reason to go ahead').fill('Tolerated amoxicillin before (synthetic)');
  await checks.getByRole('button', { name: 'Record reason' }).click();
  await expect(checks).toContainText('Overridden with reason: Tolerated amoxicillin before (synthetic)');
  expect(calls.overrides).toEqual([{ key: 'allergy:rx-1:al-1', reason: 'Tolerated amoxicillin before (synthetic)' }]);
  await expect(card(page, 'Amoxicillin')).toContainText('Safety: overridden with reason');
  await expect(checks).not.toContainText('Severe alerts without a reason');

  await checks.getByText('About these checks').click();
  await expect(checks).toContainText("Source: Medvya's curated starter rules, not a licensed drug database, pending clinical safety sign-off.");
  await expect(checks).toContainText('Version: 2026.11-draft-1');
  await expect(checks).toContainText('Age, weight and kidney function are not taken into account.');
});

test('the checks run again after a change, and "not recorded" allergies are said, never read as none', async ({ page }) => {
  const { calls } = await mockPrescription(page, { lines: [line('rx-1', 'Metformin')], checks: clearChecks({ allergies: 'NotRecorded', alerts: [{
    key: 'allergies:not_recorded', kind: 'AllergiesNotRecorded', severity: 'Moderate', itemId: null, medicine: null, other: null, otherOnChart: false, code: null,
    substance: null, allergyStatus: null, dailyMicrograms: null, maxDailyMicrograms: null }] }) });
  await page.goto('/consult/e-1');
  const checks = page.getByRole('region', { name: 'Safety checks' });
  await expect(checks).toContainText('Allergies: not recorded');
  await expect(checks.getByRole('status').filter({ hasText: 'Moderate · Allergies are not recorded for this patient' })).toBeVisible();
  await expect(card(page, 'Metformin')).toContainText('Safety: not fully checked');
  await expect(checks.getByRole('button', { name: 'Give a reason to go ahead' })).toHaveCount(0); // moderate: no reason asked
  const before = calls.checked;
  await card(page, 'Metformin').getByLabel('Dose').fill('2');
  await expect(card(page, 'Metformin').getByText('Saved')).toBeVisible();
  await expect.poll(() => calls.checked).toBeGreaterThan(before);
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
for (const locale of ['en', 'ar'])
  test(`axe finds no violations on the prescription composer (${locale})`, async ({ page }) => {
    await mockPrescription(page, { locale, lines: [
      line('rx-1', 'Metformin', { dose: '1', doseUnit: 'tablet', route: 'oral', frequency: 'BD', sig: { en: 'Take 1 tablet by mouth twice a day.', hi: '1 गोली मुँह से दिन में दो बार लें।', ar: 'تناول 1 قرص عن طريق الفم مرتين يوميًا.' } }),
      line('rx-2', 'Herbal tonic X', { formularyItemId: null, strength: null, form: null, freeText: true }),
      stopLine('Atorvastatin', 'Muscle pain (synthetic)'),
    ], checks: clearChecks({ allergies: 'Listed', unresolved: 1, alerts: [penicillinAlert('rx-1'), { ...penicillinAlert('rx-1'), key: 'dup', kind: 'Duplicate', severity: 'Moderate', other: 'Metformin', code: 'same_class' }],
      notChecked: [{ id: 'rx-2', medicine: 'Herbal tonic X', onChart: false, reason: 'FreeText' }] }) });
    await page.goto('/consult/e-1');
    await expect(page.getByRole('region', { name: locale === 'ar' ? 'الوصفة الطبية' : 'Prescription' })).toBeVisible();
    if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    await expect(page.getByRole('region', { name: locale === 'ar' ? 'فحوص السلامة' : 'Safety checks' })).toBeVisible();
    await page.getByRole('button', { name: locale === 'ar' ? 'اذكر سبب المتابعة' : 'Give a reason to go ahead' }).click(); // the reason form is checked too
    await page.locator('details summary').click();
    await page.addScriptTag({ path: axePath });
    const violations = await page.evaluate(async () =>
      (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations.map(v => `${v.id} (${v.nodes.length})`));
    expect(violations).toEqual([]);
  });
