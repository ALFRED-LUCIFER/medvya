import { expect, type Locator, type Page } from '@playwright/test';
import { admin, board, calendar, fixtures, member, persona, reception, signIn, t } from './desk';
import { mockNurse } from './nurse';
import { mockProblems } from './problems';
import { mockReconciliation } from './reconciliation';

// MDV-P2-064: one screen per task (DESIGN.md Pinned chrome): top bar, content that fits, bottom bar. Measured, not pixel-compared.

/** The page scrolls at most a quarter of a screen (1.25 × the viewport); with `commit`, that action is fully on screen without scrolling. */
export async function fitsOneScreen(page: Page, commit?: Locator) {
  await page.evaluate(() => window.scrollTo(0, 0));
  const { height, viewport } = await page.evaluate(() => ({ height: document.documentElement.scrollHeight, viewport: window.innerHeight }));
  expect(height, `page is ${height} px tall in a ${viewport} px viewport`).toBeLessThanOrEqual(viewport * 1.25);
  if (commit) await commitVisible(page, commit);
}

/** The commit action is fully on screen with the page at the top, and nothing covers it. */
export async function commitVisible(page: Page, commit: Locator) {
  await page.evaluate(() => window.scrollTo(0, 0));
  await expect(commit).toBeInViewport({ ratio: 1 });
  const covered = await commit.evaluate(el => {
    const r = el.getBoundingClientRect(), hit = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
    return !hit || !(el.contains(hit) || hit.contains(el));
  });
  expect(covered, 'the commit action is covered').toBe(false);
}

/** A long list scrolls inside its own panel: the panel scrolls, the page does not move. */
export async function scrollsInsidePanel(page: Page, panel: Locator) {
  await page.evaluate(() => window.scrollTo(0, 0));
  const { scrolls } = await panel.evaluate(el => ({ scrolls: el.scrollHeight > el.clientHeight && /auto|scroll/.test(getComputedStyle(el).overflowY) }));
  expect(scrolls, 'the panel scrolls inside itself').toBe(true);
  await panel.evaluate(el => el.scrollTo(0, el.scrollHeight));
  expect(await page.evaluate(() => window.scrollY), 'the page stays still').toBe(0);
}

type Screen = {
  name: string;
  /** Opens the screen in `locale` with typical synthetic data, the API faked in the browser. */
  open: (page: Page, locale: string) => Promise<void>;
  /** The task's commit action, when it has one. */
  commit?: (page: Page, locale: string) => Locator;
};

const heading = (page: Page) => expect(page.getByRole('heading', { level: 1 })).toBeVisible();
const go = async (page: Page, path: string) => {
  await page.goto(path);
  await heading(page);
  await page.waitForLoadState('networkidle');
};
const desk = (as: typeof reception, path: string) => async (page: Page, locale: string) => {
  await signIn(page, as, locale);
  await go(page, path);
};
const button = (key: string) => (page: Page, locale: string) => page.getByRole('button', { name: t(locale, key), exact: true }).last();

// A week of opening hours and one doctor's schedule: what a set-up clinic shows.
const weekdays = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
const opening = weekdays.map(weekday => ({ weekday, opens: '09:00:00', closes: '17:00:00' }));
const template = { calendarId: 'd-1', calendarKind: 'Doctor', name: 'Dr. Arjun Rao (synthetic)', slotMinutes: 20, breaks: [], visitLengths: [], appointmentsOutsideHours: [],
  hours: weekdays.map(weekday => ({ weekday, start: '09:00:00', end: '13:00:00', room: null })) };

const doctor = persona('SoloDoctor', { RegisterBookPatients: 'Full', CheckInQueue: 'Full', Patient360Chart: 'Full' });

export const taskScreens: Screen[] = [
  { name: 'nurse intake', open: async (page, locale) => { await mockNurse(page, locale); await go(page, '/nurse/a1'); }, commit: button('intake.handOff') },
  { name: 'walk-in', open: desk(reception, '/front-desk/walk-in'), commit: button('walkIn.confirm') },
  {
    name: 'booking',
    open: async (page, locale) => {
      await calendar(page, locale);
      await heading(page);
      await page.getByRole('button', { name: t(locale, 'schedule.slot.book', { name: 'Dr. d-1 (synthetic)', time: '09:20' }) }).click();
      await expect(page.getByRole('complementary', { name: t(locale, 'booking.title') })).toBeVisible();
    },
    commit: (page, locale) => page.getByRole('complementary', { name: t(locale, 'booking.title') }).getByRole('button', { name: t(locale, 'booking.confirm') }),
  },
  { name: 'medicines', open: async (page, locale) => { await mockReconciliation(page, locale); await go(page, '/consult/e-1/medicines'); }, commit: button('reconcile.confirm') },
  { name: 'problems', open: async (page, locale) => { await mockProblems(page, locale); await go(page, '/consult/e-1/problems'); }, commit: button('problems.add.submit') },
  { name: 'settings profile', open: desk(admin, '/settings?tab=profile'), commit: button('settings.save') },
  {
    name: 'settings hours',
    open: async (page, locale) => {
      await signIn(page, admin, locale);
      await page.route('**/v1/settings/locations/*/hours', r => r.fulfill({ json: opening }));
      await page.route('**/v1/schedule/templates*', r => r.fulfill({ json: [template] }));
      await go(page, '/settings?tab=hours');
    },
    commit: (page, locale) => page.getByRole('button', { name: t(locale, 'settings.save'), exact: true }).first(),
  },
  {
    name: 'add doctor',
    open: async (page, locale) => { await desk(admin, '/settings?tab=doctors')(page, locale); await page.getByRole('button', { name: t(locale, 'doctors.add') }).first().click(); },
    commit: button('doctors.next'),
  },
  {
    name: 'add staff',
    open: async (page, locale) => { await desk(admin, '/settings?tab=staff')(page, locale); await page.getByRole('button', { name: t(locale, 'staff.add') }).click(); },
    commit: button('staff.next'),
  },
  {
    name: 'waitlist',
    open: async (page, locale) => {
      await signIn(page, reception, locale);
      const entry = (i: number) => ({ id: `w-${i}`, patientId: `p-${i}`, patientName: `Meera Iyer (synthetic) ${i}`, mrn: `00000${i}`, calendarId: null, calendarName: null,
        visitType: 'FollowUp', reasonCategory: 'General', days: ['Monday'], from: '09:00:00', until: '11:00:00', state: 'Waiting', requestedAt: board.now, offered: null,
        offerExpiresAt: null, match: null, followUp: null, dueOn: null });
      await page.route('**/v1/waitlist?*', r => r.fulfill({ json: { facilityId: 'f-1', timeZone: 'Asia/Kolkata', now: board.now, entries: [1, 2, 3, 4, 5, 6].map(entry) } }));
      await go(page, '/front-desk/waitlist');
    },
    commit: button('waitlist.addSubmit'),
  },
  {
    name: 'my day',
    open: async (page, locale) => {
      await signIn(page, doctor, locale);
      await page.route('**/v1/front-desk/board?*', r => r.fulfill({ json: { ...board, cards: board.cards.map(c => ({ ...c, calendarId: doctor.user.id })) } }));
      await page.route('**/v1/chart/allergy-flags', r => r.fulfill({ json: { items: [] } }));
      await page.route('**/v1/intake-escalations?*', r => r.fulfill({ json: [] }));
      await page.route('**/v1/prescriptions/pending', r => r.fulfill({ json: { more: false, items: [] } }));
      await page.route('**/v1/notes/pending', r => r.fulfill({ json: { more: false, items: [] } }));
      await go(page, '/today');
    },
    // The next patient's Start or Open consultation.
    commit: page => page.locator('section[aria-labelledby=today-next]').getByRole('button'),
  },
  { name: 'front desk board', open: desk(reception, '/front-desk') },
];

type ListScreen = { name: string; open: (page: Page, locale: string) => Promise<void>; panel: (page: Page, locale: string) => Locator };
const many = <T,>(n: number, row: (i: number) => T) => [...Array(n).keys()].map(row);
const audit = fixtures['**/v1/audit/access-log**'] as { items: { id: string }[] };
const clinician = persona('SoloDoctor', { RegisterBookPatients: 'Read', CheckInQueue: 'Full', Patient360Chart: 'Full' });
const innerTab = (page: Page) => page.getByRole('tabpanel').last();

/** Long lists (AC3): the page stays still and only the list's own panel scrolls. */
export const listScreens: ListScreen[] = [
  {
    name: 'front desk board',
    open: async (page, locale) => {
      await signIn(page, reception, locale);
      await page.route('**/v1/front-desk/board?*', r => r.fulfill({ json: { ...board, cards: many(40, i => ({ ...board.cards[1]!, id: `c${i}`, patientId: `p-${i}`, patientName: `Meera Iyer (synthetic) ${i}` })) } }));
      await go(page, '/front-desk');
    },
    panel: (page, locale) => page.getByRole('region', { name: t(locale, 'board.region') }),
  },
  {
    name: 'patient record history',
    open: async (page, locale) => {
      await signIn(page, clinician, locale);
      await page.route('**/v1/patients/p-1/chart', r => r.fulfill({ json: chart }));
      await page.route('**/v1/patients/p-1/trends', r => r.fulfill({ json: { timeZone: 'Asia/Kolkata', trends: [] } }));
      await page.route('**/v1/patients/p-1/timeline*', r => r.fulfill({ json: { timeZone: 'Asia/Kolkata', next: null,
        entries: many(30, i => ({ id: `e${i}`, kind: 'Encounter', at: `2026-0${1 + (i % 9)}-1${i % 10}T04:30:00Z`, code: 'Consultation', title: null, detail: null, author: 'Dr. Arjun Rao (synthetic)', source: 'ClinicianVerified' })) } }));
      await go(page, '/patients/p-1/timeline');
    },
    panel: (page, locale) => page.locator(`[aria-label="${t(locale, 'timeline.list')}"]`),
  },
  {
    name: 'audit log',
    open: async (page, locale) => {
      await signIn(page, admin, locale);
      await page.route('**/v1/audit/access-log**', r => r.fulfill({ json: { ...audit, items: many(50, i => ({ ...audit.items[0]!, id: `e${i}` })) } }));
      await go(page, '/settings?tab=audit');
    },
    panel: innerTab,
  },
  {
    name: 'staff',
    open: async (page, locale) => {
      await signIn(page, admin, locale);
      await page.route('**/v1/staff', r => r.fulfill({ json: many(30, i => ({ ...member, membershipId: `m${i}`, userId: `u${i}`, name: `Staff member ${i} (synthetic)`, email: `staff${i}@example.test` })) }));
      await go(page, '/settings?tab=staff');
    },
    panel: innerTab,
  },
  {
    name: 'directory',
    open: async (page, locale) => {
      await signIn(page, reception, locale);
      await page.route('**/v1/patients/search**', r => r.fulfill({ json: { matchedOn: 'Directory', page: 1, pageSize: 20, total: 60, items: many(20, i => ({ id: `p-${i}`, initials: 'MI',
        fullName: `Meera Iyer (synthetic) ${i}`, ageYears: 54, ageEstimated: false, sexAtBirth: 'Female', mrnTail: '0012', mobileTail: '0101', status: 'Complete' })) } }));
      await go(page, '/patients/directory');
    },
    panel: (page, locale) => page.getByRole('region', { name: t(locale, 'patients.tableLabel') }),
  },
];

// The chart the timeline's banner reads, in the server's shape (as timeline.spec.ts).
const chart = {
  patient: { id: 'p-1', fullName: 'Meera Iyer (synthetic)', mrn: '000001', dateOfBirth: '1972-03-12', ageYears: 54, sexAtBirth: 'Female', preferredLanguage: null }, treatmentConsent: true,
  timeZone: 'Asia/Kolkata', lifeStage: 'Adult', historySections: ['PastMedical', 'Surgical', 'Family', 'Social', 'WomensHealth'], pregnancy: { open: null, ruledOut: null },
  chart: { allergyState: 'NoKnownAllergies', allergies: [], problems: [], medicines: [], lastVitals: [], trends: [], recentEncounters: [], openEncounterId: null, lastLabs: [], bmi: null, history: [], bloodGroup: null, careTeam: [] },
};
