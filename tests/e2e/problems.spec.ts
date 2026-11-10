import { createRequire } from 'node:module';
import { expect, test } from '@playwright/test';
import { mockProblems } from './support/problems';

// MDV-P2-010 (E-07): the doctor codes diagnoses with ICD-10 by code or words, with a verification state; a provisional one is shown apart in
// words, an icon and a dashed outline (never colour alone), and a problem can be resolved. The server rules are proven on PostgreSQL
// (ProblemListTests); the J2 step is in journeys/j2.spec.ts.
test('the doctor finds a code by words, adds it as provisional, and resolves another problem', async ({ page }) => {
  const { calls } = await mockProblems(page);
  await page.goto('/consult/e-1/problems');
  await expect(page.getByRole('region', { name: 'Patient identity' }).getByRole('heading', { name: 'Meera Iyer (synthetic)' })).toBeVisible();
  await expect(page.getByText('Codes from ICD-10 (2019-medvya-starter-1).', { exact: false })).toBeVisible();
  const table = page.getByRole('region', { name: 'Problem list' });
  await expect(table.getByRole('row', { name: /Type 2 diabetes/ })).toContainText('E11.9');
  await expect(table.getByRole('row', { name: /Knee pain/ })).toContainText('Not coded');
  await expect(table.getByRole('row', { name: /Knee pain/ })).toContainText('Patient reported');

  // Search by words, then by code without the dot.
  const search = page.getByLabel('Search ICD-10 by code or words');
  await search.fill('angina');
  const results = page.getByRole('list', { name: 'Matching codes' });
  await expect(results.getByRole('button')).toHaveText([/I20\.9.* · Angina, not specified/]);
  await search.fill('e785');
  await expect(results.getByRole('button')).toHaveText([/E78\.5.* · Hyperlipidaemia, not specified/]);
  await search.fill('nothing like this');
  await expect(page.getByText('No code matches.', { exact: false })).toBeVisible();
  await search.fill('angina');
  await results.getByRole('button', { name: /I20\.9/ }).click();
  await expect(page.getByRole('status').filter({ hasText: 'I20.9' })).toContainText('Angina, not specified');

  await page.getByRole('combobox', { name: 'Verification' }).click();
  await page.getByRole('option', { name: 'Provisional · not confirmed' }).click();
  await page.getByRole('button', { name: 'Add problem' }).click();
  await expect(table.getByRole('row', { name: /Angina/ })).toContainText('Provisional · not confirmed');
  expect(calls.adds).toEqual([{ code: 'I20.9', description: null, verification: 'Provisional', onsetOn: null }]);

  // Neither a code nor words: said before anything is sent.
  await page.getByRole('button', { name: 'Add problem' }).click();
  await expect(page.getByText('Choose a code or describe the problem.')).toBeVisible();
  expect(calls.adds).toHaveLength(1);

  // A code-less problem in the doctor's words, verified here: "Added today · verified".
  await page.getByLabel('Description').fill('Chest tightness on exertion (synthetic)');
  await page.getByRole('button', { name: 'Add problem' }).click();
  await expect(table.getByRole('row', { name: /Chest tightness/ })).toContainText('Added today · verified');

  await page.getByRole('button', { name: 'Edit Knee pain since a fall (synthetic)' }).click();
  const edit = page.getByRole('form', { name: 'Edit Knee pain since a fall (synthetic)' });
  await edit.getByRole('combobox', { name: 'Status' }).click();
  await page.getByRole('option', { name: 'Resolved' }).click();
  await edit.getByRole('button', { name: 'Save' }).click();
  await expect(table.getByRole('row', { name: /Knee pain/ })).toContainText('Resolved');
  expect(calls.updates).toEqual([{ id: 'pr-2', verification: 'PatientReported', status: 'Resolved', onsetOn: null }]);
});

test('an ended consultation shows the list read-only', async ({ page }) => {
  await mockProblems(page, 'en', true);
  await page.goto('/consult/e-1/problems');
  await expect(page.getByText('The consultation has ended. The problem list is read-only here.')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Add problem' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: /^Edit / })).toHaveCount(0);
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
for (const locale of ['en', 'ar'] as const) {
  test(`axe finds no violations on the problem list (${locale}); targets are 44 px`, async ({ page }) => {
    await mockProblems(page, locale);
    await page.goto('/consult/e-1/problems');
    await page.getByRole('table').waitFor();
    await page.locator('#problem-search').fill('diabetes'); // results open, so they are checked too
    await page.getByRole('list').last().getByRole('button').first().waitFor();
    await page.getByRole('table').getByRole('button').first().click(); // an edit form open
    await page.getByRole('form').first().waitFor();
    expect(await page.evaluate(() => document.documentElement.dir)).toBe(locale === 'ar' ? 'rtl' : 'ltr');
    await page.addScriptTag({ path: axePath });
    const violations = await page.evaluate(async () =>
      (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
        .map(v => `${v.id} x${v.nodes.length}`));
    expect(violations).toEqual([]);
    const small = await page.getByRole('button').evaluateAll(els => els.filter(e => e.getBoundingClientRect().height < 44 && (e as HTMLElement).offsetParent).map(e => e.textContent));
    expect(small).toEqual([]);
  });
}
