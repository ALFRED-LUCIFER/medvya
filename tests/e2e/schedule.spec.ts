import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';

// MDV-P1-011 / MDV-P1-021: the P-04 day view and booking from an empty slot. CI has no API, so /v1/me/context and the
// scheduling endpoints are mocked with the synthetic cast in the shapes the server sends (IScheduleService.cs,
// IBookingService.cs). The device runs in New York to prove times are shown in facility time.
test.use({ timezoneId: 'America/New_York' });

const persona = (role: string, displayName: string, capabilities: Record<string, string>) => ({
  user: { id: `u-${role}`, displayName },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role, flags: [], capabilities },
});
const reception = persona('Receptionist', 'A. Joseph', { RegisterBookPatients: 'Full', CheckInQueue: 'Full' });
const nurse = persona('Nurse', 'S. Thomas RN', { RegisterBookPatients: 'Read', CheckInQueue: 'Full', VitalsAllergiesEntry: 'Full' });

const patientId = '5e1d0001-0004-4000-8000-000000000001';

// IST is UTC+05:30: 09:00 IST = 03:30Z.
const ist = (date: string, hhmm: string) => {
  const [h, m] = hhmm.split(':').map(Number);
  return new Date(Date.parse(`${date}T00:00:00Z`) + ((h! * 60 + m!) - 330) * 60_000).toISOString();
};
const slots = (date: string, from: string, to: string, minutes: number, held: string[] = []) => {
  const out: { start: string; end: string; held: boolean }[] = [];
  for (let t = Date.parse(ist(date, from)); t + minutes * 60_000 <= Date.parse(ist(date, to)); t += minutes * 60_000) {
    const start = new Date(t).toISOString();
    out.push({ start, end: new Date(t + minutes * 60_000).toISOString(), held: held.includes(start) });
  }
  return out;
};
const lengths = (slot: number) => [
  { visitType: 'FollowUp', minutes: slot }, { visitType: 'NewConsultation', minutes: 2 * slot },
  { visitType: 'Procedure', minutes: slot }, { visitType: 'Teleconsult', minutes: slot },
];

function scheduleDay(date: string) {
  const at = (hhmm: string) => ist(date, hhmm);
  return {
    facilityId: 'f-1', timeZone: 'Asia/Kolkata', date, start: at('09:00'), end: at('20:00'),
    columns: [
      {
        calendarId: 'd-1', calendarKind: 'Doctor', name: 'Dr. Arjun Rao (synthetic)', slotMinutes: 20, visitLengths: lengths(20),
        hours: [{ start: at('09:00'), end: at('13:00'), room: 'Room 3' }, { start: at('17:00'), end: at('20:00'), room: 'Room 3' }],
        // 09:00–09:40 is a booked new consultation; 09:40 is being booked at another desk.
        slots: [...slots(date, '09:40', '11:55', 20, [at('09:40')]), ...slots(date, '17:00', '20:00', 20)],
        appointments: [{ id: 'a-1', reference: 'APT-0A1B2C3D', patientId, patientName: 'Meera Iyer (synthetic)', visitType: 'NewConsultation', state: 'Booked', start: at('09:00'), end: at('09:40') }],
        unavailable: [
          { start: at('11:55'), end: at('12:00'), reason: 'Break', label: 'Tea' }, // too short for its words
          { start: at('12:00'), end: at('13:00'), reason: 'Break', label: 'Lunch break' },
          { start: at('13:00'), end: at('17:00'), reason: 'NotWorking', label: null },
        ],
      },
      {
        calendarId: 'd-2', calendarKind: 'Doctor', name: 'Dr. S. Menon (synthetic)', slotMinutes: 20, visitLengths: lengths(20), hours: [], slots: [], appointments: [],
        unavailable: [{ start: at('09:00'), end: at('20:00'), reason: 'NotWorking', label: null }],
      },
      {
        calendarId: 'r-1', calendarKind: 'Resource', name: 'Procedure room (synthetic)', slotMinutes: 30, visitLengths: lengths(30), appointments: [],
        hours: [{ start: at('10:00'), end: at('13:00'), room: null }], slots: slots(date, '10:00', '13:00', 30),
        unavailable: [{ start: at('09:00'), end: at('10:00'), reason: 'NotWorking', label: null }, { start: at('13:00'), end: at('20:00'), reason: 'NotWorking', label: null }],
      },
    ],
  };
}

type Calls = { dates: (string | null)[]; holds: Record<string, unknown>[]; releases: string[]; bookings: { body: Record<string, unknown>; csrf: string | undefined }[] };

/** Signs in and answers the scheduling API; returns what the page sent. `holdCode` makes holds fail with that 409 code. */
async function open(page: Page, { locale = 'en', status = 200, as = reception, holdCode = '', delayMs = 0 } = {}) {
  const calls: Calls = { dates: [], holds: [], releases: [], bookings: [] };
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: as }));
  await page.route('**/v1/schedule?*', async r => {
    const url = new URL(r.request().url());
    expect(url.searchParams.get('facilityId')).toBe('f-1');
    calls.dates.push(url.searchParams.get('date'));
    if (delayMs) await new Promise(done => setTimeout(done, delayMs));
    return status === 200 ? r.fulfill({ json: scheduleDay(url.searchParams.get('date') ?? '2026-10-19') }) : r.fulfill({ status, json: {} });
  });
  await page.route('**/v1/slot-holds', r => {
    const body = r.request().postDataJSON() as { calendarId: string; start: string; visitType: string };
    calls.holds.push(body);
    if (holdCode) return r.fulfill({ status: 409, json: { title: 'Slot no longer available', code: holdCode } });
    const end = new Date(Date.parse(body.start) + (body.visitType === 'NewConsultation' ? 40 : 20) * 60_000).toISOString();
    return r.fulfill({ json: { id: `h-${calls.holds.length}`, facilityId: 'f-1', ...body, end, expiresAt: ist('2026-10-19', '08:02') } });
  });
  await page.route('**/v1/slot-holds/*', r => { calls.releases.push(r.request().url().split('/').pop()!); return r.fulfill({ status: 204 }); });
  await page.route('**/v1/schedule/patients/*', r =>
    r.request().url().includes(patientId) ? r.fulfill({ json: { id: patientId, fullName: 'Deepak Nair (synthetic)', mrn: '000002' } }) : r.fulfill({ status: 404, json: {} }));
  await page.route('**/v1/appointments', r => {
    calls.bookings.push({ body: r.request().postDataJSON() as Record<string, unknown>, csrf: r.request().headers()['x-medvya-csrf'] });
    return r.fulfill({ json: { id: 'a-2', reference: 'APT-9F8E7D6C', patientName: 'Deepak Nair (synthetic)', mrn: '000002', calendarId: 'd-1', start: ist('2026-10-19', '10:00'), end: ist('2026-10-19', '10:40'), state: 'Booked' } });
  });
  await page.goto('/schedule');
  return calls;
}

const column = (page: Page, name: string) => page.getByRole('list', { name });
const bookButton = (page: Page, time: string) => page.getByRole('button', { name: `Book Dr. Arjun Rao (synthetic) at ${time}` });

test('shows bookable slots exactly in the template windows, in facility time', async ({ page }) => {
  await open(page, { as: nurse }); // read-only: free slots are listed, not buttons
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Monday, 19 October 2026');
  await expect(page.getByText('Facility time · Asia/Kolkata')).toBeVisible();

  const rao = column(page, 'Dr. Arjun Rao (synthetic)');
  const free = rao.getByRole('listitem').filter({ hasText: 'Free' });
  await expect(free).toHaveCount(14);
  await expect(free.first()).toContainText('Free · 10:00'); // 04:30Z, not New York time
  await expect(free.last()).toContainText('Free · 19:40');
  await expect(rao).not.toContainText('Free · 12:00'); // lunch break
  await expect(rao.getByRole('listitem').filter({ hasText: 'Unavailable 12:00–13:00 · Break · Lunch break' })).toHaveCount(1);
  await expect(rao.getByRole('listitem').filter({ hasText: 'Unavailable 13:00–17:00 · Not working' })).toHaveCount(1);
  await expect(page.getByText('Room 3 · 20-min slots')).toBeVisible();
  await expect(page.getByText('Not in today')).toBeVisible(); // a doctor with no hours that day
  await expect(column(page, 'Procedure room (synthetic)').getByRole('listitem').filter({ hasText: 'Free' })).toHaveCount(6);

  // A 40-minute visit spans two 20-minute slots; its state is words and an icon, not colour.
  const visit = rao.getByRole('listitem').filter({ hasText: 'Meera Iyer (synthetic)' });
  await expect(visit).toContainText('Booked· 09:00 · New consultation');
  expect(Math.round((await visit.boundingBox())!.height)).toBeGreaterThanOrEqual(2 * 48 - 4);
  await expect(rao.getByRole('listitem').filter({ hasText: 'Being booked · 09:40' })).toHaveCount(1);
  await expect(page.getByRole('button', { name: /^Book / })).toHaveCount(0);
  await expect(page.getByRole('complementary', { name: 'Book appointment' })).toHaveCount(0);
});

test('moves between days and back to today', async ({ page }) => {
  const calls = await open(page);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Monday, 19 October 2026');
  await page.getByRole('button', { name: 'Next day' }).click();
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Tuesday, 20 October 2026');
  await page.getByRole('button', { name: 'Previous day' }).click();
  await page.getByRole('button', { name: 'Previous day' }).click();
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Sunday, 18 October 2026');
  await page.getByRole('button', { name: 'Today' }).click();
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Monday, 19 October 2026');
  expect(calls.dates.slice(0, 4)).toEqual([null, '2026-10-20', '2026-10-19', '2026-10-18']); // the server decides "today"
});

test('reception books an empty slot: hold, longer visit type, confirm the patient, book', async ({ page }) => {
  const calls = await open(page);
  await expect(bookButton(page, '09:40')).toHaveCount(0); // being booked elsewhere
  await expect(bookButton(page, '09:00')).toHaveCount(0); // already booked
  await bookButton(page, '10:00').click();

  const panel = page.getByRole('complementary', { name: 'Book appointment' });
  await expect(panel).toContainText('Dr. Arjun Rao (synthetic) · 10:00–10:20');
  await expect(panel).toContainText('Held for you until 08:02');
  expect(calls.holds[0]).toEqual({ facilityId: 'f-1', calendarId: 'd-1', start: ist('2026-10-19', '10:00'), visitType: 'FollowUp' });

  await panel.getByRole('combobox', { name: 'Visit type' }).click();
  await page.getByRole('option', { name: 'New consultation · 40 min' }).click();
  await expect(panel).toContainText('10:00–10:40'); // a new hold covers both slots
  expect(calls.holds[1]).toMatchObject({ visitType: 'NewConsultation' });

  const confirm = panel.getByRole('button', { name: 'Confirm booking' });
  await expect(confirm).toBeDisabled(); // no patient yet
  await panel.getByLabel('Patient ID').fill('5e1d0001-0004-4000-8000-00000000ffff');
  await expect(panel).toContainText('No patient with this ID at this clinic.');
  await panel.getByLabel('Patient ID').fill(patientId);
  await expect(panel).toContainText('Deepak Nair (synthetic) · MRN 000002');
  await panel.getByRole('combobox', { name: 'Reason category' }).click();
  await page.getByRole('option', { name: 'Long-term condition review' }).click();
  await panel.getByLabel('Reason in the patient\'s words (optional)').fill('Synthetic: sugar readings high');
  await panel.getByRole('combobox', { name: 'Booked by' }).click();
  await page.getByRole('option', { name: 'Phone' }).click();
  await confirm.click();

  await expect(panel).toContainText('Appointment booked');
  await expect(panel).toContainText('Deepak Nair (synthetic) at 10:00 · APT-9F8E7D6C');
  expect(calls.bookings).toEqual([{
    body: { holdId: 'h-2', patientId, reasonCategory: 'ChronicReview', reasonText: 'Synthetic: sugar readings high', channel: 'Phone' },
    csrf: '1',
  }]);
  expect(page.url()).not.toContain('sugar'); // the free-text reason never goes in a URL
  expect(calls.releases).toEqual([]); // booking used the hold; nothing to release
});

test('closing the panel releases the hold, and a slot taken meanwhile says so', async ({ page }) => {
  const calls = await open(page);
  await bookButton(page, '10:20').click();
  const panel = page.getByRole('complementary', { name: 'Book appointment' });
  await expect(panel).toContainText('Held for you until');
  await panel.getByRole('button', { name: 'Cancel' }).click();
  await expect(panel).toContainText('Select an empty slot');
  expect(calls.releases).toEqual(['h-1']);

  await page.unrouteAll({ behavior: 'ignoreErrors' });
  await open(page, { holdCode: 'being_booked' });
  await bookButton(page, '10:20').click();
  await expect(page.getByRole('alert')).toContainText('Someone else is booking this slot right now. Choose another slot.');
  await expect(page.getByRole('button', { name: 'Confirm booking' })).toBeDisabled();
});

// MDV-P1-022: when the doctor-day lock stays taken through every retry, the desk is told plainly that nothing was saved.
for (const [locale, book, message] of [
  ['en', 'Book Dr. Arjun Rao (synthetic) at 10:20', 'This calendar is busy with other bookings right now. Nothing was saved; try again.'],
  ['ar', 'احجز مع Dr. Arjun Rao (synthetic) الساعة 10:20', 'هذا التقويم مشغول بحجوزات أخرى الآن. لم يُحفظ شيء؛ حاول مرة أخرى.'],
] as const)
  test(`a busy calendar says nothing was saved (${locale})`, async ({ page }) => {
    await open(page, { locale, holdCode: 'busy' });
    await page.getByRole('button', { name: book }).click();
    await expect(page.getByRole('alert')).toContainText(message);
  });

test('no access says so and is not retried', async ({ page }) => {
  const calls = await open(page, { status: 403 });
  await expect(page.getByRole('heading', { name: 'You don’t have access to this facility.' })).toBeVisible();
  await expect(page.getByRole('alert')).toHaveCount(0); // an access state, not a failure
  expect(calls.dates).toHaveLength(1);
});

test('a failure offers a retry', async ({ page }) => {
  await open(page, { status: 500 });
  await expect(page.getByRole('alert')).toContainText('The schedule could not be loaded', { timeout: 15_000 }); // after the controller's retries
  await expect(page.getByRole('button', { name: 'Try again' })).toBeVisible();
});

test('offline is said once, by the shell banner', async ({ page, context }) => {
  await page.route('**/v1/me/context', r => r.fulfill({ json: reception }));
  await page.route('**/v1/schedule?*', () => {}); // never answers
  await page.goto('/schedule');
  await expect(page.getByRole('status').filter({ hasText: 'Loading the schedule' })).toBeVisible();
  await context.setOffline(true);
  await expect(page.getByText('You are offline')).toHaveCount(1);
  await context.setOffline(false);
  await page.unrouteAll({ behavior: 'ignoreErrors' });
});

test('a fast double click on Next moves two days, and the old day is marked as updating meanwhile', async ({ page }) => {
  const calls = await open(page, { delayMs: 600 });
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Monday, 19 October 2026');
  const next = page.getByRole('button', { name: 'Next day' });
  await next.click();
  await next.click(); // before the 20th has answered
  await expect(page.getByRole('region', { name: 'Day schedule' })).toHaveAttribute('aria-busy', 'true');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Wednesday, 21 October 2026');
  await expect(page.getByRole('region', { name: 'Day schedule' })).not.toHaveAttribute('aria-busy');
  expect(calls.dates).toEqual([null, '2026-10-20', '2026-10-21']);
});

test('a break too short for its words keeps its reason as icon, tooltip and screen-reader text', async ({ page }) => {
  await open(page);
  const tea = column(page, 'Dr. Arjun Rao (synthetic)').getByRole('listitem').filter({ hasText: 'Unavailable 11:55–12:00 · Break · Tea' });
  await expect(tea).toHaveAttribute('title', 'Unavailable · Break · Tea');
  await expect(tea.locator('svg')).toBeVisible();
});

for (const locale of ['en', 'ar'])
  test(`when the columns overflow, the grid scrolls by keyboard with sticky headers and time axis (${locale})`, async ({ page }) => {
    await page.setViewportSize({ width: 520, height: 700 }); // narrower than three 168 px columns
    await open(page, { locale });
    const grid = page.getByRole('region', { name: locale === 'ar' ? 'جدول اليوم' : 'Day schedule' });
    await expect(grid).toBeVisible();
    expect(await grid.evaluate(e => e.scrollWidth > e.clientWidth && e.scrollHeight > e.clientHeight)).toBe(true);

    await grid.focus(); // reachable by keyboard (scrollable-region-focusable)
    await expect(grid).toBeFocused();
    const header = page.getByRole('heading', { name: 'Dr. Arjun Rao (synthetic)' });
    const axis = grid.locator('[aria-hidden] > span').first(); // the first time mark
    const [headerY, axisX] = [(await header.boundingBox())!.y, (await axis.boundingBox())!.x];
    await page.keyboard.press('PageDown');
    await grid.evaluate((e, rtl) => { e.scrollLeft = rtl ? -400 : 400; }, locale === 'ar'); // RTL scrolls towards negative
    await expect.poll(() => grid.evaluate(e => Math.abs(e.scrollLeft) > 0 && e.scrollTop > 0)).toBe(true);
    expect((await header.boundingBox())!.y).toBeCloseTo(headerY, 0); // column headers stick to the top
    expect((await axis.boundingBox())!.x).toBeCloseTo(axisX, 0); // times stick to the reading-start side

    await page.addScriptTag({ path: axePath });
    const violations = await page.evaluate(async () =>
      (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
        .map(v => `${v.id} (${v.nodes.length})`));
    expect(violations).toEqual([]);
  });

test('Arabic is right-to-left with the time axis on the reading-start side', async ({ page }) => {
  await open(page, { locale: 'ar' });
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
  await expect(page.getByRole('region', { name: 'جدول اليوم' })).toBeVisible();
  const first = (await column(page, 'Dr. Arjun Rao (synthetic)').boundingBox())!;
  const room = (await column(page, 'Procedure room (synthetic)').boundingBox())!;
  expect(first.x).toBeGreaterThan(room.x); // columns flow right to left
  await page.getByRole('button', { name: 'احجز مع Dr. Arjun Rao (synthetic) الساعة 10:00' }).click();
  await expect(page.getByRole('complementary', { name: 'حجز موعد' })).toContainText('محجوز لك حتى 08:02');
});

test('keyboard books without a mouse; 44 px targets; 200% zoom never scrolls the page sideways', async ({ page }) => {
  await page.setViewportSize({ width: 683, height: 768 }); // a 1366 px screen at 200 %
  await open(page);
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
  await page.getByRole('button', { name: 'Previous day' }).focus();
  await page.keyboard.press('Tab');
  await expect(page.getByRole('button', { name: 'Today' })).toBeFocused();
  for (const name of ['Previous day', 'Today', 'Next day']) expect((await page.getByRole('button', { name }).boundingBox())!.height).toBeGreaterThanOrEqual(44);
  expect((await bookButton(page, '10:00').boundingBox())!.height).toBeGreaterThanOrEqual(44);
  await bookButton(page, '10:00').focus();
  await page.keyboard.press('Enter');
  const panel = page.getByRole('complementary', { name: 'Book appointment' });
  await expect(panel).toContainText('Held for you until');
  await panel.getByLabel('Patient ID').focus();
  await page.keyboard.type(patientId);
  await expect(panel).toContainText('Deepak Nair (synthetic) · MRN 000002');
  for (const b of await panel.getByRole('button').all()) expect((await b.boundingBox())!.height).toBeGreaterThanOrEqual(44);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
for (const locale of ['en', 'ar'])
  test(`axe finds no violations on the schedule and the booking panel (${locale})`, async ({ page }) => {
    await open(page, { locale });
    await expect(column(page, 'Dr. Arjun Rao (synthetic)')).toBeVisible();
    await column(page, 'Dr. Arjun Rao (synthetic)').getByRole('button').first().click();
    await expect(page.getByRole('complementary').getByRole('button').first()).toBeVisible();
    await page.addScriptTag({ path: axePath });
    const violations = await page.evaluate(async () =>
      (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
        .map(v => `${v.id} (${v.nodes.length})`));
    expect(violations).toEqual([]);
  });
