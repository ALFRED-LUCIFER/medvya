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

const flagRequests: { patientIds: string[] }[] = [];

async function open(page: Page, locale = 'en') {
  flagRequests.length = 0;
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: doctor }));
  await page.route('**/v1/front-desk/board?*', r => r.fulfill({ json: { facilityId: 'f-1', timeZone: 'Asia/Kolkata', date: '2026-10-19', now: ist('10:04'), cards: [
    card('a1', 'Meera Iyer (synthetic)', '10:00', 'ReadyForDoctor', 'u-rao', { token: 'A-03', queuePlace: 2 }),
    card('a2', 'Deepak Nair (synthetic)', '10:20', 'Booked', 'u-rao', { registrationIncomplete: true }),
    card('a3', 'Farah Ali (synthetic)', '10:10', 'CheckedIn', 'u-khan', { token: 'B-01', queuePlace: 3 }),
    // MDV-P1-103: a walk-in given a 19:00 slot, escalated by the nurse: first in the clinic's queue (the server's place), pinned above Next patient.
    card('a4', 'Lakshmi Rajan (synthetic)', '19:00', 'WithNurse', 'u-rao', { token: 'A-05', queuePlace: 1, walkIn: true }),
  ] } }));
  await page.route('**/v1/encounters', r => r.fulfill({ json: encounter }));
  await page.route('**/v1/encounters/e-1', r => r.fulfill({ json: { encounter, timeZone: 'Asia/Kolkata', treatmentConsent: true,
    patient: { id: 'p-a1', fullName: 'Meera Iyer (synthetic)', mrn: '000001', dateOfBirth: '1972-03-12', ageYears: 54, sexAtBirth: 'Female' },
    allergies: { state: 'Listed', allergies: [{ substance: 'Penicillin', reaction: null, status: 'Confirmed', source: 'ClinicianVerified' }] } } }));
  // The server answers only for patients this doctor may treat: Farah (a colleague's) is left out, so she shows no flag.
  await page.route('**/v1/chart/allergy-flags', r => {
    flagRequests.push(r.request().postDataJSON() as { patientIds: string[] });
    return r.fulfill({ json: { items: [{ patientId: 'p-a1', state: 'Listed', abnormalVitals: true }, { patientId: 'p-a2', state: 'NotRecorded', abnormalVitals: false }] } });
  });
  // The nurse's urgent item for Meera's visit (MDV-P2-007); the server answers only for patients this doctor may treat.
  await page.route('**/v1/intake-escalations?*', r => r.fulfill({ json: [
    { id: 'x-1', appointmentId: 'a4', patientId: 'p-a4', reason: 'Fever with BP 168/96 (synthetic)', escalatedAt: ist('10:02') }] }));
  // An unsigned note from an earlier day (MDV-P2-002): the server lists only this doctor's own.
  await page.route('**/v1/notes/pending', r => r.fulfill({ json: { more: false, items: [
    { encounterId: 'e-old', startedAt: '2026-10-16T06:30:00Z', drafted: true, noteSigned: false, openPastDay: true, patientName: 'Kavya Reddy (synthetic)' },
    // Signed, but nobody pressed End (MDV audit UX-11): the server lists it until the consultation is ended.
    { encounterId: 'e-open', startedAt: '2026-10-18T06:30:00Z', drafted: true, noteSigned: true, openPastDay: true, patientName: 'Deepak Nair (synthetic)' }] } }));
  await page.goto('/today');
}

// MDV-P2-059 AC2: the title and the actions sit in the 44 px PageHeader row and do not move when the day arrives; the day is secondary text.
test('the title and the actions do not move while the day loads', async ({ page }) => {
  await open(page);
  await expect(page.getByRole('region', { name: "Today's visits" })).toBeVisible();
  let release = () => {};
  const held = new Promise<void>(r => { release = r; });
  await page.route('**/v1/front-desk/board?*', async r => { await held; await r.fallback(); });
  await page.reload();
  const title = page.getByRole('heading', { level: 1, name: 'Today' }), mine = page.getByRole('button', { name: 'My patients' });
  await expect(page.locator('main [data-slot=page-message][data-kind=busy]')).toBeVisible();
  const before = [await title.boundingBox(), await mine.boundingBox()];
  release();
  await expect(page.getByRole('region', { name: "Today's visits" })).toBeVisible();
  await expect(page.locator('main [data-slot=page-header-meta]')).not.toBeEmpty();
  expect([await title.boundingBox(), await mine.boundingBox()]).toEqual(before);
  expect((await page.locator('main [data-slot=page-header-row]').boundingBox())!.height).toBe(44);
});

test('Today shows my visits first, the clinic when toggled, and starts the consultation', async ({ page }) => {
  await open(page);
  const hero = page.getByRole('region', { name: 'Next patient' });
  await expect(hero).toContainText('Meera Iyer (synthetic)');
  const list = page.getByRole('region', { name: "Today's visits" });
  await expect(list.getByRole('listitem')).toHaveCount(3); // mine only
  await expect(list).not.toContainText('Farah Ali');
  await expect(list.getByRole('listitem').filter({ hasText: 'Deepak Nair' })).toContainText('Booked');
  await expect(list.getByRole('listitem').filter({ hasText: 'Deepak Nair' })).toContainText('Registration incomplete');
  await expect(list).toContainText('54 y');
  await page.getByRole('button', { name: 'Whole clinic' }).click();
  await expect(list.getByRole('listitem')).toHaveCount(4);
  await expect(list.getByRole('listitem').filter({ hasText: 'Farah Ali' })).toContainText('Dr. Sana Khan');
  await page.getByRole('button', { name: 'My patients' }).click();
  await hero.getByRole('button', { name: 'Start consultation with Meera Iyer (synthetic)' }).click();
  await expect(page).toHaveURL(/\/consult\/e-1$/);
  const banner = page.getByRole('region', { name: 'Patient identity' });
  await expect(banner).toBeVisible();
  await expect(banner).toContainText('Allergies: Penicillin (Verified)'); // the chart's allergies, not "not recorded yet"
});

test('each visit shows its allergy state in words and links to the chart; a patient outside my care shows no flag', async ({ page }) => {
  await open(page);
  const list = page.getByRole('region', { name: "Today's visits" });
  await expect(page.getByRole('region', { name: 'Next patient' })).toContainText('Allergies listed');
  await expect(page.getByRole('region', { name: 'Next patient' })).toContainText('Abnormal vitals today'); // MDV-1090: words and an icon
  await expect(list.getByRole('listitem').filter({ hasText: 'Deepak Nair' })).not.toContainText('Abnormal vitals');
  await expect(list.getByRole('listitem').filter({ hasText: 'Deepak Nair' })).toContainText('Allergies not recorded'); // unknown is never "none"
  await page.getByRole('button', { name: 'Whole clinic' }).click();
  await expect(list.getByRole('listitem').filter({ hasText: 'Farah Ali' })).not.toContainText(/llergies/);
  // The ids go in the body, once each, never the address.
  expect(flagRequests.at(-1)!.patientIds.sort()).toEqual(['p-a1', 'p-a2', 'p-a3', 'p-a4']);
  await expect(list.getByRole('link', { name: 'Deepak Nair (synthetic)' })).toHaveAttribute('href', '/patients/p-a2');
  await page.getByRole('region', { name: 'Next patient' }).getByRole('link', { name: 'Meera Iyer (synthetic)' }).click();
  await expect(page).toHaveURL(/\/patients\/p-a1$/);
});

// MDV-P1-103 AC1, AC3: at 1024 by 768 the escalated visit is in the first viewport, above Next patient, with its reason in words and an
// icon; the doctor's day lists the waiting patients in the server's queue order (escalation first), each arrived row with its token.
test("a nurse's escalation is pinned above Next patient in words, and the day follows the clinic's queue order with tokens", async ({ page }) => {
  await page.setViewportSize({ width: 1024, height: 768 });
  await open(page);
  const pinned = page.getByRole('region', { name: 'Escalated by the nurse' });
  await expect(pinned).toContainText('Lakshmi Rajan (synthetic)');
  await expect(pinned).toContainText('Nurse escalation: Fever with BP 168/96 (synthetic)');
  await expect(pinned).toContainText('A-05');
  const [pin, hero] = [(await pinned.boundingBox())!, (await page.getByRole('region', { name: 'Next patient' }).boundingBox())!];
  expect(pin.y + pin.height).toBeLessThanOrEqual(hero.y);
  expect(pin.y + pin.height).toBeLessThanOrEqual(768);
  await expect(page.getByRole('region', { name: 'Next patient' })).toContainText('Meera Iyer (synthetic)'); // the next one who is not escalated
  const rows = page.getByRole('region', { name: "Today's visits" }).getByRole('listitem');
  await expect(rows.first()).toContainText('Lakshmi Rajan (synthetic)'); // row 1, though her slot is 19:00
  await expect(rows.nth(0)).toContainText('A-05');
  await expect(rows.nth(1)).toContainText('A-03');
  await expect(rows.filter({ hasText: 'Deepak Nair' })).not.toContainText('Nurse escalation');
});

test('a failed escalation read is announced, never shown as "no escalation"', async ({ page }) => {
  await open(page);
  await page.unroute('**/v1/intake-escalations?*');
  await page.route('**/v1/intake-escalations?*', r => r.fulfill({ status: 500, json: {} }));
  await page.reload();
  await expect(page.getByRole('alert').filter({ hasText: 'Nurse escalations could not be loaded' })).toBeVisible();
});

test('an unsigned note from an earlier day stays on Today and opens its consultation', async ({ page }) => {
  await open(page);
  const pending = page.getByRole('region', { name: '1 note to sign' });
  await expect(pending).toContainText('Kavya Reddy (synthetic)');
  await expect(pending).toContainText('16 Oct 2026 · Draft, not signed');
  await expect(pending).toContainText('Still open from an earlier day');
  await expect(pending).not.toContainText('Deepak Nair');
  await pending.getByRole('link', { name: 'Open to finish' }).click();
  await expect(page).toHaveURL(/\/consult\/e-old$/);
});

test('a signed consultation still open from an earlier day is flagged until it is ended (MDV audit UX-11)', async ({ page }) => {
  await open(page);
  const open_ = page.getByRole('region', { name: 'Consultations still open from an earlier day' });
  await expect(open_).toContainText('Deepak Nair (synthetic)');
  await expect(open_).toContainText('18 Oct 2026 · Note signed, consultation not ended');
  await open_.getByRole('link', { name: 'Open to end' }).click();
  await expect(page).toHaveURL(/\/consult\/e-open$/);
});

test('on a phone the next patient and the consult button come first and the page does not scroll sideways', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await open(page);
  // The first consult action is the escalated patient's, pinned above Next patient (MDV-P1-103): that one must be in thumb reach.
  const button = page.getByRole('main').getByRole('button', { name: /Start consultation/ }).first();
  await expect(button).toHaveAccessibleName('Start consultation with Lakshmi Rajan (synthetic)');
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
