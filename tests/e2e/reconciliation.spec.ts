import { createRequire } from 'node:module';
import { expect, test } from '@playwright/test';
import { mockReconciliation } from './support/reconciliation';

// MDV-P2-011 (E-07): the doctor reconciles the medicines of a consultation. Conflicts are written in words, Confirm stays disabled until every
// row is decided, Stop and Change ask why, and confirming shows the table read-only. The server rules are proven on PostgreSQL
// (MedicationReconciliationTests); the J2 step is in journeys/j2.spec.ts.
test('the doctor decides every medicine and confirms', async ({ page }) => {
  const { calls } = await mockReconciliation(page);
  await page.goto('/consult/e-1/medicines');
  await expect(page.getByRole('region', { name: 'Patient identity' }).getByRole('heading', { name: 'Meera Iyer (synthetic)' })).toBeVisible();
  const confirm = page.getByRole('button', { name: 'Confirm changes' });
  await expect(page.getByRole('status').filter({ hasText: 'decided' })).toHaveText('0 of 3 decided · 3 sources disagree');
  await expect(confirm).toBeDisabled();
  await expect(page.getByText('Decide every medicine to confirm.')).toBeVisible();

  // Conflicts in words, not colour alone.
  await expect(page.getByText('Atorvastatin active in the chart, patient says stopped')).toBeVisible();
  await expect(page.getByText('Metformin: the doses differ between sources')).toBeVisible();
  await expect(page.getByText('Aspirin: patient reports it, but it is not on the chart')).toBeVisible();

  // Stop asks why first, and saves nothing until it has a reason.
  const statin = page.getByRole('group', { name: 'Decision for Atorvastatin' });
  await statin.getByRole('button', { name: 'Stop' }).click();
  await page.getByRole('form', { name: 'Decision for Atorvastatin' }).getByRole('button', { name: 'Save decision' }).click();
  await expect(page.getByText('Say why.')).toBeVisible();
  expect(calls.decisions).toEqual([]);
  await page.getByLabel('Reason').fill('Muscle pain (synthetic)');
  await page.getByRole('button', { name: 'Save decision' }).click();
  await expect(page.getByText('Decided: Stop · Muscle pain (synthetic)')).toBeVisible();

  // Change asks for the new dose too.
  await page.getByRole('group', { name: 'Decision for Metformin' }).getByRole('button', { name: 'Change' }).click();
  await page.getByLabel('New dose').fill('1000 mg');
  await page.getByLabel('Reason').fill('Patient takes it twice daily (synthetic)');
  await page.getByRole('button', { name: 'Save decision' }).click();
  await page.getByRole('group', { name: 'Decision for Aspirin' }).getByRole('button', { name: 'Add to chart' }).click();

  await expect(page.getByRole('status').filter({ hasText: 'decided' })).toHaveText('3 of 3 decided · 3 sources disagree');
  expect(calls.decisions).toEqual([
    { key: 'atorvastatin', kind: 'Stop', newDose: '', reason: 'Muscle pain (synthetic)' },
    { key: 'metformin', kind: 'Change', newDose: '1000 mg', reason: 'Patient takes it twice daily (synthetic)' },
    { key: 'aspirin', kind: 'Add', newDose: '', reason: '' },
  ]);
  await expect(confirm).toBeEnabled();
  await confirm.click();
  await expect(page.getByText(/Confirmed at 10:05\. The chart is updated\./)).toBeVisible();
  await expect(page.getByRole('button', { name: 'Confirm changes' })).toHaveCount(0);
  await expect(page.getByRole('group', { name: 'Decision for Atorvastatin' })).toHaveCount(0); // read-only once confirmed
  expect(calls.confirms).toBe(1);
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
for (const locale of ['en', 'ar'] as const) {
  test(`axe finds no violations on the reconciliation (${locale}); targets are 44 px`, async ({ page }) => {
    await mockReconciliation(page, locale);
    await page.goto('/consult/e-1/medicines');
    await page.getByRole('table').waitFor();
    await page.getByRole('group').first().getByRole('button').first().click(); // saves Keep so the decided state is checked too
    await page.getByRole('group').nth(2).getByRole('button').nth(1).click();
    await page.getByRole('group').first().getByRole('button').nth(2).click(); // opens the Stop form
    expect(await page.evaluate(() => document.documentElement.dir)).toBe(locale === 'ar' ? 'rtl' : 'ltr');
    await page.getByRole('form').waitFor();
    await page.waitForTimeout(500); // the pressed button's colour transition must end, or axe reads mid-fade colours
    await page.addScriptTag({ path: axePath });
    const violations = await page.evaluate(async () =>
      (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
        .map(v => `${v.id} x${v.nodes.length}`));
    expect(violations).toEqual([]);
    const small = await page.getByRole('button').evaluateAll(els => els.filter(e => e.getBoundingClientRect().height < 44 && (e as HTMLElement).offsetParent).map(e => e.textContent));
    expect(small).toEqual([]);
  });
}
