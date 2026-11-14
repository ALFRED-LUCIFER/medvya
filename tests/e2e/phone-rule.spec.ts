import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';
import { mockNote } from './support/note';
import { line, mockPrescription } from './support/prescription';
import { mockReconciliation } from './support/reconciliation';

// MDV-P1-098, ruling 5: under 768 px clinical writes say "Open this on a tablet or computer"; reading, the queue and an addendum stay.
// A usability rule in the client only: the server's answers are unchanged (no endpoint changed; its authorisation tests are untouched).
// CI has no API: endpoints are mocked in the shapes the server sends (support/*).
const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
const message = 'Open this on a tablet or computer.';
const doctor = {
  user: { id: 'u-rao', displayName: 'Dr. Arjun Rao (synthetic)' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role: 'SoloDoctor' }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role: 'SoloDoctor', flags: ['clinical.today'] as string[], country: 'IN',
    capabilities: { RegisterBookPatients: 'Full', CheckInQueue: 'Full', Patient360Chart: 'Full', VitalsAllergiesEntry: 'Full', ClinicalNotes: 'Sign', PrescriptionsOrders: 'Sign', StaffRolesIntegrations: 'Config' } },
};
const phone = { width: 375, height: 800 }, tablet = { width: 768, height: 1024 };
// A phone is narrow and touch: the rule follows the pointer too, so a desktop zoomed to 200 % keeps every control (last test).
test.use({ hasTouch: true, isMobile: true });
const signedSeed = { version: 3, status: 'Signed', sections: { chiefComplaint: 'Thirst', assessment: 'Review', plan: 'Metformin 500 mg' },
  signature: { versionNo: 1, signedBy: 'u-rao', signedAt: '2026-10-19T04:35:00Z', contentHash: 'b'.repeat(64), signerName: 'Dr Asha Raman (synthetic)' } };

/** One open consultation with a draft prescription, orders and a note (draft unless seeded), as the full doctor. */
async function consult(page: Page, { locale = 'en', note = {} as Parameters<typeof mockNote>[1] } = {}) {
  await mockPrescription(page, { locale, lines: [line('rx-1', 'Metformin')] });
  const mock = await mockNote(page, note);
  await page.route('**/v1/me/context', r => r.fulfill({ json: doctor })); // after the helpers: this one answers
  return mock;
}
const scrollsSideways = (page: Page) => page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
const writeControls = (page: Page) => page.getByRole('button', { name: /^(Sign note|End consultation|Authorise|Place orders|Add medicine|Add as free text)/ });

test('at 375 px a draft consultation shows the message with the patient\'s name in place of the note, plan, prescription and orders', async ({ page }) => {
  await page.setViewportSize(phone);
  await consult(page);
  await page.goto('/consult/e-1');
  const gate = page.locator('[data-phone-rule]');
  await expect(gate).toHaveCount(1); // one message for the whole draft, not one per section
  await expect(gate).toContainText(message);
  await expect(gate).toContainText('Meera Iyer (synthetic)');
  await expect(page.getByRole('region', { name: 'Consultation note' })).toHaveCount(0); // Review and sign is part of the note
  await expect(page.getByRole('region', { name: 'Prescription' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: /Orders/ })).toHaveCount(0);
  await expect(writeControls(page)).toHaveCount(0);
  await expect(page.locator('[data-slot=patient-banner]')).toContainText('Meera Iyer (synthetic)'); // who it is stays readable
  expect(await scrollsSideways(page)).toBe(false);
});

test('at 375 px the reconciliation and coding pages show the message with the patient\'s name and no write control', async ({ page }) => {
  await page.setViewportSize(phone);
  await mockReconciliation(page);
  await page.route('**/v1/me/context', r => r.fulfill({ json: doctor }));
  for (const path of ['/consult/e-1/medicines', '/consult/e-1/problems']) {
    await page.goto(path);
    const gate = page.locator('[data-phone-rule]');
    await expect(gate).toContainText(message);
    await expect(gate).toContainText('Meera Iyer (synthetic)');
    await expect(page.getByRole('heading', { level: 1 })).toHaveCount(1);
    await expect(page.getByRole('button', { name: /Keep|Change|Stop|Confirm|Add diagnosis/ })).toHaveCount(0);
  }
});

test('at 375 px a signed note stays readable and an addendum can be written and signed', async ({ page }) => {
  await page.setViewportSize(phone);
  const mock = await consult(page, { note: signedSeed });
  mock.reauthed = true;
  await page.goto('/consult/e-1');
  await expect(page.getByRole('region', { name: 'Consultation note' }).getByLabel('Plan')).toHaveValue('Metformin 500 mg');
  const history = page.getByRole('region', { name: 'Version history' });
  await history.getByRole('button', { name: 'Add addendum' }).click();
  await history.getByLabel('Addendum', { exact: true }).fill('Synthetic: metformin dose is 1000 mg, not 500 mg');
  await history.getByLabel('Reason for the addendum').fill('Dose typed wrongly');
  await history.getByRole('button', { name: 'Sign addendum' }).click();
  await expect(history).toContainText('Addendum to version 1');
  expect(mock.addendumPosts).toEqual([{ text: 'Synthetic: metformin dose is 1000 mg, not 500 mg', reason: 'Dose typed wrongly' }]);
  // The prescription and orders under a signed note are still writes: the one message stands in for them.
  await expect(page.locator('[data-phone-rule]')).toHaveCount(1);
  await expect(page.getByRole('region', { name: 'Prescription' })).toHaveCount(0);
  expect(await scrollsSideways(page)).toBe(false);
});

test('at 768 px every write control of the consultation is there', async ({ page }) => {
  await page.setViewportSize(tablet);
  await consult(page);
  await page.goto('/consult/e-1');
  await expect(page.getByRole('region', { name: 'Consultation note' })).toBeVisible();
  await expect(page.getByRole('region', { name: 'Prescription' })).toBeVisible();
  await expect(page.locator('[data-phone-rule]')).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'End consultation' })).toBeVisible();
  await mockReconciliation(page);
  await page.route('**/v1/me/context', r => r.fulfill({ json: doctor }));
  await page.goto('/consult/e-1/medicines');
  await expect(page.getByRole('heading', { name: 'Reconcile medicines' })).toBeVisible();
  await expect(page.locator('[data-phone-rule]')).toHaveCount(0);
});

test('at 375 px My day and the chart are readable with the allergy first and no sideways scroll', async ({ page }) => {
  await page.setViewportSize(phone);
  await page.route('**/v1/me/context', r => r.fulfill({ json: doctor }));
  await page.route('**/v1/front-desk/board?*', r => r.fulfill({ json: { facilityId: 'f-1', timeZone: 'Asia/Kolkata', date: '2026-10-19', now: '2026-10-19T04:40:00Z', cards: [] } }));
  await page.goto('/today');
  await expect(page.getByRole('main').getByRole('heading', { level: 1 })).toBeVisible();
  await expect(page.locator('[data-phone-rule]')).toHaveCount(0);
  expect(await scrollsSideways(page)).toBe(false);

  await page.route('**/v1/patients/p-1/chart', r => r.fulfill({ json: {
    patient: { id: 'p-1', fullName: 'Meera Iyer (synthetic)', mrn: '000001', dateOfBirth: '1972-03-12', ageYears: 54, sexAtBirth: 'Female', preferredLanguage: null },
    treatmentConsent: true, timeZone: 'Asia/Kolkata', lifeStage: 'Adult', historySections: ['PastMedical'], pregnancy: { open: null, ruledOut: null },
    chart: { allergyState: 'Listed', allergies: [{ substance: 'Penicillin', reaction: 'Rash', status: 'Confirmed', source: 'ClinicianVerified' }],
      problems: [], medicines: [], lastVitals: [], trends: [], recentEncounters: [], openEncounterId: null, lastLabs: [], bmi: null, bloodGroup: null, careTeam: [], history: [] } } }));
  await page.route('**/v1/patients/p-1/timeline*', r => r.fulfill({ json: { entries: [], next: null, timeZone: 'Asia/Kolkata' } }));
  await page.route('**/v1/patients/p-1/visits', r => r.fulfill({ json: { visits: [], timeZone: 'Asia/Kolkata' } }));
  await page.goto('/patients/p-1');
  const banner = page.getByRole('region', { name: 'Patient Meera Iyer (synthetic)' });
  await expect(banner).toContainText('Allergy: Penicillin (Verified)');
  await expect(banner.getByText(/Allergy:/)).toBeInViewport(); // the allergy is on the first screen, before any scrolling
  await expect(page.locator('[data-phone-rule]')).toHaveCount(0);
  expect(await scrollsSideways(page)).toBe(false);
});

test('at 375 px settings and full registration show the message; quick registration still works', async ({ page }) => {
  await page.setViewportSize(phone);
  await page.route('**/v1/me/context', r => r.fulfill({ json: doctor }));
  for (const [path, title] of [['/settings', 'Practice settings'], ['/patients/new', 'Register patient']] as const) {
    await page.goto(path);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(title);
    await expect(page.locator('[data-phone-rule]')).toContainText(message);
  }
  await page.goto('/patients/new?mode=quick');
  await expect(page.locator('[data-phone-rule]')).toHaveCount(0);
  await expect(page.getByRole('main').getByRole('textbox').first()).toBeVisible();
  await page.setViewportSize(tablet);
  await page.goto('/patients/new');
  await expect(page.locator('[data-phone-rule]')).toHaveCount(0);
});

for (const locale of ['en', 'ar'])
  test(`the phone message is axe clean at 375 px (${locale})`, async ({ page }) => {
    await page.setViewportSize(phone);
    await consult(page, { locale });
    await page.goto('/consult/e-1');
    await expect(page.locator('[data-phone-rule]')).toContainText(locale === 'en' ? message : 'افتح هذا على جهاز لوحي أو حاسوب.');
    if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    await page.addScriptTag({ path: axePath });
    const violations = await page.evaluate(async () =>
      (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
        .map(v => `${v.id} (${v.nodes.length})`));
    expect(violations).toEqual([]);
  });

test.describe('a desktop zoomed to 200 %', () => {
  test.use({ hasTouch: false, isMobile: false });
  test('is as narrow as a phone but keeps a mouse, so every write control stays (WCAG 1.4.4, 1.4.10)', async ({ page }) => {
    await page.setViewportSize({ width: 640, height: 360 });
    await consult(page);
    await page.goto('/consult/e-1');
    await expect(page.getByRole('region', { name: 'Consultation note' })).toBeVisible();
    await expect(page.getByRole('region', { name: 'Prescription' })).toBeVisible();
    await expect(page.locator('[data-phone-rule]')).toHaveCount(0);
  });
});
