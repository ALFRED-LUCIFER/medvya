import { createRequire } from 'node:module';
import { expect, test, type Page, type Request } from '@playwright/test';

// MDV-P1-027 waitlist: add with preferences, "Offer {time}" holds a slot and messages the patient, the patient's yes books it and takes
// them off the list, a no releases it. Not a step of J1-J6 (the journeys are the clinic-flow stories; the waitlist is a P-05 side panel
// of the desk), so it has its own spec. CI has no API: the session, schedule, patients and waitlist endpoints are mocked in the server's
// shapes with the synthetic cast. The real rules (hold, concurrency, window, access, message, audit) are covered by WaitlistTests on PostgreSQL.
const reception = {
  user: { id: 'u-joseph', displayName: 'A. Joseph' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role: 'Receptionist' }],
  active: {
    membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role: 'Receptionist', flags: [], country: 'IN',
    capabilities: { RegisterBookPatients: 'Full', CheckInQueue: 'Full', InvoicesPayments: 'Full', Patient360Chart: 'None', ClinicalNotes: 'None' },
  },
};
const nurse = { ...reception, active: { ...reception.active, role: 'Nurse', capabilities: { RegisterBookPatients: 'Read', CheckInQueue: 'Full' } } };
const ist = (hhmm: string) => {
  const [h, m] = hhmm.split(':').map(Number);
  return new Date(Date.parse('2026-10-19T00:00:00Z') + ((h! * 60 + m!) - 330) * 60_000).toISOString();
};
const waiting = {
  id: 'w-1', patientId: 'p-1', patientName: 'Meera Iyer (synthetic)', mrn: '000001', calendarId: null, calendarName: null, visitType: 'FollowUp', reasonCategory: 'General',
  days: ['Monday', 'Wednesday'], from: '09:00:00', until: '11:00:00', state: 'Waiting', requestedAt: ist('08:10'), offered: null, offerExpiresAt: null,
  match: { calendarId: 'd-1', calendarName: 'Dr. Arjun Rao (synthetic)', start: ist('09:40') }, followUp: null, dueOn: null,
};
// A doctor's follow-up from the consultation's plan (MDV-P2-043): a booking to make from its due day.
const noMatch = { ...waiting, id: 'w-2', patientId: 'p-2', patientName: 'Deepak Nair (synthetic)', mrn: '000002', days: [], from: null, until: null, requestedAt: ist('08:20'), match: null,
  followUp: 'P1M', dueOn: '2026-11-19' };

type Calls = { adds: Request[]; offers: Request[]; accepts: Request[]; declines: Request[] };

async function desk(page: Page, { locale = 'en', as = reception }: { locale?: string; as?: object } = {}) {
  const calls: Calls = { adds: [], offers: [], accepts: [], declines: [] };
  let entries: unknown[] = [structuredClone(waiting), structuredClone(noMatch)];
  const list = () => ({ facilityId: 'f-1', timeZone: 'Asia/Kolkata', now: ist('08:30'), entries });
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: as }));
  await page.route('**/v1/front-desk/board?*', r => r.fulfill({ json: { facilityId: 'f-1', timeZone: 'Asia/Kolkata', date: '2026-10-19', now: ist('08:30'), cards: [] } }));
  await page.route('**/v1/schedule?*', r => r.fulfill({
    json: { facilityId: 'f-1', timeZone: 'Asia/Kolkata', date: '2026-10-19', start: ist('08:00'), end: ist('21:00'),
      columns: [{ calendarId: 'd-1', calendarKind: 'Doctor', name: 'Dr. Arjun Rao (synthetic)', slotMinutes: 20, hours: [], slots: [], unavailable: [], appointments: [], visitLengths: [], capacity: 0 }] },
  }));
  await page.route('**/v1/schedule/patients/p-3?*', r => r.fulfill({ json: { id: 'p-3', fullName: 'Arjun Nair (synthetic)', mrn: '000003' } }));
  await page.route('**/v1/patients/search**', r => r.fulfill({ json: { matchedOn: 'Name', page: 1, pageSize: 20, total: 1,
    items: [{ id: 'p-3', initials: 'AN', fullName: 'Arjun Nair (synthetic)', ageYears: 36, ageEstimated: false, sexAtBirth: 'Male', mrnTail: '0003', mobileTail: '0003', status: 'Complete' }] } }));
  await page.route('**/v1/waitlist?*', r => r.fulfill({ json: list() }));
  await page.route('**/v1/waitlist', r => {
    calls.adds.push(r.request());
    entries = [...entries, { ...noMatch, id: 'w-3', patientId: 'p-3', patientName: 'Arjun Nair (synthetic)', mrn: '000003' }];
    return r.fulfill({ json: entries.at(-1) });
  });
  await page.route('**/v1/waitlist/w-1/offer', r => {
    calls.offers.push(r.request());
    entries[0] = { ...waiting, state: 'Offered', offered: waiting.match, offerExpiresAt: ist('09:00'), match: null };
    return r.fulfill({ json: entries[0] });
  });
  await page.route('**/v1/waitlist/w-1/accept', r => {
    calls.accepts.push(r.request());
    entries = entries.filter(e => (e as { id: string }).id !== 'w-1');
    return r.fulfill({ json: { id: 'a-1', calendarId: 'd-1', start: ist('09:40'), patientName: 'Meera Iyer (synthetic)' } });
  });
  await page.route('**/v1/waitlist/w-1/decline', r => {
    calls.declines.push(r.request());
    entries[0] = structuredClone(waiting);
    return r.fulfill({ json: entries[0] });
  });
  return calls;
}

test('a freed slot is offered, held and messaged; the patient accepting books it and leaves the list', async ({ page }) => {
  const calls = await desk(page);
  await page.goto('/front-desk');
  await page.getByRole('link', { name: 'Waitlist' }).click();
  await expect(page).toHaveURL('/front-desk/waitlist');
  await expect(page.getByRole('heading', { level: 1, name: 'Waitlist' })).toBeVisible();

  const entries = page.getByTestId('waitlist-entry');
  await expect(entries).toHaveCount(2);
  await expect(entries.first()).toContainText('Meera Iyer (synthetic) · MRN 000001'); // request order
  await expect(entries.first()).toContainText('Any doctor · Follow-up · Monday, Wednesday · 09:00 to 11:00');
  await expect(entries.nth(1)).toContainText('No free slot that suits this patient');
  await expect(entries.nth(1)).toContainText('Follow-up from the doctor: book from Thursday, 19 November 2026');
  await expect(entries.first()).not.toContainText('Follow-up from the doctor');
  await expect(entries.nth(1).getByRole('button', { name: /^Offer/ })).toHaveCount(0);

  await entries.first().getByRole('button', { name: /^Offer 19 Oct 2026 09:40/ }).click();
  expect(calls.offers[0]!.postDataJSON()).toEqual({ calendarId: 'd-1', start: ist('09:40') }); // the slot the desk saw, ids and an instant
  expect(calls.offers[0]!.headers()['x-medvya-csrf']).toBe('1');
  await expect(page.getByRole('status')).toContainText('Slot held and the patient has been messaged.');
  await expect(entries.first()).toContainText('Slot offered');
  await expect(entries.first()).toContainText('Held: 19 Oct 2026 09:40 with Dr. Arjun Rao (synthetic), until 09:00.');

  await entries.first().getByRole('button', { name: 'Patient accepted: book it' }).click();
  expect(calls.accepts).toHaveLength(1);
  await expect(page.getByRole('status')).toContainText('Meera Iyer (synthetic) is booked for 19 Oct 2026 09:40 and is off the waitlist.');
  await expect(entries).toHaveCount(1);
});

test('a declined offer releases the slot and the patient waits for the next one', async ({ page }) => {
  const calls = await desk(page);
  await page.goto('/front-desk/waitlist');
  await page.getByRole('button', { name: /^Offer/ }).click();
  await page.getByRole('button', { name: 'Patient declined' }).click();
  expect(calls.declines).toHaveLength(1);
  await expect(page.getByRole('status')).toContainText('Slot released.');
  await expect(page.getByTestId('waitlist-entry').first().getByRole('button', { name: /^Offer/ })).toBeVisible();
});

test('adding a patient sends preferences as ids, days and times, never in the URL', async ({ page }) => {
  const calls = await desk(page);
  await page.goto('/front-desk/waitlist');
  await page.getByRole('searchbox', { name: 'Name, MRN, phone or ID' }).fill('Arjun');
  await page.getByRole('button', { name: 'Search' }).click();
  await page.getByRole('button', { name: /Arjun Nair/ }).click();
  await expect(page.getByText('Patient: Arjun Nair (synthetic) · MRN 000003')).toBeVisible();
  await page.getByRole('checkbox', { name: 'Tuesday' }).check();
  await page.getByLabel('Earliest start').fill('17:00');
  await page.getByLabel('Latest start').fill('09:00');
  await page.getByRole('button', { name: 'Add to waitlist' }).click();
  await expect(page.getByText('The latest start must not be before the earliest start.')).toBeVisible();
  await page.getByLabel('Latest start').fill('19:00');
  await page.getByRole('button', { name: 'Add to waitlist' }).click();
  await expect(page.getByRole('status')).toContainText('Added to the waitlist.');
  expect(calls.adds[0]!.postDataJSON()).toMatchObject({ facilityId: 'f-1', patientId: 'p-3', calendarId: null, days: ['Tuesday'], from: '17:00:00', until: '19:00:00' });
  expect(calls.adds[0]!.url()).not.toContain('Arjun');
  await expect(page.getByTestId('waitlist-entry')).toHaveCount(3);
});

test('a role without full booking access gets no Waitlist link', async ({ page }) => {
  await desk(page, { as: nurse });
  await page.goto('/front-desk');
  await expect(page.getByRole('link', { name: 'Waitlist' })).toHaveCount(0);
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
for (const locale of ['en', 'ar'])
  test(`axe finds no violations on the waitlist (${locale}); 44 px targets; RTL`, async ({ page }) => {
    await desk(page, { locale });
    await page.goto('/front-desk/waitlist');
    await expect(page.getByTestId('waitlist-entry')).toHaveCount(2);
    if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    for (const b of await page.getByRole('main').getByRole('button').all()) if (await b.isVisible()) expect((await b.boundingBox())!.height).toBeGreaterThanOrEqual(44);
    await page.addScriptTag({ path: axePath });
    const violations = await page.evaluate(async () =>
      (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
        .map(v => `${v.id} (${v.nodes.length})`));
    expect(violations).toEqual([]);
  });
