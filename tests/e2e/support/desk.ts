import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { expect, type Page } from '@playwright/test';

// MDV-P1-036: the desk screens, their mocked API (CI has none) and the layout and axe checks, shared by tablet.spec.ts and
// desk-access.spec.ts (keyboard and 200% zoom).
export const persona = (role: string, capabilities: Record<string, string>) => ({
  user: { id: `u-${role}`, displayName: 'A. Joseph' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role, flags: [] as string[], country: 'IN', capabilities },
});
export const reception = persona('Receptionist', { RegisterBookPatients: 'Full', CheckInQueue: 'Full' });
export const admin = persona('PracticeAdmin', { StaffRolesIntegrations: 'Config', RegisterBookPatients: 'Full', CheckInQueue: 'Full', AuditLog: 'Read' });

export const date = '2026-10-19';
export const at = (hhmm: string) => new Date(`${date}T${hhmm}:00+05:30`).toISOString();
// 09:20 to 12:40 in 20-minute slots (09:00 is the booked visit below).
export const slots = [...Array(10).keys()].map(i => {
  const start = Date.parse(at('09:20')) + i * 20 * 60_000;
  return { start: new Date(start).toISOString(), end: new Date(start + 20 * 60_000).toISOString(), held: false };
});
export const day = {
  facilityId: 'f-1', timeZone: 'Asia/Kolkata', date, start: at('09:00'), end: at('13:00'),
  columns: ['d-1', 'd-2'].map(id => ({
    calendarId: id, calendarKind: 'Doctor', name: `Dr. ${id} (synthetic)`, slotMinutes: 20, capacity: 9, hours: [{ start: at('09:00'), end: at('13:00'), room: 'Room 3' }],
    visitLengths: ['FollowUp', 'NewConsultation', 'Procedure', 'Teleconsult'].map(visitType => ({ visitType, minutes: 20 })),
    slots, unavailable: [],
    appointments: [{ id: `a-${id}`, reference: 'APT-0A1B2C3D', patientId: 'p-1', patientName: 'Meera Iyer (synthetic)', visitType: 'FollowUp', state: 'Booked', start: at('09:00'), end: at('09:20') }],
  })),
};
export const states = ['Booked', 'CheckedIn', 'WithNurse', 'ReadyForDoctor', 'InConsult', 'Billing', 'Done'];
export const board = {
  facilityId: 'f-1', timeZone: 'Asia/Kolkata', date, now: at('10:32'),
  cards: states.map((state, i) => ({
    id: `c${i}`, reference: `APT-${i}`, patientId: `p-${i}`, patientName: `Meera Iyer (synthetic) ${i}`, dateOfBirth: '1972-03-12', registrationIncomplete: i === 0,
    calendarId: 'd-1', calendarName: 'Dr. Arjun Rao (synthetic)', visitType: 'FollowUp', reasonCategory: 'General', start: at('10:00'), end: at('10:20'), state, since: at('10:20'),
  })),
};
// One synthetic row per list, in the server's shapes (the same fixtures as each screen's own spec), so tables and cards are laid out.
export const facility = { id: 'f-1', name: 'Adyar (synthetic)', address: null, timeZone: 'Asia/Kolkata' };
export const member = { membershipId: 'a', userId: 'u-rao', name: 'Dr. Arjun Rao (synthetic)', email: 'rao@example.test', role: 'SoloDoctor', customRoleId: null, customRoleName: null, facilityId: 'f-1',
  facilityName: 'Adyar (synthetic)', status: 'Invited', mfa: 'pending', invitationId: 'i-1', invitationState: 'pending', invitationExpiresAt: '2026-11-12T10:00:00Z' };
export const fixtures: Record<string, unknown> = {
  '**/v1/notifications/inbox?*': { unread: 0, items: [] },
  '**/v1/settings/terms': { version: '2026-11-draft', accepted: true, acceptedAt: '2026-11-01T04:00:00Z', required: true },
  '**/v1/settings/profile': { name: 'Iyer Family Clinic (synthetic)', address: '12 Test Street, Chennai', registrationNumber: 'CE-0001', registrationExpiresOn: null, taxNumber: null, phone: null, email: 'desk@clinic.example', hasLogo: false },
  '**/v1/settings/locations/*/closures': [],
  '**/v1/access-requests': { requests: [], grantDays: [1, 7, 30] },
  '**/v1/settings/clinical-access': { policy: 'AllFacilityClinicians' },
  '**/v1/settings/nurse-intake': { enabled: false },
  '**/v1/staff/access-review': { quarter: '2026-Q4', completedAt: null, lastCompletedAt: null,
    members: [{ membershipId: 'a', name: 'Dr. Arjun Rao (synthetic)', role: 'SoloDoctor', facilityName: 'Adyar (synthetic)', lastSignIn: null, decision: null }] },
  '**/v1/staff': [member],
  '**/v1/staff/roles': { builtIn: [], custom: [] },
  '**/v1/settings/doctors': {
    doctors: [{ id: 'd-1', fullName: 'Dr. Arjun Rao (synthetic)', specialty: 'Family medicine', qualifications: null, languages: null, facilityId: 'f-1', facilityName: 'Adyar (synthetic)',
      status: 'Active', authority: 'TNMC', number: '84213', category: null, expiresOn: null, expired: false, attestedBy: 'u-1', attestedByName: null, attestedAt: '2026-11-01T09:00:00Z', hasSignature: false }],
    defaults: { newVisitFee: 50000, followUpFee: 30000, currency: 'INR' }, authorities: [{ code: 'TNMC', name: 'Tamil Nadu Medical Council' }],
  },
  '**/v1/queue/displays?*': [{ id: 'q-1', facilityId: 'f-1', name: 'Lobby TV', createdAt: '2026-10-01T04:00:00Z', revokedAt: null }],
  '**/v1/audit/access-log**': { next: null, actions: ['patient.read'], items: [{ id: 'e1', occurredAt: '2026-10-19T05:30:00Z', actorUserId: 'u-rao', actorName: 'Dr. Arjun Rao (synthetic)',
    role: 'SoloDoctor', action: 'patient.read', resourceType: 'patient', patientId: 'p-1', patientMrn: '000001', patientName: null, purpose: 'treatment', outcome: 'Allowed', detail: null }] },
  '**/v1/schedule/metrics**': { from: '2026-10-05T05:30:00Z', to: '2026-10-19T05:30:00Z', checkIn: { returningMedianSeconds: 74, returningCount: 18, medianSeconds: 81, count: 25, targetSeconds: 60 },
    noShows: { noShows: 6, attended: 54, percent: 10 }, doubleBookingRefusals: 0, baselines: [] },
  '**/v1/notifications/delivery**': { channels: [{ channel: 'Email', total: 40, delivered: 36, failed: 2, pending: 2 }], failures: [] },
  '**/v1/settings/notifications': { confirmation: true, reminder24h: true, reminder2h: true, changes: true, email: true, sms: true, quietStart: '21:00:00', quietEnd: '08:00:00', senderName: null },
  '**/v1/settings/notifications/templates?*': [{ group: 'Reminder', subject: 'Appointment reminder', text: 'Reminder: your appointment at {clinic} is on {date} at {time}.' }],
  '**/v1/schedule/exceptions**': [{ id: 'x1', calendarId: 'd-1', kind: 'Leave', fromDate: '2026-10-19', toDate: '2026-10-21', start: null, end: null, reason: 'Conference', canEdit: true, version: 'v1' }],
  '**/v1/front-desk/overview?*': { facilityId: 'f-1', timeZone: 'Asia/Kolkata', now: at('10:32'),
    doctors: [{ calendarId: 'd-1', name: 'Dr. Arjun Rao (synthetic)', counts: Object.fromEntries(states.map(s => [s, 1])), delayMinutes: 30,
      next: [{ id: 'c1', start: at('10:00'), state: 'CheckedIn', patientName: 'Meera Iyer (synthetic) 1', token: 'A-02' }] }],
    waiting: [{ id: 'c1', token: 'A-02', patientName: 'Meera Iyer (synthetic) 1', calendarId: 'd-1', state: 'CheckedIn', start: at('10:00'), since: at('10:20') }] },
  '**/v1/patients/p-1': { id: 'p-1', mrn: '000012', fullName: 'Meera Iyer (synthetic)', arabicName: null, dateOfBirth: '1972-03-12', dateOfBirthEstimated: false, ageYears: 54,
    sexAtBirth: 'Female', genderIdentity: null, preferredLanguage: null, nationality: 'IN', mobile: '+919000000301', alternatePhone: null, email: null,
    address: null, status: 'Complete', registeredOn: '2026-10-02', identifiers: [], payer: null, emergencyContact: null, guardian: null },
  '**/v1/patients/p-1/merges': { merges: [] },
  '**/v1/patients/p-1/consents': { policyVersion: '2026-11-draft', decisions: [] },
};

export type Persona = typeof reception;
export async function signIn(page: Page, as: Persona, locale: string) {
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: as }));
  await page.route('**/v1/front-desk/board?*', r => r.fulfill({ json: board }));
  await page.route('**/v1/schedule?*', r => r.fulfill({ json: day }));
  await page.route('**/v1/patients/search**', r => r.fulfill({ json: { matchedOn: 'Directory', items: [], page: 1, pageSize: 20, total: 0 } }));
  await page.route('**/v1/patients/duplicate-check', r => r.fulfill({ json: { candidates: [] } }));
  await page.route('**/v1/settings/locations', r => r.fulfill({ json: [facility] }));
  await page.route('**/v1/settings/locations/*/hours', r => r.fulfill({ json: [] }));
  await page.route('**/v1/schedule/templates*', r => r.fulfill({ json: [] }));
  for (const [url, json] of Object.entries(fixtures)) await page.route(url, r => r.fulfill({ json }));
}

/** Controls under 44 px, covered, or poking out of the viewport outside a scroll container of its own; `within` limits it to a drawer. */
export async function problems(page: Page, within = 'body') {
  return page.evaluate(within => {
    const out: string[] = [];
    const vw = document.documentElement.clientWidth;
    if (document.documentElement.scrollWidth > vw) out.push(`page scrolls sideways (${document.documentElement.scrollWidth} > ${vw})`);
    const controls = 'button, a[href], input:not([type=hidden]), select, textarea, [role=combobox], [role=tab], [role=checkbox], [role=switch], [role=radio]';
    for (const el of document.querySelector(within)!.querySelectorAll<HTMLElement>(controls)) {
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
      // Covered by something else (a panel fixed over the page): the tap would land on the cover, not the control. Content under the
      // sticky top bar or the phone's bottom tabs scrolls out from under them, so those two are not covers.
      const [x, y] = [r.left + r.width / 2, r.top + r.height / 2];
      const top = x >= 0 && x < vw && y >= 0 && y < window.innerHeight ? document.elementFromPoint(x, y) : null;
      if (top && !el.contains(top) && !top.contains(el) && !el.closest('label')?.contains(top) && !top.closest('header, nav')) out.push(`${name} is covered by ${top.outerHTML.slice(0, 80)}`);
    }
    return out;
  }, within);
}

export const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
export const screens = [
  { name: 'reception board', path: '/front-desk', as: reception },
  { name: 'calendar', path: '/schedule', as: reception },
  { name: 'registration', path: '/patients/new?mode=quick', as: reception },
  { name: 'patient search', path: '/patients/directory', as: reception },
  { name: 'walk-in', path: '/front-desk/walk-in', as: reception },
  { name: 'clinic overview', path: '/front-desk/overview', as: reception },
  { name: 'leave and exceptions', path: '/schedule/leave', as: admin },
  { name: 'patient record', path: '/patients/directory/p-1', as: reception },
  ...['profile', 'locations', 'hours', 'holidays', 'staff', 'doctors', 'displays', 'audit', 'metrics', 'terms', 'notifications', 'data']
    .map(tab => ({ name: `settings ${tab}`, path: `/settings?tab=${tab}`, as: admin })),
];

/** axe rule ids that fail on the page as it is now. */
export async function axe(page: Page) {
  await page.addScriptTag({ path: axePath });
  return page.evaluate(async () => (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string }[] }> } }).axe.run()).violations.map(v => v.id));
}

/** Opens a screen signed in as `as`, waits for its heading and the network, and checks the language direction. */
export async function openScreen(page: Page, s: { path: string; as: Persona }, locale: string) {
  await signIn(page, s.as, locale);
  await page.goto(s.path);
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
  await page.waitForLoadState('networkidle');
  if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
}

export const meera = '5e1d0001-0036-4000-8000-000000000001'; // booking takes a real patient id (a UUID)
/** The calendar before its first slot, with a hold, a patient to find and a booking to make; returns the bookings sent. */
export async function calendar(page: Page, locale = 'en') {
  await page.clock.setFixedTime(new Date(at('08:00'))); // before the first slot, so every slot is bookable
  await signIn(page, reception, locale); // first: the routes below override its empty search
  const bookings: unknown[] = [];
  await page.route('**/v1/slot-holds', r => r.fulfill({ json: { id: 'h-1', facilityId: 'f-1', calendarId: 'd-1', visitType: 'FollowUp', start: at('09:20'), end: at('09:40'), expiresAt: at('08:02') } }));
  await page.route('**/v1/slot-holds/h-1', r => r.fulfill({ status: 204 }));
  await page.route('**/v1/patients/search**', r => r.fulfill({ json: { matchedOn: 'Name', page: 1, pageSize: 20, total: 1,
    items: [{ id: meera, initials: 'MI', fullName: 'Meera Iyer (synthetic)', ageYears: 54, ageEstimated: false, sexAtBirth: 'Female', mrnTail: '0012', mobileTail: '0101', status: 'Complete' }] } }));
  await page.route(`**/v1/schedule/patients/${meera}?*`, r => r.fulfill({ json: { id: meera, fullName: 'Meera Iyer (synthetic)', mrn: '000012' } }));
  await page.route('**/v1/appointments', r => {
    bookings.push(r.request().postDataJSON());
    return r.fulfill({ json: { id: 'a-9', reference: 'APT-9F8E7D6C', patientName: 'Meera Iyer (synthetic)', mrn: '000012', calendarId: 'd-1', start: at('09:20'), end: at('09:40'), state: 'Booked' } });
  });
  await page.goto('/schedule');
  return bookings;
}

const messages = (locale: string) => JSON.parse(readFileSync(new URL(`../../../packages/i18n/messages/${locale}.json`, import.meta.url), 'utf8')) as Record<string, string>;
/** A UI string as the app shows it in `locale`, so one flow runs in English and Arabic. */
export const t = (locale: string, key: string, values: Record<string, string> = {}) =>
  messages(locale)[key]!.replace(/\{(\w+)\}/g, (_, k: string) => values[k] ?? `{${k}}`);
