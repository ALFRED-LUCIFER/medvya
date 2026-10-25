import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';

// MDV-P1-011 / MDV-P1-021 / MDV-P1-023: the P-04 day, week and list views and booking from an empty slot. CI has no API, so /v1/me/context and the
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
// A doctor whose calendar is d-1 (the calendar id is the doctor's user id), with read-only booking.
const doctor = { ...persona('HospitalDoctor', 'Dr. Arjun Rao (synthetic)', { RegisterBookPatients: 'Read', Patient360Chart: 'Full' }), user: { id: 'd-1', displayName: 'Dr. Arjun Rao (synthetic)' } };

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
        calendarId: 'd-1', calendarKind: 'Doctor', name: 'Dr. Arjun Rao (synthetic)', slotMinutes: 20, visitLengths: lengths(20), capacity: 17,
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
        calendarId: 'd-2', calendarKind: 'Doctor', name: 'Dr. S. Menon (synthetic)', slotMinutes: 20, visitLengths: lengths(20), capacity: 0, hours: [], slots: [], appointments: [],
        unavailable: [{ start: at('09:00'), end: at('20:00'), reason: 'NotWorking', label: null }],
      },
      {
        calendarId: 'r-1', calendarKind: 'Resource', name: 'Procedure room (synthetic)', slotMinutes: 30, visitLengths: lengths(30), capacity: 6,
        appointments: [{ id: 'a-9', reference: 'APT-9C9C9C9C', patientId, patientName: 'Kavya Rao (synthetic)', visitType: 'Procedure', state: 'Cancelled', start: at('12:30'), end: at('13:00') }],
        hours: [{ start: at('10:00'), end: at('13:00'), room: null }], slots: slots(date, '10:00', '13:00', 30),
        unavailable: [{ start: at('09:00'), end: at('10:00'), reason: 'NotWorking', label: null }, { start: at('13:00'), end: at('20:00'), reason: 'NotWorking', label: null }],
      },
    ],
  };
}

/** GET /v1/schedule/week: Monday to Sunday of one calendar, each day that calendar's column of the day view; closed at weekends. */
function scheduleWeek(calendarId: string, date: string) {
  const asked = new Date(`${date}T12:00:00Z`);
  const monday = Date.parse(`${date}T12:00:00Z`) - ((asked.getUTCDay() + 6) % 7) * 86_400_000;
  const days = [...Array(7).keys()].map(i => {
    const d = scheduleDay(new Date(monday + i * 86_400_000).toISOString().slice(0, 10));
    const column = d.columns.find(c => c.calendarId === calendarId)!;
    return { ...d, columns: [i < 5 ? column : { ...column, hours: [], slots: [], appointments: [], capacity: 0, unavailable: [] }] };
  });
  return { facilityId: 'f-1', timeZone: 'Asia/Kolkata', calendarId, days };
}

type Calls = {
  changes: { url: string; body: Record<string, unknown> }[];
  weeks: { calendarId: string | null; date: string | null }[];
  dates: (string | null)[]; holds: Record<string, unknown>[]; releases: string[]; searches: { body: Record<string, unknown>; url: string }[];
  bookings: { body: Record<string, unknown>; csrf: string | undefined }[];
};

// POST /v1/patients/search (MDV-P1-018): privacy-safe rows only. "Nair" finds Deepak Nair (synthetic), anything else nobody.
const nair = { id: patientId, initials: 'DN', ageYears: 47, ageEstimated: false, sexAtBirth: 'Male', mrnTail: '0002', status: 'Complete' };

/** Signs in and answers the scheduling API; returns what the page sent. `holdCode` makes holds fail with that 409 code. */
async function open(page: Page, { locale = 'en', status = 200, as = reception, holdCode = '', delayMs = 0, changeCode = '' } = {}) {
  const calls: Calls = { changes: [], weeks: [], dates: [], holds: [], releases: [], searches: [], bookings: [] };
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: as }));
  await page.route('**/v1/schedule?*', async r => {
    const url = new URL(r.request().url());
    expect(url.searchParams.get('facilityId')).toBe('f-1');
    calls.dates.push(url.searchParams.get('date'));
    if (delayMs) await new Promise(done => setTimeout(done, delayMs));
    return status === 200 ? r.fulfill({ json: scheduleDay(url.searchParams.get('date') ?? '2026-10-19') }) : r.fulfill({ status, json: {} });
  });
  await page.route('**/v1/schedule/week?*', r => {
    const url = new URL(r.request().url());
    expect(url.searchParams.get('facilityId')).toBe('f-1');
    const calendarId = url.searchParams.get('calendarId'), date = url.searchParams.get('date');
    calls.weeks.push({ calendarId, date });
    return r.fulfill({ json: scheduleWeek(calendarId!, date ?? '2026-10-19') });
  });
  await page.route('**/v1/slot-holds', r => {
    const body = r.request().postDataJSON() as { calendarId: string; start: string; visitType: string };
    calls.holds.push(body);
    if (holdCode) return r.fulfill({ status: 409, json: { title: 'Slot no longer available', code: holdCode } });
    const end = new Date(Date.parse(body.start) + (body.visitType === 'NewConsultation' ? 40 : 20) * 60_000).toISOString();
    return r.fulfill({ json: { id: `h-${calls.holds.length}`, facilityId: 'f-1', ...body, end, expiresAt: ist('2026-10-19', '08:02') } });
  });
  await page.route('**/v1/slot-holds/*', r => { calls.releases.push(r.request().url().split('/').pop()!); return r.fulfill({ status: 204 }); });
  await page.route('**/v1/patients/search', r => {
    const body = r.request().postDataJSON() as { query: string };
    calls.searches.push({ body, url: r.request().url() });
    const items = body.query?.toLowerCase().includes('nair') ? [nair] : [];
    return r.fulfill({ json: { matchedOn: 'Name', items, page: 1, pageSize: 20, total: items.length } });
  });
  await page.route('**/v1/schedule/patients/*', r =>
    r.request().url().includes(patientId) ? r.fulfill({ json: { id: patientId, fullName: 'Deepak Nair (synthetic)', mrn: '000002' } }) : r.fulfill({ status: 404, json: {} }));
  await page.route('**/v1/appointments', r => {
    calls.bookings.push({ body: r.request().postDataJSON() as Record<string, unknown>, csrf: r.request().headers()['x-medvya-csrf'] });
    return r.fulfill({ json: { id: 'a-2', reference: 'APT-9F8E7D6C', patientName: 'Deepak Nair (synthetic)', mrn: '000002', calendarId: 'd-1', start: ist('2026-10-19', '10:00'), end: ist('2026-10-19', '10:40'), state: 'Booked' } });
  });
  // MDV-P1-024: reschedule, cancel, no-show and check-in answer the changed visit (or a 409 code).
  await page.route(/\/v1\/appointments\/[^/]+\/(reschedule|cancel|no-show|check-in)$/, r => {
    const url = r.request().url(), body = (r.request().postDataJSON() ?? {}) as Record<string, unknown>;
    calls.changes.push({ url, body });
    if (changeCode) return r.fulfill({ status: 409, json: { title: 'Visit cannot make this move', code: changeCode } });
    const base = scheduleDay('2026-10-19').columns[0]!.appointments[0]!;
    const state = url.endsWith('/cancel') ? 'Cancelled' : url.endsWith('/no-show') ? 'NoShow' : url.endsWith('/check-in') ? 'CheckedIn' : 'Booked';
    const start = (body.start as string | undefined) ?? base.start;
    return r.fulfill({ json: { ...base, state, start, end: new Date(Date.parse(start) + 40 * 60_000).toISOString() } });
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
  const search = panel.getByRole('searchbox', { name: 'Name, MRN, phone or ID' });
  await search.fill('Nobody Synthetic');
  await search.press('Enter'); // searches; does not submit the booking
  await expect(panel).toContainText('No patients match.');
  await expect(panel.getByRole('link', { name: 'Register a new patient' })).toHaveAttribute('href', '/patients/new');
  await search.fill('Nair');
  await panel.getByRole('button', { name: 'Search' }).click();
  const row = panel.getByRole('button', { name: 'D N, 47 y, Male, MRN ending 0002. Choose for this booking' });
  await expect(row).toHaveAttribute('aria-pressed', 'false');
  await expect(panel.getByRole('list', { name: 'Patients found' })).not.toContainText('Deepak'); // initials, age, sex and MRN tail only
  await row.click();
  await expect(row).toHaveAttribute('aria-pressed', 'true');
  await expect(panel).toContainText('Deepak Nair (synthetic) · MRN 000002'); // the audited lookup confirms the chosen patient
  expect(calls.searches.map(s => s.body.query)).toEqual(['Nobody Synthetic', 'Nair']);
  expect(calls.searches.every(s => !s.url.includes('Nair'))).toBe(true); // search text only in the body
  expect(calls.bookings).toEqual([]);
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
    await page.getByRole('button', { name: locale === 'ar' ? 'يوم' : 'Day', exact: true }).click(); // phones open the list (MDV-P1-023)
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
  await panel.getByRole('searchbox', { name: 'Name, MRN, phone or ID' }).focus();
  await page.keyboard.type('Nair');
  await page.keyboard.press('Enter');
  const row = panel.getByRole('button', { name: /^D N, 47 y, Male/ });
  await row.focus();
  await page.keyboard.press('Enter');
  await expect(panel).toContainText('Deepak Nair (synthetic) · MRN 000002');
  // A field error is tied to its field for screen readers (R14).
  const reason = panel.getByLabel('Reason in the patient\'s words (optional)');
  await reason.fill('x'.repeat(501));
  await panel.getByRole('button', { name: 'Confirm booking' }).click();
  await expect(reason).toHaveAccessibleDescription(/Keep the reason under 500 characters\./);
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

// MDV-P1-023: week and list views of the same data, the doctor's own view, phones and tablets.
const visits = /Booked|Checked in|In room|Completed|No-show|Cancelled/;
const squash = (texts: string[]) => texts.map(t => t.replace(/\s+/g, ' ').trim());

test('week and list views show the same appointments as the day view', async ({ page }) => {
  const calls = await open(page);
  const inDay = squash(await column(page, 'Dr. Arjun Rao (synthetic)').getByRole('listitem').filter({ hasText: visits }).allInnerTexts());
  expect(inDay).toHaveLength(1);

  await page.getByRole('button', { name: 'List', exact: true }).click();
  await expect(page.getByRole('button', { name: 'List', exact: true })).toHaveAttribute('aria-pressed', 'true');
  const raoList = page.getByRole('region', { name: 'Dr. Arjun Rao (synthetic)' });
  expect(squash(await raoList.getByRole('listitem').filter({ hasText: visits }).allInnerTexts()).map(t => t.replace(/^\S+ /, ''))).toEqual(inDay); // after the time
  await expect(raoList.getByRole('button', { name: 'Book Dr. Arjun Rao (synthetic) at 10:00' })).toBeVisible(); // empty slots stay bookable
  await expect(raoList.getByRole('listitem').filter({ hasText: 'Unavailable 12:00–13:00 · Break · Lunch break' })).toHaveCount(1);

  await page.getByRole('button', { name: 'Week', exact: true }).click();
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Mon, 19 Oct – Sun, 25 Oct');
  const week = page.getByRole('region', { name: 'Week of Dr. Arjun Rao (synthetic)' });
  const monday = week.getByRole('list', { name: 'Appointments on Monday, 19 October 2026' });
  expect(squash(await monday.getByRole('listitem').allInnerTexts())).toEqual(inDay);
  await expect(week.getByText('booked of 17')).toHaveCount(5); // Monday to Friday
  await expect(week.getByText('Not working')).toHaveCount(2); // Saturday and Sunday
  expect(calls.weeks).toEqual([{ calendarId: 'd-1', date: null }]); // all columns shown, so the first calendar's week

  await page.getByRole('combobox', { name: 'Doctor or resource' }).click();
  await page.getByRole('option', { name: 'Procedure room (synthetic)' }).click();
  await expect(page.getByRole('region', { name: 'Week of Procedure room (synthetic)' })).toBeVisible();
  await page.getByRole('button', { name: 'Next week' }).click();
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Mon, 26 Oct – Sun, 1 Nov');
  await expect.poll(() => calls.weeks.slice(1)).toEqual([{ calendarId: 'r-1', date: null }, { calendarId: 'r-1', date: '2026-10-26' }]);

  await page.getByRole('button', { name: 'Tue, 27 Oct' }).click(); // a day opens its day view
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Tuesday, 27 October 2026');
  await expect(page.getByRole('region', { name: 'Day schedule' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Procedure room (synthetic)' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Dr. Arjun Rao (synthetic)' })).toHaveCount(0); // the filter stays
});

test('every state is an icon and words, explained by the legend', async ({ page }) => {
  await open(page);
  const legend = page.getByRole('list', { name: 'What the colours and shapes mean' });
  await expect(legend.getByRole('listitem')).toHaveText(['Booked', 'Checked in', 'In room', 'Completed', 'No-show', 'Cancelled', 'Being booked', 'Unavailable']);
  for (const item of await legend.getByRole('listitem').all()) await expect(item.locator('svg')).toHaveCount(1);
});

test('a doctor starts on their own column, read-only, and can show everyone', async ({ page }) => {
  await open(page, { as: doctor });
  await expect(page.getByRole('combobox', { name: 'Doctor or resource' })).toHaveText('Dr. Arjun Rao (synthetic)');
  await expect(page.getByRole('heading', { name: 'Dr. Arjun Rao (synthetic)' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Procedure room (synthetic)' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: /^Book / })).toHaveCount(0); // free slots are listed, not buttons
  await expect(page.getByRole('complementary', { name: 'Book appointment' })).toHaveCount(0);
  await page.getByRole('combobox', { name: 'Doctor or resource' }).click();
  await page.getByRole('option', { name: 'All doctors and resources' }).click();
  await expect(page.getByRole('heading', { name: 'Procedure room (synthetic)' })).toBeVisible();
});

test('on a phone the list is the default, with a pinned Book that opens the next free slot', async ({ page }) => {
  await page.clock.setFixedTime(new Date(ist('2026-10-19', '10:05'))); // 10:00 has started; 10:20 is the next free slot
  await page.setViewportSize({ width: 390, height: 844 });
  const calls = await open(page);
  await expect(page.getByRole('button', { name: 'List', exact: true })).toHaveAttribute('aria-pressed', 'true');
  await expect(page.getByRole('region', { name: 'Day schedule' })).toHaveCount(0);
  const book = page.getByRole('button', { name: 'Book appointment' });
  const tabs = page.locator('nav.fixed');
  const [b, n] = [(await book.boundingBox())!, (await tabs.boundingBox())!];
  expect(b.y + b.height).toBeLessThanOrEqual(n.y); // pinned above the bottom tabs
  expect(b.height).toBeGreaterThanOrEqual(44);
  await page.evaluate(() => window.scrollTo(0, document.body.scrollHeight));
  await expect(book).toBeInViewport(); // still there at the end of the list
  await book.click();
  const panel = page.getByRole('complementary', { name: 'Book appointment' });
  await expect(panel).toContainText('Dr. Arjun Rao (synthetic) · 10:20–10:40');
  await expect(panel).toBeInViewport(); // brought into view above the list
  await expect(book).toBeHidden(); // never covers the panel's fields while a booking is under way
  expect(calls.holds[0]).toMatchObject({ calendarId: 'd-1', start: ist('2026-10-19', '10:20') });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

for (const width of [768, 1024])
  test(`on a ${width} px tablet the day view opens and the week scrolls inside its own box`, async ({ page }) => {
    await page.setViewportSize({ width, height: 1000 });
    await open(page);
    await expect(page.getByRole('button', { name: 'Day', exact: true })).toHaveAttribute('aria-pressed', 'true');
    await expect(page.getByRole('region', { name: 'Day schedule' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Book appointment' })).toBeHidden(); // the pinned Book is for phones
    await page.getByRole('button', { name: 'Week', exact: true }).click();
    const week = page.getByRole('region', { name: 'Week of Dr. Arjun Rao (synthetic)' });
    await expect(week.getByText('booked of 17').first()).toBeVisible();
    await week.focus();
    await expect(week).toBeFocused(); // keyboard can scroll it
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  });

for (const locale of ['en', 'ar'] as const)
  for (const view of ['Week', 'List'] as const)
    test(`axe finds no violations in the ${view.toLowerCase()} view, full width and at 200% zoom (${locale})`, async ({ page }) => {
      const name = { en: { Week: 'Week', List: 'List' }, ar: { Week: 'أسبوع', List: 'قائمة' } }[locale][view];
      await open(page, { locale });
      await page.getByRole('button', { name, exact: true }).click();
      await expect(page.getByRole('button', { name, exact: true })).toHaveAttribute('aria-pressed', 'true');
      await expect(page.getByText('Meera Iyer (synthetic)').first()).toBeVisible();
      for (const size of [{ width: 1280, height: 720 }, { width: 640, height: 720 }]) { // 640 px is a 1280 px screen at 200 %
        await page.setViewportSize(size);
        await page.addScriptTag({ path: axePath });
        const violations = await page.evaluate(async () =>
          (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
            .map(v => `${v.id} (${v.nodes.length})`));
        expect(violations).toEqual([]);
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
      }
    });

test('views and the filter are reachable by keyboard with 44 px targets', async ({ page }) => {
  await open(page);
  await page.getByRole('button', { name: 'Next day' }).focus();
  for (const name of ['Day', 'Week', 'List']) {
    await page.keyboard.press('Tab');
    await expect(page.getByRole('button', { name, exact: true })).toBeFocused();
    expect((await page.getByRole('button', { name, exact: true }).boundingBox())!.height).toBeGreaterThanOrEqual(44);
  }
  await page.keyboard.press('Enter'); // List
  await expect(page.getByRole('button', { name: 'List', exact: true })).toHaveAttribute('aria-pressed', 'true');
  await page.keyboard.press('Tab');
  await expect(page.getByRole('combobox', { name: 'Doctor or resource' })).toBeFocused();
  expect((await page.getByRole('combobox', { name: 'Doctor or resource' }).boundingBox())!.height).toBeGreaterThanOrEqual(44);
});

test('the list prints as an agenda: the schedule without navigation or controls', async ({ page }) => {
  await open(page);
  await page.getByRole('button', { name: 'List', exact: true }).click();
  await page.emulateMedia({ media: 'print' });
  await expect(page.getByRole('region', { name: 'Dr. Arjun Rao (synthetic)' })).toBeVisible();
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
  for (const nav of await page.getByRole('navigation').all()) await expect(nav).toBeHidden();
  await expect(page.getByRole('button', { name: 'Today' })).toBeHidden();
  await expect(page.getByRole('complementary', { name: 'Book appointment' })).toBeHidden();
});

// MDV-P1-024: the detail panel, reschedule (click or drag, always confirmed), cancel with a reason, no-show with rebook.
const meera = (page: Page) => column(page, 'Dr. Arjun Rao (synthetic)').getByRole('button', { name: /Meera Iyer \(synthetic\)/ });
const visitPanel = (page: Page) => page.getByRole('complementary', { name: 'Appointment' });

test('a visit opens its panel with each action allowed or explained', async ({ page }) => {
  await page.clock.setFixedTime(new Date(ist('2026-10-19', '08:30'))); // before the 09:00 visit
  await open(page);
  await meera(page).click();
  const panel = visitPanel(page);
  await expect(panel).toContainText('APT-0A1B2C3D');
  await expect(panel).toContainText('Mon, 19 Oct · 09:00 · 40 min');
  await expect(panel).toContainText('New consultation');
  for (const name of ['Check in', 'Reschedule', 'Cancel appointment']) await expect(panel.getByRole('button', { name, exact: true })).toBeEnabled();
  const noShow = panel.getByRole('button', { name: 'Mark no-show' });
  await expect(noShow).toBeDisabled();
  await expect(noShow).toHaveAccessibleDescription('Possible once the start time has passed.');
  await expect(page.getByRole('complementary', { name: 'Book appointment' })).toHaveCount(0); // one panel at a time

  await column(page, 'Procedure room (synthetic)').getByRole('button', { name: /Kavya Rao \(synthetic\)/ }).click();
  for (const name of ['Check in', 'Reschedule', 'Cancel appointment', 'Mark no-show'])
    await expect(visitPanel(page).getByRole('button', { name, exact: true })).toHaveAccessibleDescription('The visit is cancelled.');
  await visitPanel(page).getByRole('button', { name: 'Close' }).click();
  await expect(page.getByRole('complementary', { name: 'Book appointment' })).toBeVisible();
});

test('reschedule picks a new slot, asks first, then moves; the old slot frees on the server', async ({ page }) => {
  await page.clock.setFixedTime(new Date(ist('2026-10-19', '08:30')));
  const calls = await open(page);
  await meera(page).click();
  await visitPanel(page).getByRole('button', { name: 'Reschedule' }).click();
  await expect(visitPanel(page)).toContainText('Choose the new slot');
  const target = page.getByRole('button', { name: 'Move Meera Iyer (synthetic) to Dr. Arjun Rao (synthetic) at 10:20' });
  await target.click();
  const confirm = page.getByRole('group', { name: 'Move this appointment?' });
  await expect(confirm).toContainText('From Dr. Arjun Rao (synthetic), Mon, 19 Oct at 09:00 to Dr. Arjun Rao (synthetic), Mon, 19 Oct at 10:20.');
  expect(calls.changes).toEqual([]); // nothing moves before the desk confirms
  await confirm.getByRole('button', { name: 'Move appointment' }).click();
  await expect(visitPanel(page)).toContainText('Moved. The old slot is free again.');
  expect(calls.changes).toEqual([{ url: expect.stringMatching(/\/v1\/appointments\/a-1\/reschedule$/), body: { calendarId: 'd-1', start: ist('2026-10-19', '10:20') } }]);
});

test('dragging a visit onto an empty slot asks before moving it', async ({ page }) => {
  await page.clock.setFixedTime(new Date(ist('2026-10-19', '08:30')));
  const calls = await open(page);
  await meera(page).dragTo(page.getByRole('button', { name: 'Book Procedure room (synthetic) at 11:00' }));
  const confirm = page.getByRole('group', { name: 'Move this appointment?' });
  await expect(confirm).toContainText('to Procedure room (synthetic), Mon, 19 Oct at 11:00.');
  expect(calls.changes).toEqual([]);
  await confirm.getByRole('button', { name: 'Back' }).click(); // changed their mind: nothing sent
  await expect(confirm).toHaveCount(0);
  expect(calls.changes).toEqual([]);
});

test('cancel needs a reason, and a note for Other; the note never goes in a URL', async ({ page }) => {
  await page.clock.setFixedTime(new Date(ist('2026-10-19', '08:30')));
  const calls = await open(page);
  await meera(page).click();
  await visitPanel(page).getByRole('button', { name: 'Cancel appointment' }).click();
  const confirm = visitPanel(page).getByRole('button', { name: 'Cancel appointment' });
  await confirm.click();
  await expect(visitPanel(page).getByRole('combobox', { name: 'Reason' })).toHaveAccessibleDescription('Choose a reason.');
  await visitPanel(page).getByRole('combobox', { name: 'Reason' }).click();
  await page.getByRole('option', { name: 'Other' }).click();
  await confirm.click();
  await expect(visitPanel(page).getByLabel('Note (required for Other)')).toHaveAccessibleDescription(/Add a note for “Other”\./);
  expect(calls.changes).toEqual([]);
  await visitPanel(page).getByLabel('Note (required for Other)').fill('Synthetic: moving to another city');
  await confirm.click();
  await expect(visitPanel(page)).toContainText('Appointment cancelled. The slot is free again.');
  expect(calls.changes).toEqual([{ url: expect.stringMatching(/\/a-1\/cancel$/), body: { reason: 'Other', note: 'Synthetic: moving to another city' } }]);
  expect(calls.changes[0]!.url).not.toContain('Synthetic');
});

test('a no-show offers a rebook that fills in the patient', async ({ page }) => {
  await page.clock.setFixedTime(new Date(ist('2026-10-19', '09:10'))); // the 09:00 visit has started
  const calls = await open(page);
  await meera(page).click();
  await visitPanel(page).getByRole('button', { name: 'Mark no-show' }).click();
  await expect(visitPanel(page)).toContainText('Marked as a no-show. The slot is free again.');
  await visitPanel(page).getByRole('button', { name: 'Rebook Meera Iyer (synthetic)' }).click();
  const booking = page.getByRole('complementary', { name: 'Book appointment' });
  await expect(booking).toContainText('Rebook Meera Iyer (synthetic)');
  await bookButton(page, '10:00').click();
  await expect(booking).toContainText('· MRN 000002'); // the patient is already chosen
  expect(calls.changes.map(c => c.url.split('/').pop())).toEqual(['no-show']);
  expect(calls.holds).toHaveLength(1);
});

test('a refused change says why and refreshes the schedule', async ({ page }) => {
  await page.clock.setFixedTime(new Date(ist('2026-10-19', '08:30')));
  const calls = await open(page, { changeCode: 'changed' });
  const before = calls.dates.length;
  await meera(page).click();
  await visitPanel(page).getByRole('button', { name: 'Check in' }).click();
  await expect(page.getByRole('alert')).toContainText('Someone changed this appointment meanwhile.');
  await expect.poll(() => calls.dates.length).toBeGreaterThan(before);
});

for (const locale of ['en', 'ar'] as const)
  test(`the appointment panel is keyboard-operable with 44 px targets and axe-clean (${locale})`, async ({ page }) => {
    await page.clock.setFixedTime(new Date(ist('2026-10-19', '08:30')));
    await open(page, { locale });
    const chip = column(page, 'Dr. Arjun Rao (synthetic)').getByRole('button', { name: /Meera Iyer/ });
    await chip.focus();
    await page.keyboard.press('Enter');
    const panel = page.getByRole('complementary', { name: locale === 'ar' ? 'الموعد' : 'Appointment' });
    await expect(panel).toBeVisible();
    for (const b of await panel.getByRole('button').all()) expect((await b.boundingBox())!.height).toBeGreaterThanOrEqual(44);
    await panel.getByRole('button', { name: locale === 'ar' ? 'إلغاء الموعد' : 'Cancel appointment' }).focus();
    await page.keyboard.press('Enter');
    await expect(panel.getByRole('combobox')).toBeVisible();
    await page.addScriptTag({ path: axePath });
    const violations = await page.evaluate(async () =>
      (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
        .map(v => `${v.id} (${v.nodes.length})`));
    expect(violations).toEqual([]);
  });
