import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';
import { fakeHub } from './support/fakeHub';

// MDV-P1-028: the P-01 reception board and one-tap check-in. CI has no API, so /v1/me/context and the front-desk endpoints
// are mocked with the synthetic cast in the shapes the server sends (IFrontDeskService.cs). The device runs in New York to
// prove times are shown in facility time.
test.use({ timezoneId: 'America/New_York' });

const persona = (role: string, displayName: string, capabilities: Record<string, string>) => ({
  user: { id: `u-${role}`, displayName },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role, flags: [] as string[], capabilities },
});
const reception = persona('Receptionist', 'A. Joseph', { RegisterBookPatients: 'Full', CheckInQueue: 'Full' });
const hospitalDoctor = persona('HospitalDoctor', 'Dr. S. Menon', { RegisterBookPatients: 'Full', CheckInQueue: 'Read', Patient360Chart: 'Full' });

// IST is UTC+05:30: 09:00 IST = 03:30Z.
const ist = (hhmm: string) => {
  const [h, m] = hhmm.split(':').map(Number);
  return new Date(Date.parse('2026-10-19T00:00:00Z') + ((h! * 60 + m!) - 330) * 60_000).toISOString();
};
const now = ist('10:32');
type Card = Record<string, unknown> & { id: string; state: string };
const card = (id: string, name: string, time: string, state: string, extra: Partial<Card> = {}): Card => ({
  id, reference: `APT-${id.toUpperCase()}`, patientId: `p-${id}`, patientName: name, dateOfBirth: '1972-03-12', registrationIncomplete: false,
  calendarId: 'd-1', calendarName: 'Dr. Arjun Rao (synthetic)', visitType: 'FollowUp', reasonCategory: 'General', sex: 'Female',
  start: ist(time), end: ist(time), state, since: ist(time), ...extra,
});
const cards = () => [
  card('a1', 'Meera Iyer (synthetic)', '11:00', 'Booked', { reasonCategory: 'ChronicReview', registrationIncomplete: true }),
  card('a2', 'Farhan Qureshi (synthetic)', '10:20', 'CheckedIn', { since: ist('10:20'), token: 'A-07' }),
  card('a3', 'Lakshmi Pillai (synthetic)', '10:40', 'WithNurse', { reasonCategory: 'PostDischarge' }),
  card('a4', 'Vikram Desai (synthetic)', '10:00', 'InConsult', { visitType: 'NewConsultation' }),
  card('a5', 'Rahul Menon (synthetic)', '09:30', 'Billing'),
];

type Calls = { checkIns: { id: string; body: { elapsedMs: number }; csrf: string | undefined }[]; moves: { id: string; state: string }[]; boards: number;
  undos: { id: string; state: string }[]; ends: { id: string; how: string; reason: string }[] };

async function open(page: Page, { locale = 'en', as = reception, status = 200, moveCode = '', undoCode = '', empty = false, only = null as string[] | null, extra = [] as Card[] } = {}) {
  const calls: Calls = { checkIns: [], moves: [], boards: 0, undos: [], ends: [] };
  const before = new Map<string, string>(); // where each card was before its last move, for undo
  let left = 0;
  const state = empty ? [] : [...cards().filter(c => !only || only.includes(c.state)), ...extra];
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: as }));
  await page.route('**/v1/front-desk/board?*', r => {
    expect(new URL(r.request().url()).searchParams.get('facilityId')).toBe('f-1');
    calls.boards++;
    return status === 200 ? r.fulfill({ json: { facilityId: 'f-1', timeZone: 'Asia/Kolkata', date: '2026-10-19', now, cards: state, leftWithoutBeingSeen: left } }) : r.fulfill({ status, json: {} });
  });
  await page.route('**/v1/appointments/*/check-in', r => {
    const id = r.request().url().split('/').at(-2)!;
    calls.checkIns.push({ id, body: r.request().postDataJSON() as { elapsedMs: number }, csrf: r.request().headers()['x-medvya-csrf'] });
    const c = state.find(x => x.id === id)!;
    Object.assign(c, { state: 'CheckedIn', since: now });
    return r.fulfill({ json: c });
  });
  await page.route('**/v1/appointments/*/status', r => {
    const id = r.request().url().split('/').at(-2)!;
    const body = r.request().postDataJSON() as { state: string };
    calls.moves.push({ id, state: body.state });
    if (moveCode) return r.fulfill({ status: 409, json: { title: 'Visit cannot make this move', code: moveCode } });
    const c = state.find(x => x.id === id)!;
    before.set(id, c.state);
    Object.assign(c, { state: body.state, since: now });
    return r.fulfill({ json: c });
  });
  // MDV-P1-100: undo the last move; left without being seen and cancel take the card off the board.
  await page.route('**/v1/appointments/*/undo', r => {
    const id = r.request().url().split('/').at(-2)!;
    calls.undos.push({ id, state: (r.request().postDataJSON() as { state: string }).state });
    if (undoCode) return r.fulfill({ status: 409, json: { title: 'Visit cannot make this move', code: undoCode } });
    const c = state.find(x => x.id === id)!;
    Object.assign(c, { state: before.get(id), since: now });
    return r.fulfill({ json: c });
  });
  for (const how of ['left', 'cancel'])
    await page.route(`**/v1/appointments/*/${how}`, r => {
      const id = r.request().url().split('/').at(-2)!;
      calls.ends.push({ id, how, reason: (r.request().postDataJSON() as { reason: string }).reason });
      state.splice(state.findIndex(x => x.id === id), 1);
      if (how === 'left') left++;
      return r.fulfill({ json: {} });
    });
  await page.goto('/front-desk');
  return Object.assign(calls, { state }); // state: what the server's board holds now, for changes made at "another desk"
}

const column = (page: Page, name: string) => page.getByRole('region', { name: new RegExp(`^${name}`) });
const cardOf = (page: Page, name: string) => page.getByRole('article', { name });

test('shows today\'s visits by status with nonclinical tags, in facility time', async ({ page }) => {
  await open(page);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Reception board · Mon, 19 Oct');
  await expect(page.getByText('Receptionist view hides clinical notes')).toBeVisible();
  for (const [name, count] of [['Booked', 1], ['Checked in', 1], ['With nurse', 1], ['Ready for doctor', 0], ['In consult', 1], ['Billing', 1], ['Done', 0]] as const)
    await expect(column(page, name).getByRole('article')).toHaveCount(count);
  await expect(column(page, 'Ready for doctor')).toContainText('No visits');

  const meera = cardOf(page, 'Meera Iyer (synthetic)');
  await expect(meera).toContainText('11:00'); // 05:30Z shown in IST, not New York time
  await expect(meera).toContainText('54 y · Female'); // age and sex tell two patients of one name apart (MDV-AUDIT-1105)
  await expect(meera).not.toContainText('Long-term condition review'); // compact card: no reason or doctor line
  await expect(meera).toContainText('Registration incomplete');
  await expect(cardOf(page, 'Farhan Qureshi (synthetic)')).toContainText('Waiting 12 min');
  await expect(cardOf(page, 'Farhan Qureshi (synthetic)')).toContainText('Token A-07'); // what the queue TV shows instead of the name
  // Each column's action, as designed. Nurse intake is off here, so a checked-in patient goes straight to the doctor (MDV audit UX-05).
  // A waiting card also has its menu (MDV-P1-100).
  await expect(cardOf(page, 'Farhan Qureshi (synthetic)').getByRole('button')).toHaveText(['Ready for doctor', 'More']);
  await expect(cardOf(page, 'Lakshmi Pillai (synthetic)').getByRole('button')).toHaveText(['Intake done', 'More']);
  await expect(cardOf(page, 'Vikram Desai (synthetic)').getByRole('button')).toHaveText('Consult done');
  const billing = cardOf(page, 'Rahul Menon (synthetic)');
  await expect(billing.getByRole('button', { name: 'Close visit' })).toBeEnabled();
  await expect(billing.getByRole('button', { name: 'Payment received' })).toBeDisabled(); // placeholder until P4
});

test('with the nurse and billing steps off the board fits a 1280 px laptop without sideways scrolling', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 720 });
  await open(page, { only: ['Booked', 'CheckedIn', 'InConsult'] });
  await expect(page.getByRole('region', { name: 'Visits by status' }).getByRole('heading', { level: 2 })).toHaveCount(5); // Booked, Checked in, Ready, In consult, Done
  expect(await page.getByRole('region', { name: 'Visits by status' }).evaluate(el => el.scrollWidth <= el.clientWidth)).toBe(true);
});

test('a returning patient is checked in with two taps after confirming name and date of birth', async ({ page }) => {
  const calls = await open(page);
  const meera = cardOf(page, 'Meera Iyer (synthetic)');
  await meera.getByRole('button', { name: 'Check in' }).click();
  const confirm = meera.getByRole('group', { name: 'Confirm identity' });
  await expect(confirm).toContainText('Meera Iyer (synthetic) · born 12 Mar 1972');
  await expect(confirm).toContainText('Registration is incomplete');
  await expect(confirm.getByRole('button', { name: 'Confirm arrival' })).toBeFocused();
  await confirm.getByRole('button', { name: 'Confirm arrival' }).click();

  await expect(column(page, 'Checked in').getByRole('article', { name: 'Meera Iyer (synthetic)' })).toBeVisible();
  expect(calls.checkIns).toHaveLength(1);
  expect(calls.checkIns[0]).toMatchObject({ id: 'a1', csrf: '1' });
  expect(calls.checkIns[0]!.body.elapsedMs).toBeGreaterThan(0); // timed for the exit-gate median
  expect(page.url()).not.toContain('Meera');
});

test('moves follow the board and a conflict says the visit moved on', async ({ page }) => {
  const calls = await open(page);
  await cardOf(page, 'Farhan Qureshi (synthetic)').getByRole('button', { name: 'Ready for doctor' }).click();
  await expect(column(page, 'Ready for doctor').getByRole('article')).toHaveCount(1);
  expect(calls.moves).toEqual([{ id: 'a2', state: 'ReadyForDoctor' }]);
  // With nurse intake off, the With nurse column shows only while a visit is still in it.
  await cardOf(page, 'Lakshmi Pillai (synthetic)').getByRole('button', { name: 'Intake done' }).click();
  await expect(column(page, 'With nurse')).toHaveCount(0);
  await expect(column(page, 'Ready for doctor').getByRole('article')).toHaveCount(2);

  await page.unrouteAll({ behavior: 'ignoreErrors' });
  await open(page, { moveCode: 'invalid_transition' });
  await cardOf(page, 'Lakshmi Pillai (synthetic)').getByRole('button', { name: 'Intake done' }).click();
  await expect(cardOf(page, 'Lakshmi Pillai (synthetic)').getByRole('alert')).toHaveText('This visit has already moved on. The board is up to date.');
});

// MDV-P1-100 (finding F-05): a wrong Call in is undone from the status line; the server decides the window.
test('Call in, then Undo within the window: the card is back in Ready for doctor with its token', async ({ page }) => {
  const calls = await open(page);
  const farhan = cardOf(page, 'Farhan Qureshi (synthetic)');
  await farhan.getByRole('button', { name: 'Ready for doctor' }).click();
  await farhan.getByRole('button', { name: 'Call in' }).click();
  await expect(column(page, 'In consult').getByRole('article', { name: 'Farhan Qureshi (synthetic)' })).toBeVisible();
  await expect(page.getByRole('status').filter({ hasText: 'moved to' })).toHaveText('Farhan Qureshi (synthetic) moved to In consult.');

  await page.getByRole('button', { name: 'Undo' }).click();
  const back = column(page, 'Ready for doctor').getByRole('article', { name: 'Farhan Qureshi (synthetic)' });
  await expect(back).toContainText('Token A-07');
  await expect(page.getByRole('status').filter({ hasText: 'Undone' })).toHaveText('Undone: Farhan Qureshi (synthetic) is back in Ready for doctor.');
  await expect(page.getByRole('button', { name: 'Undo' })).toHaveCount(0); // an undo is not undone
  expect(calls.undos).toEqual([{ id: 'a2', state: 'InConsult' }]); // the state the desk saw, so the server can refuse a stale undo
});

test('an undo the server refuses says why, and the offer goes after 30 seconds', async ({ page }) => {
  await page.clock.install();
  await open(page, { undoCode: 'undo_expired' });
  const farhan = cardOf(page, 'Farhan Qureshi (synthetic)');
  await farhan.getByRole('button', { name: 'Ready for doctor' }).click();
  await page.getByRole('button', { name: 'Undo' }).click();
  await expect(farhan.getByRole('alert')).toHaveText('Too late to undo: more than 30 seconds have passed.');

  await farhan.getByRole('button', { name: 'Call in' }).click();
  await expect(page.getByRole('button', { name: 'Undo' })).toBeVisible();
  await page.clock.fastForward(30_000);
  await expect(page.getByRole('button', { name: 'Undo' })).toHaveCount(0);
  await expect(page.getByRole('status').filter({ hasText: 'moved to' })).toBeVisible(); // the line stays; only the offer goes
});

test('the card menu marks a patient as left without being seen, by keyboard, with a reason', async ({ page }) => {
  const calls = await open(page);
  const more = cardOf(page, 'Farhan Qureshi (synthetic)').getByRole('button', { name: 'More for Farhan Qureshi (synthetic)' });
  await more.focus();
  await page.keyboard.press('Enter');
  const dialog = page.getByRole('dialog', { name: 'Farhan Qureshi (synthetic): what happened?' });
  await expect(dialog.getByRole('button', { name: 'Cancel visit' })).toBeVisible();
  await dialog.getByRole('button', { name: 'Left without being seen' }).focus();
  await page.keyboard.press('Enter');
  const reasons = page.getByRole('dialog', { name: 'Farhan Qureshi (synthetic) left without being seen' });
  await reasons.getByRole('button', { name: 'Mark as left' }).click();
  await expect(reasons.getByText('Choose a reason.')).toBeVisible(); // never without a reason
  expect(calls.ends).toEqual([]);
  await reasons.getByRole('combobox', { name: 'Reason' }).focus();
  await page.keyboard.press('Enter');
  await page.getByRole('option', { name: 'Patient unwell' }).click();
  await reasons.getByRole('button', { name: 'Mark as left' }).click();

  await expect(cardOf(page, 'Farhan Qureshi (synthetic)')).toHaveCount(0); // off the board: not in Done
  await expect(column(page, 'Done').getByRole('article')).toHaveCount(0);
  await expect(page.getByRole('status').filter({ hasText: 'left without' })).toHaveText('Farhan Qureshi (synthetic) marked as left without being seen.');
  await expect(page.getByText('Left without being seen today: 1')).toBeVisible();
  expect(calls.ends).toEqual([{ id: 'a2', how: 'left', reason: 'PatientUnwell' }]);
  expect(page.url()).not.toContain('Farhan');
});

test('the card menu cancels a checked-in visit; closing the dialog changes nothing; a nurse-held card offers only left', async ({ page }) => {
  const calls = await open(page);
  const more = cardOf(page, 'Farhan Qureshi (synthetic)').getByRole('button', { name: /^More/ });
  await more.click();
  await page.keyboard.press('Escape');
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await expect(more).toBeFocused();
  expect(calls.ends).toEqual([]);

  await more.click();
  await page.getByRole('dialog').getByRole('button', { name: 'Cancel visit' }).click();
  const cancel = page.getByRole('dialog', { name: 'Cancel the visit for Farhan Qureshi (synthetic)' });
  await cancel.getByRole('combobox', { name: 'Reason' }).click();
  await page.getByRole('option', { name: 'Booked in error' }).click();
  await cancel.getByRole('button', { name: 'Cancel visit' }).click();
  await expect(cardOf(page, 'Farhan Qureshi (synthetic)')).toHaveCount(0);
  expect(calls.ends).toEqual([{ id: 'a2', how: 'cancel', reason: 'BookedInError' }]);

  // With the nurse: not cancelled from here, so the menu goes straight to the reason for leaving.
  await cardOf(page, 'Lakshmi Pillai (synthetic)').getByRole('button', { name: /^More/ }).click();
  await expect(page.getByRole('dialog', { name: 'Lakshmi Pillai (synthetic) left without being seen' })).toBeVisible();
});

test('a card with an open consultation reads With the doctor and has no desk action', async ({ page }) => {
  await open(page, { extra: [card('a6', 'Deepak Nair (synthetic)', '10:10', 'InConsult', { withDoctor: true, room: 'Room 3' })] });
  const deepak = cardOf(page, 'Deepak Nair (synthetic)');
  await expect(deepak).toContainText('With the doctor');
  await expect(deepak.getByRole('button')).toHaveCount(0);
  await expect(cardOf(page, 'Vikram Desai (synthetic)').getByRole('button', { name: 'Consult done' })).toBeEnabled(); // no consultation screen in use: the desk still closes it
});

test('with billing switched on (flag billing.invoices), Billing cards lead to payment instead of the placeholder', async ({ page }) => {
  const billingOn = { ...reception, active: { ...reception.active, flags: ['billing.invoices'], capabilities: { ...reception.active.capabilities, InvoicesPayments: 'Full' } } };
  await open(page, { as: billingOn });
  const billing = cardOf(page, 'Rahul Menon (synthetic)');
  await expect(billing.getByRole('link', { name: 'Take payment' })).toHaveAttribute('href', '/billing');
  await expect(billing.getByRole('button', { name: 'Payment received' })).toHaveCount(0);
  await expect(billing.getByRole('button', { name: 'Close visit' })).toBeEnabled();
});

test('a read-only role sees the board without queue actions; as a doctor it only starts consultations (MDV-P2-001)', async ({ page }) => {
  await open(page, { as: hospitalDoctor });
  await expect(cardOf(page, 'Meera Iyer (synthetic)')).toBeVisible();
  const buttons = await page.getByRole('region', { name: 'Visits by status' }).getByRole('button').allTextContents();
  // Checked in, with the nurse and in consult; never booked or billing, and no check-in or move.
  expect(buttons).toEqual(['Start consultation', 'Start consultation', 'Open consultation']);
});

test('empty, no access, failure and offline each say so', async ({ page, context }) => {
  await open(page, { empty: true });
  await expect(page.getByRole('alert').filter({ hasText: 'No visits today yet' })).toBeVisible();

  await page.unrouteAll({ behavior: 'ignoreErrors' });
  const forbidden = await open(page, { status: 403 });
  await expect(page.getByRole('alert').filter({ hasText: 'You don’t have access to this facility.' })).toBeVisible();
  expect(forbidden.boards).toBe(1); // not retried

  await page.unrouteAll({ behavior: 'ignoreErrors' });
  await open(page, { status: 500 });
  await expect(page.getByRole('alert')).toContainText('The board could not be loaded', { timeout: 15_000 });
  await expect(page.getByRole('button', { name: 'Try again' })).toBeVisible();

  await page.unrouteAll({ behavior: 'ignoreErrors' });
  await page.route('**/v1/me/context', r => r.fulfill({ json: reception }));
  await page.route('**/v1/front-desk/board?*', () => {}); // never answers
  await page.goto('/front-desk');
  await expect(page.getByRole('status').filter({ hasText: 'Loading the board' })).toBeVisible();
  await context.setOffline(true);
  await expect(page.getByRole('alert').filter({ hasText: 'The board loads when you reconnect.' })).toBeVisible();
  await context.setOffline(false);
  await page.unrouteAll({ behavior: 'ignoreErrors' });
});

test('Arabic is right-to-left with Booked on the reading-start side', async ({ page }) => {
  await open(page, { locale: 'ar' });
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
  const booked = (await column(page, 'محجوز').boundingBox())!;
  const done = (await column(page, 'منتهٍ').boundingBox())!;
  expect(booked.x).toBeGreaterThan(done.x); // columns flow right to left
  await cardOf(page, 'Meera Iyer (synthetic)').getByRole('button', { name: 'تسجيل الوصول' }).click();
  await expect(page.getByRole('group', { name: 'تأكيد الهوية' })).toBeVisible();
});

test('keyboard checks in; 44 px targets; at 200% zoom the board is one list by status, the tablet board scrolls on its own, never the page', async ({ page }) => {
  await page.setViewportSize({ width: 683, height: 768 }); // a 1366 px screen at 200 %: under 768 px, one list (MDV-P1-095)
  const calls = await open(page);
  const tabs = page.getByRole('group', { name: 'Show visits by status' });
  await expect(tabs.getByRole('button', { name: 'Booked · 1' })).toHaveAttribute('aria-pressed', 'true'); // the first status with visits
  const checkIn = cardOf(page, 'Meera Iyer (synthetic)').getByRole('button', { name: 'Check in' });
  await checkIn.focus();
  await page.keyboard.press('Enter');
  await expect(page.getByRole('button', { name: 'Confirm arrival' })).toBeFocused();
  for (const b of await page.getByRole('group', { name: 'Confirm identity' }).getByRole('button').all())
    expect((await b.boundingBox())!.height).toBeGreaterThanOrEqual(44);
  await page.keyboard.press('Enter');
  await tabs.getByRole('button', { name: 'Checked in · 2' }).click();
  await expect(column(page, 'Checked in').getByRole('article')).toHaveCount(2);
  await expect(column(page, 'Booked')).toBeHidden(); // one status at a time
  expect(calls.checkIns).toHaveLength(1);
  for (const b of await page.getByRole('main').getByRole('button').all())
    if (await b.isVisible() && await b.isEnabled()) expect((await b.boundingBox())!.height).toBeGreaterThanOrEqual(44);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  const board = page.getByRole('region', { name: 'Visits by status' });
  expect(await board.evaluate(el => el.scrollWidth <= el.clientWidth)).toBe(true); // a list: nothing sideways

  await page.setViewportSize({ width: 768, height: 1024 }); // tablet portrait: the board, scrolling sideways on its own
  await expect(tabs).toBeHidden();
  expect(await board.evaluate(el => el.scrollWidth > el.clientWidth)).toBe(true);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.setViewportSize({ width: 1024, height: 768 }); // tablet landscape
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

// MDV-P1-095: the board's chrome. Nine visits for two doctors; metrics at the widths a desk uses.
const nine = () => [
  card('b1', 'Meera Iyer (synthetic)', '11:00', 'Booked'), card('b2', 'Anand Krishnamurthy Venkataraghavan Subramaniam (synthetic)', '11:20', 'Booked'),
  card('b3', 'Sara Thomas (synthetic)', '11:40', 'Booked', { calendarId: 'd-2', calendarName: 'Dr. S. Menon (synthetic)' }),
  card('c1', 'Farhan Qureshi (synthetic)', '10:20', 'CheckedIn', { token: 'A-07', queuePlace: 1 }),
  card('c2', 'Deepak Nair (synthetic)', '10:25', 'CheckedIn', { token: 'A-08', queuePlace: 2, calendarId: 'd-2', calendarName: 'Dr. S. Menon (synthetic)' }),
  card('r1', 'Lakshmi Pillai (synthetic)', '10:10', 'ReadyForDoctor', { queuePlace: 1 }),
  card('i1', 'Vikram Desai (synthetic)', '10:00', 'InConsult'), card('l1', 'Rahul Menon (synthetic)', '09:30', 'Billing'),
  card('d1', 'Kavya Iyer (synthetic)', '09:00', 'Done'),
];
const firstCardTop = async (page: Page) => (await page.getByRole('article').first().boundingBox())!.y;

test('one header row and a one-line workload: at 1024 x 768 the first card starts at or above 220 px; at 1440 and 768 too', async ({ page }) => {
  for (const [w, h, most] of [[1024, 768, 220], [1440, 900, 220], [768, 1024, 300]] as const) {
    await page.setViewportSize({ width: w, height: h });
    await fakeHub(page, '/v1/hubs/board', /Meera|Iyer/); // live, as at the desk
    await open(page, { only: [], extra: nine() });
    await expect(cardOf(page, 'Meera Iyer (synthetic)')).toBeVisible();
    await expect(page.getByText('Live updates')).toBeVisible();
    expect(await firstCardTop(page), `first card at ${w} px`).toBeLessThanOrEqual(most);
    const row = (await page.locator('[data-slot=page-header-row]').boundingBox())!;
    if (w >= 1024) expect(row.height, `one header row at ${w} px`).toBeLessThanOrEqual(48); // the view switch is 48 px; at 768 the actions wrap
    await page.unrouteAll({ behavior: 'ignoreErrors' });
  }
});

test('from 768 to 1440 px no text leaves its card and every action label is on one line', async ({ page }) => {
  for (const locale of ['en', 'ar'])
    for (const w of [768, 1024, 1440]) {
      await page.setViewportSize({ width: w, height: 900 });
      await open(page, { locale, only: [], extra: nine() });
      await expect(page.getByRole('article')).toHaveCount(9);
      const outside = await page.getByRole('region', { name: locale === 'en' ? 'Visits by status' : 'الزيارات حسب الحالة' }).evaluate(region => {
        const out: string[] = [];
        for (const card of region.querySelectorAll('article')) {
          const c = card.getBoundingClientRect();
          for (const el of card.querySelectorAll<HTMLElement>('h3, p, span, button, a')) {
            const r = el.getBoundingClientRect();
            if (r.width && (r.left < c.left - 0.5 || r.right > c.right + 0.5)) out.push(`${el.textContent?.slice(0, 30)} outside its card`);
          }
          for (const b of card.querySelectorAll<HTMLElement>('button')) if (b.getBoundingClientRect().height > 46) out.push(`${b.textContent} wraps`);
        }
        return out;
      });
      expect(outside, `${locale} at ${w} px`).toEqual([]);
      await page.unrouteAll({ behavior: 'ignoreErrors' });
    }
});

test('the card names the doctor when two doctors work today, and not with one', async ({ page }) => {
  await open(page, { only: [], extra: nine() });
  await expect(cardOf(page, 'Sara Thomas (synthetic)')).toContainText('Dr. S. Menon (synthetic)');
  await expect(cardOf(page, 'Meera Iyer (synthetic)')).toContainText('54 y · Female · Dr. Arjun Rao (synthetic)');
  await page.unrouteAll({ behavior: 'ignoreErrors' });
  await open(page);
  await expect(cardOf(page, 'Meera Iyer (synthetic)')).not.toContainText('Dr. Arjun Rao');
});

test('a long column keeps its name and count in view while it scrolls; an empty column is a narrow stub with its name', async ({ page }) => {
  await page.setViewportSize({ width: 1024, height: 768 });
  const many = Array.from({ length: 12 }, (_, i) => card(`m${i}`, `Patient ${i} (synthetic)`, '11:00', 'Booked'));
  await open(page, { only: [], extra: many });
  const region = page.getByRole('region', { name: 'Visits by status' });
  const header = column(page, 'Booked').getByRole('heading', { level: 2 });
  await cardOf(page, 'Patient 11 (synthetic)').scrollIntoViewIfNeeded();
  expect(await region.evaluate(el => el.scrollTop)).toBeGreaterThan(0);
  const [h, r] = [(await header.boundingBox())!, (await region.boundingBox())!];
  expect(Math.abs(h.y - r.y)).toBeLessThanOrEqual(1); // pinned at the top of the board
  await expect(header).toHaveText(/Booked/);
  await expect(header).toContainText('12');
  const stub = column(page, 'Done');
  expect((await stub.boundingBox())!.width).toBeLessThanOrEqual(48);
  await expect(stub.getByRole('heading', { level: 2 })).toHaveText(/Done.*0/);
});

test('at 375 px the board is one list with status tabs and nothing scrolls sideways', async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 812 });
  await open(page, { only: [], extra: nine() });
  const tabs = page.getByRole('group', { name: 'Show visits by status' });
  await expect(tabs.getByRole('button')).toHaveText(['Booked · 3', 'Checked in · 2', 'Ready for doctor · 1', 'In consult · 1', 'Billing · 1', 'Done · 1']);
  await expect(page.getByRole('article')).toHaveCount(3);
  await tabs.getByRole('button', { name: 'Ready for doctor · 1' }).click();
  await expect(page.getByRole('article')).toHaveText([/Lakshmi Pillai/]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

test('a doctor sees at most one filled button in the first viewport: the next patient\'s Start consultation', async ({ page }) => {
  await page.setViewportSize({ width: 1024, height: 768 });
  await open(page, { as: hospitalDoctor, only: [], extra: nine() });
  await expect(cardOf(page, 'Lakshmi Pillai (synthetic)')).toBeVisible();
  const filled = await page.evaluate(() => [...document.querySelectorAll<HTMLElement>('main .bg-primary')].filter(el => {
    const r = el.getBoundingClientRect();
    return r.width > 0 && r.bottom > 0 && r.top < window.innerHeight && r.right > 0 && r.left < window.innerWidth;
  }).map(el => el.getAttribute('aria-label') ?? el.textContent));
  expect(filled).toEqual(['Start consultation with Lakshmi Pillai (synthetic)']);
});

for (const locale of ['en', 'ar'])
  for (const [w, h] of [[1440, 900], [1024, 768], [375, 812]] as const)
    test(`axe finds no violations on the board at ${w} px (${locale})`, async ({ page }) => {
      await page.setViewportSize({ width: w, height: h });
      await open(page, { locale, only: [], extra: nine() });
      await expect(page.getByRole('article').first()).toBeVisible();
      await page.addScriptTag({ path: axePath });
      expect(await axeViolations(page)).toEqual([]);
    });

test('live: a change made at another desk appears without reloading, and a drop pauses then resyncs', async ({ page }) => {
  const hub = await fakeHub(page, '/v1/hubs/board', /Meera|Iyer/); // nothing about patients in the hub's URLs
  const calls = await open(page);
  const status = page.getByRole('status').filter({ hasText: /Live updates/ });
  await expect(status).toHaveText('Live updates');
  expect(hub.watched).toEqual([['f-1']]);
  await expect.poll(() => calls.boards).toBe(2); // the first load, then a refetch once joined

  // Another desk calls in Farhan: the server's board changes, the hub says so, this board refetches.
  calls.state.find(c => c.id === 'a2')!.state = 'InConsult';
  hub.push('BoardChanged', [{ appointmentId: 'a2', state: 'InConsult', changedAt: now }]);
  await expect(column(page, 'In consult').getByRole('article', { name: 'Farhan Qureshi (synthetic)' })).toBeVisible();

  // The connection drops and cannot come back for a while: the board says so in words, and misses nothing.
  hub.drop();
  await expect(status).toHaveText('Live updates paused · refreshing every 30 s');
  calls.state.find(c => c.id === 'a3')!.state = 'ReadyForDoctor'; // changed while this board was not listening
  hub.restore();
  await expect(status).toHaveText('Live updates', { timeout: 20_000 });
  await expect(column(page, 'Ready for doctor').getByRole('article', { name: 'Lakshmi Pillai (synthetic)' })).toBeVisible();
  expect(hub.watched).toEqual([['f-1'], ['f-1']]);
});

test('live: unreachable hub shows the paused state, in Arabic too', async ({ page }) => {
  await page.route('**/v1/hubs/board/negotiate*', r => r.fulfill({ status: 503, body: '' }));
  await open(page, { locale: 'ar' });
  await expect(page.getByRole('status').filter({ hasText: 'التحديثات المباشرة متوقفة مؤقتًا' })).toBeVisible();
  await expect(cardOf(page, 'Meera Iyer (synthetic)')).toBeVisible(); // the board itself still loads and refreshes
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
const axeViolations = (page: Page) => page.evaluate(async () =>
  (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
    .map(v => `${v.id} (${v.nodes.length})`));
for (const locale of ['en', 'ar'])
  test(`axe finds no violations on the status line, the card menu and its reason form (${locale})`, async ({ page }) => {
    await open(page, { locale });
    const farhan = cardOf(page, 'Farhan Qureshi (synthetic)');
    await farhan.getByRole('button').first().click(); // a move, so the status line offers Undo
    await expect(page.getByRole('status').filter({ hasText: 'Farhan' })).toBeVisible();
    await page.addScriptTag({ path: axePath });
    expect(await axeViolations(page)).toEqual([]);
    await farhan.getByRole('button').last().click(); // the menu: left or cancel
    await expect(page.getByRole('dialog')).toBeVisible();
    expect(await axeViolations(page)).toEqual([]);
    await page.getByRole('dialog').getByRole('button').first().click(); // left without being seen: the reason form
    await expect(page.getByRole('dialog').getByRole('combobox')).toBeVisible();
    expect(await axeViolations(page)).toEqual([]);
  });
for (const locale of ['en', 'ar'])
  test(`axe finds no violations on the board and the identity check (${locale})`, async ({ page }) => {
    await open(page, { locale });
    await cardOf(page, 'Meera Iyer (synthetic)').getByRole('button').first().click();
    await expect(page.getByRole('group').getByRole('button').first()).toBeVisible();
    await page.addScriptTag({ path: axePath });
    const violations = await page.evaluate(async () =>
      (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
        .map(v => `${v.id} (${v.nodes.length})`));
    expect(violations).toEqual([]);
  });

// MDV-P1-102: the doctor's follow-up on the Done card, date and doctor only; Book opens the schedule on that day with the patient and doctor chosen;
// Dismiss asks for a reason first and sends only its code. The server's role scope, booking and audit are proven in FollowUpDueTests.
const followUp = { id: 'w-1', dueOn: '2026-11-02', calendarId: 'd-1', calendarName: 'Dr. Arjun Rao (synthetic)' };
for (const locale of ['en', 'ar'])
  test(`a Done card shows Follow-up due with Book, and dismissing takes a reason (${locale})`, async ({ page }) => {
    const dismissed: string[] = [];
    await page.route('**/v1/waitlist/w-1?*', r => {
      expect(r.request().method()).toBe('DELETE');
      dismissed.push(new URL(r.request().url()).searchParams.get('reason')!);
      return r.fulfill({ status: 204 });
    });
    const calls = await open(page, { locale, extra: [card('a7', 'Deepak Nair (synthetic)', '09:00', 'Done', { followUp }), card('a8', 'Sara Thomas (synthetic)', '09:10', 'Done')] });
    const deepak = cardOf(page, 'Deepak Nair (synthetic)');
    await expect(deepak.getByRole('group')).toHaveCount(1);
    await expect(cardOf(page, 'Sara Thomas (synthetic)').getByRole('group')).toHaveCount(0); // no follow-up, no line
    await page.addScriptTag({ path: axePath });
    expect(await axeViolations(page)).toEqual([]);
    await deepak.getByRole('group').getByRole('button').click();
    await expect(page.getByRole('dialog')).toBeVisible();
    expect(await axeViolations(page)).toEqual([]);
    if (locale === 'ar') return;

    const dialog = page.getByRole('dialog', { name: 'Dismiss the follow-up for Deepak Nair (synthetic)' });
    await dialog.getByRole('button', { name: 'Back' }).click();
    await expect(deepak.getByRole('group', { name: /^Follow-up due from/ })).toHaveText(/Follow-up due from Monday, 2 November 2026 · Dr\. Arjun Rao \(synthetic\)/);
    await expect(deepak.getByRole('link', { name: 'Book the follow-up for Deepak Nair (synthetic)' }))
      .toHaveAttribute('href', '/schedule?date=2026-11-02&view=day&patient=p-a7&doctor=d-1');
    await deepak.getByRole('button', { name: 'Dismiss the follow-up for Deepak Nair (synthetic)' }).click();
    await dialog.getByRole('button', { name: 'Dismiss follow-up' }).click();
    await expect(dialog.getByText('Choose a reason.')).toBeVisible(); // nothing is sent without one
    expect(dismissed).toEqual([]);
    await dialog.getByRole('combobox', { name: 'Reason' }).click();
    await page.getByRole('option', { name: 'Patient declined' }).click();
    calls.state.find(c => c.id === 'a7')!.followUp = null; // what the server's board answers after the dismissal
    await dialog.getByRole('button', { name: 'Dismiss follow-up' }).click();
    await expect(dialog).toHaveCount(0);
    expect(dismissed).toEqual(['PatientDeclined']);
    await expect(deepak.getByRole('group')).toHaveCount(0);
  });

// MDV-P2-046: the token slip. Reprint sits on a waiting card with a token; the slip names the clinic, token, doctor, date and time and
// nothing about the patient. The server's field list and the void-token refusal are proven in FrontDeskEndpointTests.
const slip = { clinicName: 'Iyer Family Clinic · Adyar', token: 'A-07', doctorName: 'Dr. Arjun Rao (synthetic)', issuedAt: ist('10:20'), timeZone: 'Asia/Kolkata' };
for (const locale of ['en', 'ar'])
  test(`Reprint opens the same token's slip: clinic, token, doctor and time, no patient details, axe clean (${locale})`, async ({ page }) => {
    const reads: string[] = [];
    await page.route('**/v1/appointments/*/token-slip', r => { reads.push(r.request().url().split('/').at(-2)!); return r.fulfill({ json: slip }); });
    await open(page, { locale });
    await expect(cardOf(page, 'Meera Iyer (synthetic)').getByRole('link')).toHaveCount(0); // booked: no token yet, nothing to reprint
    await cardOf(page, 'Farhan Qureshi (synthetic)').getByRole('link', { name: locale === 'en' ? 'Reprint token A-07' : /A-07/ }).click();
    await expect(page).toHaveURL(/\/front-desk\/token\/a2$/);
    // The slip is named by its token; the board's cards (named by patient) may still be on screen for a moment while the page loads.
    const paper = page.getByRole('article', { name: /A-07/ });
    await expect(paper).toBeVisible();
    await expect(page.getByRole('article')).toHaveCount(1);
    await expect(page.getByRole('heading', { level: 1 })).toHaveCount(1);
    await expect(paper).toContainText('A-07');
    await expect(paper).toContainText('Iyer Family Clinic · Adyar');
    await expect(paper).toContainText('Dr. Arjun Rao (synthetic)');
    await expect(paper).toContainText('10:20'); // issued, in facility time although the device is in New York
    // The test fails if a patient detail is added: no name, MRN, phone or reason.
    for (const leak of ['Farhan', 'Qureshi', 'APT-A2', '+91', 'General', '1972']) await expect(paper).not.toContainText(leak);
    expect(reads).toEqual(['a2']);
    if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    await page.addScriptTag({ path: axePath });
    expect(await axeViolations(page)).toEqual([]);
  });

test('a void token (the visit was cancelled or the patient left) is refused by the server and says so', async ({ page }) => {
  await page.route('**/v1/appointments/*/token-slip', r => r.fulfill({ status: 409, json: { title: 'Visit cannot make this move', code: 'token_void' } }));
  await page.route('**/v1/me/context', r => r.fulfill({ json: reception }));
  await page.goto('/front-desk/token/a9');
  await expect(page.getByRole('alert')).toHaveText('This visit was cancelled or the patient left, so its token is void and cannot be printed.');
  await expect(page.getByRole('article')).toHaveCount(0);
});
