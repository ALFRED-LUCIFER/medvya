import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';

// MDV-P1-011: the P-04 day view. CI has no API, so /v1/me/context and /v1/schedule are mocked with the synthetic cast in the
// shapes the server sends (IScheduleService.cs). The device runs in New York to prove times are shown in facility time.
test.use({ timezoneId: 'America/New_York' });

const reception = {
  user: { id: 'u-joseph', displayName: 'A. Joseph' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role: 'Receptionist' }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role: 'Receptionist', flags: [], capabilities: { RegisterBookPatients: 'Full', CheckInQueue: 'Full' } },
};

// IST is UTC+05:30: 09:00 IST = 03:30Z.
const ist = (date: string, hhmm: string) => {
  const [h, m] = hhmm.split(':').map(Number);
  return new Date(Date.parse(`${date}T00:00:00Z`) + ((h! * 60 + m!) - 330) * 60_000).toISOString();
};
const slots = (date: string, from: string, to: string, minutes: number) => {
  const out: { start: string; end: string }[] = [];
  for (let t = Date.parse(ist(date, from)); t + minutes * 60_000 <= Date.parse(ist(date, to)); t += minutes * 60_000)
    out.push({ start: new Date(t).toISOString(), end: new Date(t + minutes * 60_000).toISOString() });
  return out;
};

function scheduleDay(date: string) {
  const at = (hhmm: string) => ist(date, hhmm);
  return {
    facilityId: 'f-1', timeZone: 'Asia/Kolkata', date, start: at('09:00'), end: at('20:00'),
    columns: [
      {
        calendarId: 'd-1', calendarKind: 'Doctor', name: 'Dr. Arjun Rao (synthetic)', slotMinutes: 20,
        hours: [{ start: at('09:00'), end: at('13:00'), room: 'Room 3' }, { start: at('17:00'), end: at('20:00'), room: 'Room 3' }],
        slots: [...slots(date, '09:00', '12:00', 20), ...slots(date, '17:00', '20:00', 20)],
        unavailable: [
          { start: at('12:00'), end: at('13:00'), reason: 'Break', label: 'Lunch break' },
          { start: at('13:00'), end: at('17:00'), reason: 'NotWorking', label: null },
        ],
      },
      {
        calendarId: 'd-2', calendarKind: 'Doctor', name: 'Dr. S. Menon (synthetic)', slotMinutes: 20, hours: [], slots: [],
        unavailable: [{ start: at('09:00'), end: at('20:00'), reason: 'NotWorking', label: null }],
      },
      {
        calendarId: 'r-1', calendarKind: 'Resource', name: 'Procedure room (synthetic)', slotMinutes: 30,
        hours: [{ start: at('10:00'), end: at('13:00'), room: null }], slots: slots(date, '10:00', '13:00', 30),
        unavailable: [{ start: at('09:00'), end: at('10:00'), reason: 'NotWorking', label: null }, { start: at('13:00'), end: at('20:00'), reason: 'NotWorking', label: null }],
      },
    ],
  };
}

/** Signs in as reception and answers /v1/schedule; returns the dates the page asked for (null = "today"). */
async function open(page: Page, { locale = 'en', status = 200 } = {}) {
  const asked: (string | null)[] = [];
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: reception }));
  await page.route('**/v1/schedule?*', r => {
    const url = new URL(r.request().url());
    expect(url.searchParams.get('facilityId')).toBe('f-1');
    asked.push(url.searchParams.get('date'));
    return status === 200 ? r.fulfill({ json: scheduleDay(url.searchParams.get('date') ?? '2026-10-19') }) : r.fulfill({ status, json: {} });
  });
  await page.goto('/schedule');
  return asked;
}

const column = (page: Page, name: string) => page.getByRole('list', { name });

test('shows bookable slots exactly in the template windows, in facility time', async ({ page }) => {
  await open(page);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Monday, 19 October 2026');
  await expect(page.getByText('Facility time · Asia/Kolkata')).toBeVisible();

  const rao = column(page, 'Dr. Arjun Rao (synthetic)');
  const free = rao.getByRole('listitem').filter({ hasText: 'Free' });
  await expect(free).toHaveCount(18);
  await expect(free.first()).toContainText('Free · 09:00'); // 03:30Z, not New York time
  await expect(free.last()).toContainText('Free · 19:40');
  await expect(rao).not.toContainText('Free · 12:00'); // lunch break
  await expect(rao.getByRole('listitem').filter({ hasText: 'Unavailable 12:00–13:00 · Break · Lunch break' })).toHaveCount(1);
  await expect(rao.getByRole('listitem').filter({ hasText: 'Unavailable 13:00–17:00 · Not working' })).toHaveCount(1);
  await expect(page.getByText('Room 3 · 20-min slots')).toBeVisible();

  await expect(page.getByText('Not in today')).toBeVisible(); // a doctor with no hours that day
  await expect(column(page, 'Procedure room (synthetic)').getByRole('listitem').filter({ hasText: 'Free' })).toHaveCount(6);
});

test('moves between days and back to today', async ({ page }) => {
  const asked = await open(page);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Monday, 19 October 2026');
  await page.getByRole('button', { name: 'Next day' }).click();
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Tuesday, 20 October 2026');
  await page.getByRole('button', { name: 'Previous day' }).click();
  await page.getByRole('button', { name: 'Previous day' }).click();
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Sunday, 18 October 2026');
  await page.getByRole('button', { name: 'Today' }).click();
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Monday, 19 October 2026');
  expect(asked.slice(0, 4)).toEqual([null, '2026-10-20', '2026-10-19', '2026-10-18']); // the server decides "today"
});

test('no access says so and is not retried', async ({ page }) => {
  const asked = await open(page, { status: 403 });
  await expect(page.getByRole('alert')).toContainText('You don’t have access to this facility.');
  expect(asked).toHaveLength(1);
});

test('a failure offers a retry', async ({ page }) => {
  await open(page, { status: 500 });
  await expect(page.getByRole('alert')).toContainText('The schedule could not be loaded', { timeout: 15_000 }); // after the controller's retries
  await expect(page.getByRole('button', { name: 'Try again' })).toBeVisible();
});

test('offline says the schedule loads on reconnect', async ({ page, context }) => {
  await page.route('**/v1/me/context', r => r.fulfill({ json: reception }));
  await page.route('**/v1/schedule?*', () => {}); // never answers
  await page.goto('/schedule');
  await expect(page.getByRole('status').filter({ hasText: 'Loading the schedule' })).toBeVisible();
  await context.setOffline(true);
  await expect(page.getByRole('alert').filter({ hasText: 'The schedule loads when you reconnect.' })).toBeVisible();
  await context.setOffline(false);
  await page.unrouteAll({ behavior: 'ignoreErrors' });
});

test('Arabic is right-to-left with the time axis on the reading-start side', async ({ page }) => {
  await open(page, { locale: 'ar' });
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
  const grid = page.getByRole('region', { name: 'جدول اليوم' });
  await expect(grid).toBeVisible();
  const first = (await column(page, 'Dr. Arjun Rao (synthetic)').boundingBox())!;
  const room = (await column(page, 'Procedure room (synthetic)').boundingBox())!;
  expect(first.x).toBeGreaterThan(room.x); // columns flow right to left
  await expect(column(page, 'Dr. Arjun Rao (synthetic)').getByRole('listitem').first()).toContainText('متاح · 09:00');
});

test('keyboard reaches the day controls and 200% zoom never scrolls the page sideways', async ({ page }) => {
  await page.setViewportSize({ width: 683, height: 768 }); // a 1366 px screen at 200 %
  await open(page);
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
  await page.getByRole('button', { name: 'Previous day' }).focus();
  await page.keyboard.press('Tab');
  await expect(page.getByRole('button', { name: 'Today' })).toBeFocused();
  await page.keyboard.press('Tab');
  await expect(page.getByRole('button', { name: 'Next day' })).toBeFocused();
  for (const name of ['Previous day', 'Today', 'Next day']) expect((await page.getByRole('button', { name }).boundingBox())!.height).toBeGreaterThanOrEqual(44);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
for (const locale of ['en', 'ar'])
  test(`axe finds no violations on the schedule (${locale})`, async ({ page }) => {
    await open(page, { locale });
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    await expect(column(page, 'Dr. Arjun Rao (synthetic)')).toBeVisible();
    await page.addScriptTag({ path: axePath });
    const violations = await page.evaluate(async () =>
      (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
        .map(v => `${v.id} (${v.nodes.length})`));
    expect(violations).toEqual([]);
  });
