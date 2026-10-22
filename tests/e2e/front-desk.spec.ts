import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';

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
  calendarId: 'd-1', calendarName: 'Dr. Arjun Rao (synthetic)', visitType: 'FollowUp', reasonCategory: 'General',
  start: ist(time), end: ist(time), state, since: ist(time), ...extra,
});
const cards = () => [
  card('a1', 'Meera Iyer (synthetic)', '11:00', 'Booked', { reasonCategory: 'ChronicReview', registrationIncomplete: true }),
  card('a2', 'Farhan Qureshi (synthetic)', '10:20', 'CheckedIn', { since: ist('10:20') }),
  card('a3', 'Lakshmi Pillai (synthetic)', '10:40', 'WithNurse', { reasonCategory: 'PostDischarge' }),
  card('a4', 'Vikram Desai (synthetic)', '10:00', 'InConsult', { visitType: 'NewConsultation' }),
  card('a5', 'Rahul Menon (synthetic)', '09:30', 'Billing'),
];

type Calls = { checkIns: { id: string; body: { elapsedMs: number }; csrf: string | undefined }[]; moves: { id: string; state: string }[]; boards: number };

async function open(page: Page, { locale = 'en', as = reception, status = 200, moveCode = '', empty = false } = {}) {
  const calls: Calls = { checkIns: [], moves: [], boards: 0 };
  const state = empty ? [] : cards();
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: as }));
  await page.route('**/v1/front-desk/board?*', r => {
    expect(new URL(r.request().url()).searchParams.get('facilityId')).toBe('f-1');
    calls.boards++;
    return status === 200 ? r.fulfill({ json: { facilityId: 'f-1', timeZone: 'Asia/Kolkata', date: '2026-10-19', now, cards: state } }) : r.fulfill({ status, json: {} });
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
    Object.assign(c, { state: body.state, since: now });
    return r.fulfill({ json: c });
  });
  await page.goto('/front-desk');
  return calls;
}

const column = (page: Page, name: string) => page.getByRole('region', { name: new RegExp(`^${name}`) });
const cardOf = (page: Page, name: string) => page.getByRole('article', { name });

test('shows today\'s visits by status with nonclinical tags, in facility time', async ({ page }) => {
  await open(page);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Reception board · Monday, 19 October 2026');
  await expect(page.getByText('Receptionist view hides clinical notes')).toBeVisible();
  for (const [name, count] of [['Booked', 1], ['Checked in', 1], ['With nurse', 1], ['Ready for doctor', 0], ['In consult', 1], ['Billing', 1], ['Done', 0]] as const)
    await expect(column(page, name).getByRole('article')).toHaveCount(count);
  await expect(column(page, 'Ready for doctor')).toContainText('No visits');

  const meera = cardOf(page, 'Meera Iyer (synthetic)');
  await expect(meera).toContainText('11:00'); // 05:30Z shown in IST, not New York time
  await expect(meera).toContainText('Dr. Arjun Rao (synthetic) · Follow-up');
  await expect(meera).toContainText('Long-term condition review'); // the reason as its category only
  await expect(meera).toContainText('Registration incomplete');
  await expect(cardOf(page, 'Farhan Qureshi (synthetic)')).toContainText('Waiting 12 min');
  // Each column's action, as designed.
  await expect(cardOf(page, 'Farhan Qureshi (synthetic)').getByRole('button')).toHaveText('Send to nurse');
  await expect(cardOf(page, 'Lakshmi Pillai (synthetic)').getByRole('button')).toHaveText('Intake done');
  await expect(cardOf(page, 'Vikram Desai (synthetic)').getByRole('button')).toHaveText('Consult done');
  const billing = cardOf(page, 'Rahul Menon (synthetic)');
  await expect(billing.getByRole('button', { name: 'Close visit' })).toBeEnabled();
  await expect(billing.getByRole('button', { name: 'Payment received' })).toBeDisabled(); // placeholder until P4
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
  await cardOf(page, 'Farhan Qureshi (synthetic)').getByRole('button', { name: 'Send to nurse' }).click();
  await expect(column(page, 'With nurse').getByRole('article')).toHaveCount(2);
  expect(calls.moves).toEqual([{ id: 'a2', state: 'WithNurse' }]);

  await page.unrouteAll({ behavior: 'ignoreErrors' });
  await open(page, { moveCode: 'invalid_transition' });
  await cardOf(page, 'Lakshmi Pillai (synthetic)').getByRole('button', { name: 'Intake done' }).click();
  await expect(cardOf(page, 'Lakshmi Pillai (synthetic)').getByRole('alert')).toHaveText('This visit has already moved on. The board is up to date.');
});

test('with billing switched on (flag billing.invoices), Billing cards lead to payment instead of the placeholder', async ({ page }) => {
  const billingOn = { ...reception, active: { ...reception.active, flags: ['billing.invoices'], capabilities: { ...reception.active.capabilities, InvoicesPayments: 'Full' } } };
  await open(page, { as: billingOn });
  const billing = cardOf(page, 'Rahul Menon (synthetic)');
  await expect(billing.getByRole('link', { name: 'Take payment' })).toHaveAttribute('href', '/billing');
  await expect(billing.getByRole('button', { name: 'Payment received' })).toHaveCount(0);
  await expect(billing.getByRole('button', { name: 'Close visit' })).toBeEnabled();
});

test('a read-only role sees the board without actions', async ({ page }) => {
  await open(page, { as: hospitalDoctor });
  await expect(cardOf(page, 'Meera Iyer (synthetic)')).toBeVisible();
  await expect(page.getByRole('region', { name: 'Visits by status' }).getByRole('button')).toHaveCount(0);
});

test('empty, no access, failure and offline each say so', async ({ page, context }) => {
  await open(page, { empty: true });
  await expect(page.getByRole('alert')).toContainText('No visits today yet');

  await page.unrouteAll({ behavior: 'ignoreErrors' });
  const forbidden = await open(page, { status: 403 });
  await expect(page.getByRole('alert')).toContainText('You don’t have access to this facility.');
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

test('keyboard checks in; 44 px targets; tablet and 200% zoom scroll the board, never the page', async ({ page }) => {
  await page.setViewportSize({ width: 683, height: 768 }); // a 1366 px screen at 200 %
  const calls = await open(page);
  const checkIn = cardOf(page, 'Meera Iyer (synthetic)').getByRole('button', { name: 'Check in' });
  await checkIn.focus();
  await page.keyboard.press('Enter');
  await expect(page.getByRole('button', { name: 'Confirm arrival' })).toBeFocused();
  for (const b of await page.getByRole('group', { name: 'Confirm identity' }).getByRole('button').all())
    expect((await b.boundingBox())!.height).toBeGreaterThanOrEqual(44);
  await page.keyboard.press('Enter');
  await expect(column(page, 'Checked in').getByRole('article')).toHaveCount(2);
  expect(calls.checkIns).toHaveLength(1);
  for (const b of await page.getByRole('region', { name: 'Visits by status' }).getByRole('button').all())
    if (await b.isEnabled()) expect((await b.boundingBox())!.height).toBeGreaterThanOrEqual(44);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  const board = page.getByRole('region', { name: 'Visits by status' });
  expect(await board.evaluate(el => el.scrollWidth > el.clientWidth)).toBe(true); // the board scrolls sideways itself

  await page.setViewportSize({ width: 1024, height: 768 }); // tablet landscape
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
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
