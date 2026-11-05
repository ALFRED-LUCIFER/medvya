import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';

// MDV-P1-036: the reception screens on a desk tablet (768x1024 portrait, 1024x768 landscape), English and Arabic RTL.
// Every screen: no clipped controls (the page never scrolls sideways; only the board scrolls on its own), every control
// at least 44 px (WCAG 2.5.8 target), axe clean. Mocked API as in the other specs (CI has none).
const persona = (role: string, capabilities: Record<string, string>) => ({
  user: { id: `u-${role}`, displayName: 'A. Joseph' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role, flags: [] as string[], country: 'IN', capabilities },
});
const reception = persona('Receptionist', { RegisterBookPatients: 'Full', CheckInQueue: 'Full' });
const admin = persona('PracticeAdmin', { StaffRolesIntegrations: 'Config', RegisterBookPatients: 'Full', CheckInQueue: 'Full' });

const date = '2026-10-19';
const at = (hhmm: string) => new Date(`${date}T${hhmm}:00+05:30`).toISOString();
// 09:20 to 12:40 in 20-minute slots (09:00 is the booked visit below).
const slots = [...Array(10).keys()].map(i => {
  const start = Date.parse(at('09:20')) + i * 20 * 60_000;
  return { start: new Date(start).toISOString(), end: new Date(start + 20 * 60_000).toISOString(), held: false };
});
const day = {
  facilityId: 'f-1', timeZone: 'Asia/Kolkata', date, start: at('09:00'), end: at('13:00'),
  columns: ['d-1', 'd-2'].map(id => ({
    calendarId: id, calendarKind: 'Doctor', name: `Dr. ${id} (synthetic)`, slotMinutes: 20, capacity: 9, hours: [{ start: at('09:00'), end: at('13:00'), room: 'Room 3' }],
    visitLengths: ['FollowUp', 'NewConsultation', 'Procedure', 'Teleconsult'].map(visitType => ({ visitType, minutes: 20 })),
    slots, unavailable: [],
    appointments: [{ id: `a-${id}`, reference: 'APT-0A1B2C3D', patientId: 'p-1', patientName: 'Meera Iyer (synthetic)', visitType: 'FollowUp', state: 'Booked', start: at('09:00'), end: at('09:20') }],
  })),
};
const states = ['Booked', 'CheckedIn', 'WithNurse', 'ReadyForDoctor', 'InConsult', 'Billing', 'Done'];
const board = {
  facilityId: 'f-1', timeZone: 'Asia/Kolkata', date, now: at('10:32'),
  cards: states.map((state, i) => ({
    id: `c${i}`, reference: `APT-${i}`, patientId: `p-${i}`, patientName: `Meera Iyer (synthetic) ${i}`, dateOfBirth: '1972-03-12', registrationIncomplete: i === 0,
    calendarId: 'd-1', calendarName: 'Dr. Arjun Rao (synthetic)', visitType: 'FollowUp', reasonCategory: 'General', start: at('10:00'), end: at('10:20'), state, since: at('10:20'),
  })),
};

async function signIn(page: Page, as: typeof reception, locale: string) {
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: as }));
  await page.route('**/v1/front-desk/board?*', r => r.fulfill({ json: board }));
  await page.route('**/v1/schedule?*', r => r.fulfill({ json: day }));
  await page.route('**/v1/patients/search**', r => r.fulfill({ json: { matchedOn: 'Directory', items: [], page: 1, pageSize: 20, total: 0 } }));
  await page.route('**/v1/patients/duplicate-check', r => r.fulfill({ json: { candidates: [] } }));
  await page.route('**/v1/settings/locations', r => r.fulfill({ json: [{ id: 'f-1', name: 'Adyar (synthetic)', address: null, timeZone: 'Asia/Kolkata' }] }));
  await page.route('**/v1/settings/locations/*/hours', r => r.fulfill({ json: [] }));
  await page.route('**/v1/schedule/templates*', r => r.fulfill({ json: [] }));
}

/** Controls under 44 px, and anything poking out of the viewport outside a scroll container of its own. */
async function problems(page: Page) {
  return page.evaluate(() => {
    const out: string[] = [];
    const vw = document.documentElement.clientWidth;
    if (document.documentElement.scrollWidth > vw) out.push(`page scrolls sideways (${document.documentElement.scrollWidth} > ${vw})`);
    const controls = 'button, a[href], input:not([type=hidden]), select, textarea, [role=combobox], [role=tab], [role=checkbox], [role=switch], [role=radio]';
    for (const el of document.querySelectorAll<HTMLElement>(controls)) {
      if (el.matches('a[href="#main"]')) continue; // skip link: off-screen until focused
      // a checkbox, radio or switch is tapped through its label
      const r = (el.matches('input[type=checkbox], input[type=radio], [role=switch]') ? el.closest('label') ?? el : el).getBoundingClientRect();
      if (r.width <= 1 || r.height <= 1 || getComputedStyle(el).visibility === 'hidden') continue; // sr-only / hidden
      const inSentence = el.tagName === 'A' && !!el.closest('p'); // links inside a sentence are exempt (WCAG 2.5.8)
      const name = `${el.tagName.toLowerCase()} "${(el.getAttribute('aria-label') ?? el.textContent ?? '').trim().slice(0, 30)}"`;
      if (!inSentence && (r.height < 43.5 || r.width < 43.5)) out.push(`${name} is ${Math.round(r.width)}x${Math.round(r.height)}`);
      let scroller: HTMLElement | null = el.parentElement;
      while (scroller && !(scroller.scrollWidth > scroller.clientWidth && /auto|scroll/.test(getComputedStyle(scroller).overflowX))) scroller = scroller.parentElement;
      if (!scroller && (r.left < -0.5 || r.right > vw + 0.5)) out.push(`${name} is outside the viewport`);
    }
    return out;
  });
}

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
const screens = [
  { name: 'reception board', path: '/front-desk', as: reception },
  { name: 'calendar', path: '/schedule', as: reception },
  { name: 'registration', path: '/patients/new?mode=quick', as: reception },
  { name: 'patient search', path: '/patients/directory', as: reception },
  { name: 'settings hours', path: '/settings?tab=hours', as: admin },
] as const;

for (const [w, h] of [[768, 1024], [1024, 768]] as const)
  for (const locale of ['en', 'ar'])
    for (const s of screens)
      test(`${s.name} at ${w}x${h} (${locale}): nothing clipped, targets >= 44 px, axe clean`, async ({ page }) => {
        await page.setViewportSize({ width: w, height: h });
        await signIn(page, s.as, locale);
        await page.goto(s.path);
        await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
        await page.waitForLoadState('networkidle');
        if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
        expect(await problems(page)).toEqual([]);
        await page.addScriptTag({ path: axePath });
        const violations = await page.evaluate(async () => (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string }[] }> } }).axe.run()).violations.map(v => v.id));
        expect(violations).toEqual([]);
      });

// Before the first slot of the fixture day, so every slot is still bookable.
for (const [w, h] of [[768, 1024], [1024, 768]] as const)
  test(`the booking panel at ${w}x${h} opens over the calendar without clipping`, async ({ page }) => {
    await page.clock.setFixedTime(new Date(at('08:00')));
    await page.route('**/v1/slot-holds', r => r.fulfill({ json: { id: 'h-1', facilityId: 'f-1', calendarId: 'd-1', visitType: 'FollowUp', start: at('09:20'), end: at('09:40'), expiresAt: at('08:02') } }));
    await page.setViewportSize({ width: w, height: h });
    await signIn(page, reception, 'en');
    await page.goto('/schedule');
    await page.getByRole('button', { name: 'Book Dr. d-1 (synthetic) at 09:20' }).click();
    const panel = page.getByRole('complementary', { name: 'Book appointment' });
    await expect(panel).toBeVisible();
    await expect(panel).toBeInViewport({ ratio: 1 }); // reachable without scrolling the page back up
    expect(await problems(page)).toEqual([]);
  });

test.describe('touch tablet', () => {
  test.use({ hasTouch: true, viewport: { width: 768, height: 1024 } });
  test('check-in is two taps on the board', async ({ page }) => {
    await signIn(page, reception, 'en');
    const checkedIn = page.waitForRequest('**/v1/appointments/c0/check-in');
    await page.route('**/v1/appointments/*/check-in', r => r.fulfill({ json: { ...board.cards[0], state: 'CheckedIn' } }));
    await page.goto('/front-desk');
    const card = page.getByRole('article', { name: 'Meera Iyer (synthetic) 0' });
    await card.getByRole('button', { name: 'Check in' }).tap();
    await card.getByRole('button', { name: 'Confirm arrival' }).tap();
    await checkedIn;
  });
});

test('the board scrolls sideways on its own at 768 px, not the page', async ({ page }) => {
  await page.setViewportSize({ width: 768, height: 1024 });
  await signIn(page, reception, 'en');
  await page.goto('/front-desk');
  const scroller = page.getByRole('region', { name: 'Visits by status' });
  await expect(scroller).toBeVisible();
  const [scrolls, pageScrolls] = await scroller.evaluate(el => [el.scrollWidth > el.clientWidth, document.documentElement.scrollWidth > document.documentElement.clientWidth]);
  expect(scrolls).toBe(true);
  expect(pageScrolls).toBe(false);
});
