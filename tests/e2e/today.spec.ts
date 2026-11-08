import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';

// MDV-P2-002 (D-01): the doctor's Today shows their own visits by default and the clinic's when toggled, with the next patient first, status
// as icon plus words, and "Start consultation" from the board's control. CI has no API: the session, board and encounter are mocked in the
// shapes the server sends. Live updates (the board hub) and the audit row are covered by the board's own tests.
const doctor = {
  user: { id: 'u-rao', displayName: 'Dr. Arjun Rao (synthetic)' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role: 'SoloDoctor' }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role: 'SoloDoctor', flags: [] as string[], country: 'IN',
    capabilities: { RegisterBookPatients: 'Full', CheckInQueue: 'Full', Patient360Chart: 'Full' } },
};
const ist = (hhmm: string) => {
  const [h, m] = hhmm.split(':').map(Number);
  return new Date(Date.parse('2026-10-19T00:00:00Z') + ((h! * 60 + m!) - 330) * 60_000).toISOString();
};
const card = (id: string, name: string, time: string, state: string, calendarId = 'u-rao', extra = {}) => ({
  id, reference: `APT-${id.toUpperCase()}`, patientId: `p-${id}`, patientName: name, dateOfBirth: '1972-03-12', registrationIncomplete: false,
  calendarId, calendarName: calendarId === 'u-rao' ? 'Dr. Arjun Rao (synthetic)' : 'Dr. Sana Khan (synthetic)', visitType: 'FollowUp', reasonCategory: 'ChronicReview',
  start: ist(time), end: ist(time), state, since: ist(time), ...extra,
});
const encounter = { id: 'e-1', appointmentId: 'a1', patientId: 'p-a1', facilityId: 'f-1', leadClinicianId: 'u-rao', status: 'Active', authorityMode: 'Medvya',
  startedAt: ist('10:05'), endedAt: null, room: 'Room 3' };

async function open(page: Page, locale = 'en') {
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: doctor }));
  await page.route('**/v1/front-desk/board?*', r => r.fulfill({ json: { facilityId: 'f-1', timeZone: 'Asia/Kolkata', date: '2026-10-19', now: ist('10:04'), cards: [
    card('a1', 'Meera Iyer (synthetic)', '10:00', 'ReadyForDoctor'),
    card('a2', 'Deepak Nair (synthetic)', '10:20', 'Booked', 'u-rao', { registrationIncomplete: true }),
    card('a3', 'Farah Ali (synthetic)', '10:10', 'CheckedIn', 'u-khan'),
  ] } }));
  await page.route('**/v1/encounters', r => r.fulfill({ json: encounter }));
  await page.route('**/v1/encounters/e-1', r => r.fulfill({ json: { encounter, timeZone: 'Asia/Kolkata', treatmentConsent: true,
    patient: { id: 'p-a1', fullName: 'Meera Iyer (synthetic)', mrn: '000001', dateOfBirth: '1972-03-12', ageYears: 54, sexAtBirth: 'Female' } } }));
  await page.goto('/today');
}

test('Today shows my visits first, the clinic when toggled, and starts the consultation', async ({ page }) => {
  await open(page);
  const hero = page.getByRole('region', { name: 'Next patient' });
  await expect(hero).toContainText('Meera Iyer (synthetic)');
  const list = page.getByRole('region', { name: "Today's visits" });
  await expect(list.getByRole('article')).toHaveCount(2); // mine only
  await expect(list).not.toContainText('Farah Ali');
  await expect(list.getByRole('article').filter({ hasText: 'Deepak Nair' })).toContainText('Booked');
  await expect(list.getByRole('article').filter({ hasText: 'Deepak Nair' })).toContainText('Registration incomplete');
  await expect(list).toContainText('54 y');
  await page.getByRole('button', { name: 'Whole clinic' }).click();
  await expect(list.getByRole('article')).toHaveCount(3);
  await expect(list.getByRole('article').filter({ hasText: 'Farah Ali' })).toContainText('Dr. Sana Khan');
  await page.getByRole('button', { name: 'My patients' }).click();
  await hero.getByRole('button', { name: 'Start consultation with Meera Iyer (synthetic)' }).click();
  await expect(page).toHaveURL(/\/consult\/e-1$/);
  await expect(page.getByRole('region', { name: 'Patient identity' })).toBeVisible();
});

test('on a phone the next patient and the consult button come first and the page does not scroll sideways', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await open(page);
  const button = page.getByRole('region', { name: 'Next patient' }).getByRole('button', { name: /Start consultation/ });
  const box = (await button.boundingBox())!;
  expect(box.y + box.height).toBeLessThan(844 - 90); // above the bottom tabs, within thumb reach
  expect(box.height).toBeGreaterThanOrEqual(44);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
for (const locale of ['en', 'ar'])
  test(`axe finds no violations on Today (${locale}); 44 px targets`, async ({ page }) => {
    await open(page, locale);
    await expect(page.getByRole('region', { name: locale === 'ar' ? 'المريض التالي' : 'Next patient' })).toBeVisible();
    if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    for (const b of await page.getByRole('main').getByRole('button').all()) expect((await b.boundingBox())!.height).toBeGreaterThanOrEqual(44);
    await page.addScriptTag({ path: axePath });
    const violations = await page.evaluate(async () =>
      (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
        .map(v => `${v.id} (${v.nodes.length})`));
    expect(violations).toEqual([]);
  });
