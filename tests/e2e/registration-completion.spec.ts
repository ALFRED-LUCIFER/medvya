import { createRequire } from 'node:module';
import { expect, test, type Page, type Request } from '@playwright/test';

// MDV-P1-015: the completion task for a quick-registered patient, from the record page. CI has no API: /v1/me/context and
// /v1/patients/* are mocked with the synthetic cast; the facility scope and audit are covered by PatientEndpointTests.
const desk = (capabilities: Record<string, string>) => ({
  user: { id: 'u-joseph', displayName: 'A. Joseph' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role: 'Receptionist' }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role: 'Receptionist', flags: [], country: 'IN', capabilities },
});
const full = { RegisterBookPatients: 'Full', CheckInQueue: 'Full', Patient360Chart: 'None', ClinicalNotes: 'None' };
const id = '5e1d0001-0004-4000-8000-000000000131';
const leela = {
  id, mrn: '001131', fullName: 'Leela Raman (synthetic)', arabicName: null, dateOfBirth: '1993-04-02', dateOfBirthEstimated: false, ageYears: 33,
  sexAtBirth: 'Female', genderIdentity: null, preferredLanguage: 'Tamil, English', nationality: null, mobile: '+919000000131',
  alternatePhone: null, email: null, status: 'Incomplete', registeredOn: '2026-10-25', address: null, identifiers: [], payer: null,
};

async function open(page: Page, { capabilities = full as Record<string, string>, locale = 'en' } = {}) {
  const saves: Request[] = [];
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: desk(capabilities) }));
  await page.route(`**/v1/patients/${id}`, r => r.fulfill({ json: leela }));
  await page.route(`**/v1/patients/${id}/completion`, r => { saves.push(r.request()); return r.fulfill({ json: { status: 'Complete' } }); });
  await page.goto(`/patients/directory/${id}`);
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
  return saves;
}

test('the record shows the completion task; completing it sends address and payer only and closes the task', async ({ page }) => {
  const saves = await open(page);
  const task = page.getByRole('region', { name: 'Registration to complete' });
  await expect(task).toContainText('Address and payer are missing. Add them while the patient is at the desk.');
  await expect(page.getByText('Tamil, English')).toBeVisible(); // free text from before language codes is shown as entered
  await task.getByRole('link', { name: 'Complete registration' }).click();
  await expect(page).toHaveURL(`/patients/directory/${id}/complete`);

  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByText('Add the address, the payer or both.')).toBeVisible();
  expect(saves).toHaveLength(0);

  await page.getByLabel('House / flat, building').fill('4 Synthetic Street');
  await page.getByLabel('City / town / village').fill('Chennai');
  await page.getByRole('combobox', { name: 'State / UT' }).click();
  await page.getByRole('option', { name: 'Tamil Nadu', exact: true }).click();
  await page.getByLabel('PIN code').fill('600020');
  await page.getByRole('combobox', { name: 'Payment type' }).click();
  await page.getByRole('option', { name: 'Self-pay', exact: true }).click();
  await page.getByRole('button', { name: 'Save' }).click();

  await expect(page.getByText('Registration complete. The completion task is closed.')).toBeVisible();
  expect(saves).toHaveLength(1);
  expect(new URL(saves[0]!.url()).search).toBe('');
  expect(saves[0]!.postDataJSON()).toEqual({
    address: { line1: '4 Synthetic Street', line2: null, city: 'Chennai', district: null, region: 'Tamil Nadu', postalCode: '600020', makani: null, poBox: null },
    payer: { kind: 'SelfPay', payerName: null, plan: null, memberNumber: null, validTo: null, network: null },
  });
  await page.getByRole('link', { name: 'Back to the record' }).click();
  await expect(page).toHaveURL(`/patients/directory/${id}`);
});

test('a role that may only read patients sees the tag but not the completion action', async ({ page }) => {
  await open(page, { capabilities: { RegisterBookPatients: 'Read' } });
  await expect(page.getByText('Incomplete registration').first()).toBeVisible();
  await expect(page.getByRole('link', { name: 'Complete registration' })).toHaveCount(0);
});

test('another facility\'s patient is not found here, and nothing is shown to complete', async ({ page }) => {
  await page.route('**/v1/me/context', r => r.fulfill({ json: desk(full) }));
  await page.route(`**/v1/patients/${id}/completion`, r => r.fulfill({ status: 404, json: {} }));
  await page.goto(`/patients/directory/${id}/complete`);
  await page.getByLabel('House / flat, building').fill('4 Synthetic Street');
  await page.getByLabel('City / town / village').fill('Chennai');
  await page.getByRole('combobox', { name: 'State / UT' }).click();
  await page.getByRole('option', { name: 'Tamil Nadu', exact: true }).click();
  await page.getByLabel('PIN code').fill('600020');
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('alert')).toContainText('This record is not available here');
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
for (const locale of ['en', 'hi', 'ar'])
  for (const width of [1366, 375])
    test(`record task and completion form are axe clean and fit (${locale}, ${width}px)`, async ({ page }) => {
      await page.setViewportSize({ width, height: 900 });
      await open(page, { locale });
      for (const path of [`/patients/directory/${id}`, `/patients/directory/${id}/complete`]) {
        await page.goto(path);
        await expect(page.locator('main h1')).toBeVisible();
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
        for (const b of await page.locator('main button, main a, main input').all())
          if (await b.isVisible()) expect((await b.boundingBox())!.height).toBeGreaterThanOrEqual(44);
        await page.addScriptTag({ path: axePath });
        const violations = await page.evaluate(async () =>
          (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
            .map(v => `${v.id} (${v.nodes.length})`));
        expect(violations).toEqual([]);
      }
    });
