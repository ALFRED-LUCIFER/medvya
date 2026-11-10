import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';
import { line, mockPrescription, stopLine } from './support/prescription';

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
  await expect(page.getByRole('button', { name: /authori[sz]e/i })).toHaveCount(0); // MDV-P2-017
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

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
for (const locale of ['en', 'ar'])
  test(`axe finds no violations on the prescription composer (${locale})`, async ({ page }) => {
    await mockPrescription(page, { locale, lines: [
      line('rx-1', 'Metformin', { dose: '1', doseUnit: 'tablet', route: 'oral', frequency: 'BD', sig: { en: 'Take 1 tablet by mouth twice a day.', hi: '1 गोली मुँह से दिन में दो बार लें।', ar: 'تناول 1 قرص عن طريق الفم مرتين يوميًا.' } }),
      line('rx-2', 'Herbal tonic X', { formularyItemId: null, strength: null, form: null, freeText: true }),
      stopLine('Atorvastatin', 'Muscle pain (synthetic)'),
    ] });
    await page.goto('/consult/e-1');
    await expect(page.getByRole('region', { name: locale === 'ar' ? 'الوصفة الطبية' : 'Prescription' })).toBeVisible();
    if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    await page.addScriptTag({ path: axePath });
    const violations = await page.evaluate(async () =>
      (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations.map(v => `${v.id} (${v.nodes.length})`));
    expect(violations).toEqual([]);
  });
