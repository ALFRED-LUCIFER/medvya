import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';

// MDV-P2-001: the doctor starts a consultation from the board, lands on its identity banner, and ends it; another open consultation and a
// missing care relationship (MDV-P2-024) each ask the doctor what to do. CI has no API: endpoints are mocked in the shapes the server sends;
// one encounter per visit, the board move in the same transaction, the 2-second push and the audit rows are covered by EncounterTests and
// EncounterEndpointTests on PostgreSQL. The device runs in New York to prove times show in facility time.
test.use({ timezoneId: 'America/New_York' });

const doctor = {
  user: { id: 'u-rao', displayName: 'Dr. Arjun Rao (synthetic)' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role: 'SoloDoctor' }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role: 'SoloDoctor', flags: [] as string[],
    capabilities: { RegisterBookPatients: 'Full', CheckInQueue: 'Full', Patient360Chart: 'Full' } },
};
const ist = (hhmm: string) => {
  const [h, m] = hhmm.split(':').map(Number);
  return new Date(Date.parse('2026-10-19T00:00:00Z') + ((h! * 60 + m!) - 330) * 60_000).toISOString();
};
const card = (id: string, name: string, time: string, state: string) => ({
  id, reference: `APT-${id.toUpperCase()}`, patientId: `p-${id}`, patientName: name, dateOfBirth: '1972-03-12', registrationIncomplete: false,
  calendarId: 'u-rao', calendarName: 'Dr. Arjun Rao (synthetic)', visitType: 'FollowUp', reasonCategory: 'ChronicReview',
  start: ist(time), end: ist(time), state, since: ist(time),
});
const encounter = (status = 'Active') => ({
  id: 'e-1', appointmentId: 'a1', patientId: 'p-a1', facilityId: 'f-1', leadClinicianId: 'u-rao', status, authorityMode: 'Medvya',
  startedAt: ist('10:05'), endedAt: status === 'Ended' ? ist('10:25') : null, room: 'Room 3',
});

type Options = { locale?: string; start?: { status: number; json: unknown } };
async function open(page: Page, { locale = 'en', start }: Options = {}) {
  const calls = { starts: [] as unknown[], ends: 0, requests: [] as unknown[] };
  let status = 'Active';
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: doctor }));
  await page.route('**/v1/front-desk/board?*', r => r.fulfill({ json: { facilityId: 'f-1', timeZone: 'Asia/Kolkata', date: '2026-10-19', now: ist('10:04'),
    cards: [card('a1', 'Meera Iyer (synthetic)', '10:00', 'ReadyForDoctor'), card('a2', 'Deepak Nair (synthetic)', '10:20', 'Booked')] } }));
  await page.route('**/v1/encounters', r => {
    calls.starts.push(r.request().postDataJSON());
    return start && (r.request().postDataJSON() as { leaveOpen: boolean }).leaveOpen === false ? r.fulfill(start) : r.fulfill({ json: encounter() });
  });
  await page.route('**/v1/encounters/e-1', r => r.fulfill({ json: {
    encounter: encounter(status), timeZone: 'Asia/Kolkata', treatmentConsent: true,
    patient: { id: 'p-a1', fullName: 'Meera Iyer (synthetic)', mrn: '000001', dateOfBirth: '1972-03-12', ageYears: 54, sexAtBirth: 'Female' },
  } }));
  await page.route('**/v1/encounters/e-1/end', r => { calls.ends++; status = 'Ended'; return r.fulfill({ json: encounter('Ended') }); });
  await page.route('**/v1/patients/*/access-requests', r => { calls.requests.push({ url: r.request().url(), body: r.request().postDataJSON() }); return r.fulfill({ json: { id: 'r-1', status: 'Pending' } }); });
  await page.goto('/front-desk');
  return calls;
}

test('the doctor starts the consultation from the board and sees who it is for', async ({ page }) => {
  const calls = await open(page);
  await expect(page.getByRole('button', { name: 'Start consultation with Deepak Nair (synthetic)' })).toHaveCount(0); // not arrived yet
  await page.getByRole('button', { name: 'Start consultation with Meera Iyer (synthetic)' }).click();
  await expect(page).toHaveURL(/\/consult\/e-1$/);
  const banner = page.getByRole('region', { name: 'Patient identity' });
  await expect(banner.getByRole('heading', { name: 'Meera Iyer (synthetic)' })).toBeVisible();
  await expect(banner).toContainText('54 years · Female');
  await expect(banner).toContainText('000001'); // the MRN is isolated left-to-right for Arabic screens
  await expect(banner).toContainText('Allergies: not recorded yet'); // unknown is never shown as "none"
  await expect(banner).toContainText('Treatment consent: given');
  await expect(banner).toContainText('10:05 · Room 3'); // facility time, not New York
  expect(calls.starts).toEqual([{ appointmentId: 'a1', leaveOpen: false }]);

  await page.getByRole('button', { name: 'End consultation' }).click();
  await expect(page.getByRole('status')).toHaveText('Consultation ended at 10:25.');
  await expect(page.getByRole('button', { name: 'End consultation' })).toHaveCount(0);
  expect(calls.ends).toBe(1);
  await page.getByRole('link', { name: 'Back to the board' }).click();
  await expect(page).toHaveURL(/\/front-desk$/);
});

test('with another consultation open, the doctor returns to it or leaves it open', async ({ page }) => {
  const calls = await open(page, { start: { status: 409, json: { code: 'open_encounter', encounterId: 'e-1' } } });
  await page.getByRole('button', { name: 'Start consultation with Meera Iyer (synthetic)' }).click();
  const choice = page.getByRole('alert').filter({ hasText: 'You have another consultation open' });
  await expect(choice).toBeVisible();
  await choice.getByRole('button', { name: 'Leave it open and start this one' }).click();
  await expect(page).toHaveURL(/\/consult\/e-1$/);
  expect(calls.starts).toEqual([{ appointmentId: 'a1', leaveOpen: false }, { appointmentId: 'a1', leaveOpen: true }]);
});

test('without a care relationship the doctor asks for access, giving a reason from the list', async ({ page }) => {
  const calls = await open(page, { start: { status: 403, json: { code: 'no_care_relationship', requestPending: false } } });
  await page.getByRole('button', { name: 'Start consultation with Meera Iyer (synthetic)' }).click();
  const panel = page.getByRole('alert').filter({ hasText: "You are not this patient's clinician here" });
  await panel.getByRole('combobox', { name: 'Why you need access' }).click();
  await page.getByRole('option', { name: 'Covering for a colleague' }).click();
  await panel.getByRole('button', { name: 'Request access' }).click();
  await expect(page.getByRole('alert').filter({ hasText: 'Access requested. An owner of the clinic will decide.' })).toBeVisible();
  expect(calls.requests).toEqual([{ url: expect.stringContaining('/v1/patients/p-a1/access-requests'), body: { reason: 'CoveringColleague' } }]);
  await expect(page).toHaveURL(/\/front-desk$/);
});

test('a role that cannot open consultations is told so, with nothing to ask for', async ({ page }) => {
  await open(page, { start: { status: 403, json: { code: 'no_access', requestPending: false } } });
  await page.getByRole('button', { name: 'Start consultation with Meera Iyer (synthetic)' }).click();
  const panel = page.getByRole('alert').filter({ hasText: 'Your role cannot open consultations.' });
  await expect(panel).toBeVisible();
  await expect(panel.getByRole('button')).toHaveCount(0);
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
for (const locale of ['en', 'ar'])
  test(`axe finds no violations on the consultation (${locale}); 44 px targets`, async ({ page }) => {
    await open(page, { locale });
    await page.goto('/consult/e-1');
    await expect(page.getByRole('region', { name: locale === 'ar' ? 'هوية المريض' : 'Patient identity' })).toBeVisible();
    if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    for (const b of await page.getByRole('main').getByRole('button').all()) expect((await b.boundingBox())!.height).toBeGreaterThanOrEqual(44);
    await page.addScriptTag({ path: axePath });
    const violations = await page.evaluate(async () =>
      (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
        .map(v => `${v.id} (${v.nodes.length})`));
    expect(violations).toEqual([]);
  });
