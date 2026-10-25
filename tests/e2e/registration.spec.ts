import { createRequire } from 'node:module';
import { expect, test, type Page, type Request } from '@playwright/test';

// MDV-P1-016 duplicate warning while typing and resolution before save, on the quick registration form (MDV-P1-015).
// The full flows (MDV-P1-013/014) are in registration-full.spec.ts.
// CI has no API: /v1/me/context and /v1/patients/* are mocked with the synthetic cast; the server rules are covered by
// PatientDuplicateTests and PatientEndpointTests on SQL Server.
const receptionist = {
  user: { id: 'u-joseph', displayName: 'A. Joseph' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role: 'Receptionist' }],
  active: {
    membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role: 'Receptionist', flags: [], country: 'IN',
    capabilities: { RegisterBookPatients: 'Full', CheckInQueue: 'Full', InvoicesPayments: 'Full', Patient360Chart: 'None', ClinicalNotes: 'None' },
  },
};
const existingId = '5e1d0001-0004-4000-8000-000000000002';
const existing = {
  patientId: existingId, initials: 'DN', ageYears: 47, sexAtBirth: 'Male', mrnTail: '0872',
  matchedOnNameAndDateOfBirth: true, matchedIdentifiers: [], identifiers: [{ scheme: 'Abha', lastFour: '6612' }],
};

async function desk(page: Page, { locale = 'en', register }: { locale?: string; register?: (body: unknown) => { status: number; json: unknown } } = {}) {
  const checks: Request[] = [], saves: Request[] = [];
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: receptionist }));
  await page.route('**/v1/patients/duplicate-check', r => {
    checks.push(r.request());
    const body = r.request().postDataJSON() as { fullName: string; dateOfBirth: string | null };
    return r.fulfill({ json: { candidates: /deepak/i.test(body.fullName) && body.dateOfBirth === '1979-08-14' ? [existing] : [] } });
  });
  await page.route('**/v1/patients', r => {
    saves.push(r.request());
    const { status, json } = register?.(r.request().postDataJSON()) ?? {
      status: 200, json: { patientId: '5e1d0001-0004-4000-8000-000000000099', mrn: '001093', registeredAt: '2026-10-21T04:46:00Z', status: 'Incomplete', warnings: [] },
    };
    return r.fulfill({ status, json });
  });
  await page.goto('/patients/new?mode=quick');
  await expect(page.getByRole('heading', { name: locale === 'ar' ? 'تسجيل مريض جديد' : 'Register new patient' })).toBeVisible();
  return { checks, saves };
}

async function typeDeepak(page: Page) {
  await page.getByLabel('Full name').fill('Deepak Nair');
  await page.getByLabel('Mobile').fill('+91 90000 00201');
  await page.getByRole('textbox', { name: 'Date of birth', exact: true }).fill('1979-08-14');
  await page.getByRole('radio', { name: 'Male', exact: true }).check();
}

test('a possible match appears while typing, blocks save until resolved, and the decision is sent with the registration', async ({ page }) => {
  const { checks, saves } = await desk(page);
  await typeDeepak(page);

  await expect(page.getByText('Possible match · existing records that look like this patient: 1.')).toBeVisible();
  const check = checks.at(-1)!;
  expect(new URL(check.url()).search).toBe('');
  expect(check.postDataJSON()).toEqual({ fullName: 'Deepak Nair', dateOfBirth: '1979-08-14', identifiers: [] }); // never the phone

  const review = page.getByRole('region', { name: 'Attention · possible existing record' });
  await expect(review).toContainText('Matched on name + date of birth. A shared phone number alone is never treated as a match.');
  await expect(review).toContainText('DN · 47 y · Male');
  await expect(review).toContainText('••6612');
  await expect(review).toContainText('Existing · MRN ··0872');

  const save = page.getByRole('button', { name: 'Resolve duplicate to save' });
  await expect(save).toBeDisabled();
  await review.getByRole('button', { name: 'Not the same person · continue' }).click();
  await expect(review).toContainText('Decision: not the same person. You can save.');
  expect(saves).toHaveLength(0); // deciding is not saving
  await page.getByRole('button', { name: 'Register patient' }).click();

  await expect(page.getByRole('heading', { name: 'Deepak Nair is registered' })).toBeVisible();
  await expect(page.getByText(/MRN 001093 · registered .* by A\. Joseph/)).toBeVisible();
  await expect(page.getByText('Attention · quick registration.')).toBeVisible();
  expect(saves).toHaveLength(1);
  expect(saves[0]!.postDataJSON()).toMatchObject({ quick: true, fullName: 'Deepak Nair', duplicates: { decision: 'NotSamePerson', candidateIds: [existingId] } });
});

test('a merge review request is sent as the decision; "Use existing record" registers nothing', async ({ page }) => {
  const { saves } = await desk(page);
  await typeDeepak(page);
  const review = page.getByRole('region', { name: 'Attention · possible existing record' });
  await review.getByRole('button', { name: 'Request merge review (P-03)' }).click();
  await page.getByRole('button', { name: 'Register patient' }).click();
  await expect(page.getByRole('heading', { name: 'Deepak Nair is registered' })).toBeVisible();
  expect(saves[0]!.postDataJSON()).toMatchObject({ duplicates: { decision: 'MergeReview', candidateIds: [existingId] } });

  await page.getByRole('button', { name: 'Register another' }).click();
  await typeDeepak(page);
  await page.route(`**/v1/patients/${existingId}`, r => r.fulfill({ status: 404, json: {} }));
  await page.getByRole('link', { name: 'Use existing record' }).click();
  await expect(page).toHaveURL(`/patients/directory/${existingId}`);
  expect(saves).toHaveLength(1);
});

test('changing the name after deciding asks again; no match means a plain save', async ({ page }) => {
  const { saves } = await desk(page);
  await typeDeepak(page);
  await page.getByRole('button', { name: 'Not the same person · continue' }).click();
  await page.getByLabel('Full name').fill('Kavya Iyer');
  await expect(page.getByRole('region', { name: 'Attention · possible existing record' })).toBeHidden();
  await page.getByRole('button', { name: 'Register patient' }).click();
  await expect(page.getByRole('heading', { name: 'Kavya Iyer is registered' })).toBeVisible();
  expect(saves[0]!.postDataJSON()).toMatchObject({ fullName: 'Kavya Iyer', duplicates: null });
});

test('required fields are announced, and a match found only at save time is shown', async ({ page }) => {
  await desk(page, { register: () => ({ status: 400, json: { errors: [{ field: 'duplicates', code: 'unresolved' }] } }) });
  await page.getByRole('button', { name: 'Register patient' }).click();
  await expect(page.getByLabel('Full name')).toHaveAttribute('aria-invalid', 'true');
  await expect(page.getByText('Required.')).toHaveCount(4);

  await page.getByLabel('Full name').fill('Lakshmi Pillai');
  await page.getByLabel('Mobile').fill('+91 90000 00202');
  await page.getByRole('textbox', { name: 'Date of birth', exact: true }).fill('1981-02-03');
  await page.getByRole('radio', { name: 'Female', exact: true }).check();
  await page.getByRole('button', { name: 'Register patient' }).click();
  await expect(page.getByText('A possible existing record was found while saving.', { exact: false })).toHaveCount(0); // nothing to show: no candidate
  await expect(page.getByRole('button', { name: 'Register patient' })).toBeEnabled();
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
for (const locale of ['en', 'ar'])
  for (const width of [1366, 375])
    test(`registration with the duplicate panel is axe clean and fits (${locale}, ${width}px)`, async ({ page }) => {
      await page.setViewportSize({ width, height: 900 });
      await desk(page, { locale });
      await page.locator('#fullName').fill('Deepak Nair');
      await page.locator('#mobile').fill('+91 90000 00201');
      await page.locator('#dateOfBirth').fill('1979-08-14');
      await page.getByRole('radio').nth(1).check();
      await expect(page.locator('section[aria-labelledby="duplicate-review"]')).toBeVisible();
      expect(await page.evaluate(() => document.documentElement.dir)).toBe(locale === 'ar' ? 'rtl' : 'ltr');
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
      for (const b of await page.locator('main button, main a, main input:not([type=radio]):not([type=checkbox])').all())
        if (await b.isVisible()) expect((await b.boundingBox())!.height).toBeGreaterThanOrEqual(44);
      await page.addScriptTag({ path: axePath });
      const violations = await page.evaluate(async () =>
        (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
          .map(v => `${v.id} (${v.nodes.length})`));
      expect(violations).toEqual([]);
    });
