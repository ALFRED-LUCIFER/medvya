import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';
import { fakeHub } from './support/fakeHub';

// MDV-P1-030: D-04 clinic overview and the board's doctor workload. CI has no API, so the endpoints are mocked in the shapes
// the server sends (IFrontDeskService.cs).
test.use({ timezoneId: 'America/New_York' });

const persona = (role: string, userId: string, displayName: string, capabilities: Record<string, string>) => ({
  user: { id: userId, displayName },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role, flags: [] as string[], capabilities },
});
const reception = persona('Receptionist', 'u-reception', 'A. Joseph', { RegisterBookPatients: 'Full', CheckInQueue: 'Full' });
const menon = persona('HospitalDoctor', 'd-2', 'Dr. S. Menon', { RegisterBookPatients: 'Full', CheckInQueue: 'Read', Patient360Chart: 'Full' });

const ist = (hhmm: string) => {
  const [h, m] = hhmm.split(':').map(Number);
  return new Date(Date.parse('2026-10-19T00:00:00Z') + ((h! * 60 + m!) - 330) * 60_000).toISOString();
};
const counts = (c: Partial<Record<string, number>>) => ({ Booked: 0, CheckedIn: 0, WithNurse: 0, ReadyForDoctor: 0, InConsult: 0, Billing: 0, Done: 0, ...c });

function overview() {
  return {
    facilityId: 'f-1', timeZone: 'Asia/Kolkata', now: ist('09:50'),
    doctors: [
      { calendarId: 'd-1', name: 'Dr. Arjun Rao (synthetic)', counts: counts({ Booked: 1, CheckedIn: 1, InConsult: 1 }), delayMinutes: 30,
        next: [{ id: 'a2', start: ist('09:20'), state: 'CheckedIn', patientName: 'Deepak Nair (synthetic)', token: 'A-02' }] },
      { calendarId: 'd-2', name: 'Dr. S. Menon (synthetic)', counts: counts({}), delayMinutes: 0, next: [] as unknown[] },
    ],
    waiting: [{ id: 'a2', token: 'A-02', patientName: 'Deepak Nair (synthetic)', calendarId: 'd-1', state: 'CheckedIn', start: ist('09:20'), since: ist('09:10') }],
  };
}

async function open(page: Page, { as = reception, locale = 'en' } = {}) {
  const state = overview();
  const calls = { moves: [] as { id: string; body: unknown; csrf: string | undefined }[] };
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  const hub = await fakeHub(page, '/v1/hubs/board', /Deepak|Nair/);
  await page.route('**/v1/me/context', r => r.fulfill({ json: as }));
  await page.route('**/v1/front-desk/overview?*', r => r.fulfill({ json: state }));
  await page.route('**/v1/appointments/*/reassign', r => {
    const id = r.request().url().split('/').at(-2)!;
    calls.moves.push({ id, body: r.request().postDataJSON(), csrf: r.request().headers()['x-medvya-csrf'] });
    state.waiting[0]!.calendarId = 'd-2';
    return r.fulfill({ json: {} });
  });
  await page.goto('/front-desk/overview');
  return { calls, hub, state };
}

test('lists each doctor working today with visits per state, delay and next patients, and the waiting room', async ({ page }) => {
  await open(page);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Clinic overview');
  const rao = page.getByRole('article', { name: 'Dr. Arjun Rao (synthetic)' });
  await expect(rao).toContainText('Running 30 min late');
  await expect(rao.getByRole('list', { name: 'Visits by status' })).toContainText('Booked 1');
  await expect(rao.getByRole('list', { name: 'Visits by status' })).toContainText('In consult 1');
  await expect(rao).toContainText('Deepak Nair (synthetic)');
  await expect(rao).toContainText('Token A-02');
  await expect(page.getByRole('article', { name: 'Dr. S. Menon (synthetic)' })).toContainText('On time');
  const room = page.getByRole('region', { name: 'Waiting room (1)' });
  await expect(room).toContainText('Deepak Nair (synthetic)');
  await expect(room).toContainText('With Dr. Arjun Rao (synthetic)');
  await expect(room).toContainText('Waiting 40 min');
});

test('reception moves a waiting patient to another doctor with a listed reason', async ({ page }) => {
  const { calls } = await open(page);
  await page.getByRole('button', { name: 'Move to another doctor' }).click();
  const form = page.getByRole('form', { name: 'Move Deepak Nair (synthetic)' });
  await form.getByRole('button', { name: 'Move patient' }).click();
  await expect(form.getByRole('alert')).toHaveText('Choose a doctor'); // a doctor is required
  await form.getByRole('combobox', { name: 'Move to' }).click();
  await page.getByRole('option', { name: 'Dr. S. Menon (synthetic)' }).click();
  await form.getByRole('combobox', { name: 'Reason' }).click();
  await page.getByRole('option', { name: 'Balance the queues' }).click();
  await form.getByRole('button', { name: 'Move patient' }).click();
  await expect(page.getByRole('region', { name: 'Waiting room (1)' })).toContainText('With Dr. S. Menon (synthetic)');
  expect(calls.moves).toEqual([{ id: 'a2', body: { calendarId: 'd-2', reason: 'BalanceWorkload' }, csrf: '1' }]);
  expect(page.url()).not.toMatch(/Deepak|Nair/);
});

test('a doctor sees their own day first, read only', async ({ page }) => {
  await open(page, { as: menon });
  await expect(page.getByRole('article', { name: 'Your day · Dr. S. Menon (synthetic)' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Move to another doctor' })).toHaveCount(0);
});

test('the board shows each doctor\'s workload and links to the overview', async ({ page }) => {
  const card = (id: string, name: string, time: string, state: string, calendarId = 'd-1', calendarName = 'Dr. Arjun Rao (synthetic)') => ({
    id, reference: id, patientId: `p-${id}`, patientName: name, dateOfBirth: null, registrationIncomplete: false, calendarId, calendarName,
    visitType: 'FollowUp', reasonCategory: 'General', start: ist(time), end: ist(time), state, since: ist(time), room: null, token: null,
  });
  await page.route('**/v1/me/context', r => r.fulfill({ json: reception }));
  await page.route('**/v1/front-desk/board?*', r => r.fulfill({ json: { facilityId: 'f-1', timeZone: 'Asia/Kolkata', date: '2026-10-19', now: ist('09:50'),
    cards: [card('a1', 'Meera Iyer (synthetic)', '09:00', 'InConsult'), card('a2', 'Deepak Nair (synthetic)', '09:20', 'CheckedIn'),
      card('a3', 'Lakshmi Pillai (synthetic)', '09:40', 'WithNurse'), card('a4', 'Farhan Qureshi (synthetic)', '10:00', 'CheckedIn', 'd-2', 'Dr. S. Menon (synthetic)')] } }));
  await page.goto('/front-desk');
  const panel = page.getByRole('region', { name: 'Doctor workload' });
  await expect(panel).toContainText('Dr. Arjun Rao (synthetic)');
  await expect(panel).toContainText('2 waiting · Running 30 min late');
  await expect(panel).toContainText('1 waiting · On time');
  await page.getByRole('link', { name: 'Clinic overview' }).click();
  await expect(page).toHaveURL(/\/front-desk\/overview$/);
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
for (const locale of ['en', 'ar'])
  test(`axe finds no violations on the overview and the move form (${locale}); 44 px targets`, async ({ page }) => {
    await open(page, { locale });
    if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    await page.getByRole('button', { name: locale === 'en' ? 'Move to another doctor' : 'نقل إلى طبيب آخر' }).click();
    for (const b of await page.getByRole('main').getByRole('button').all()) if (await b.isVisible()) expect((await b.boundingBox())!.height).toBeGreaterThanOrEqual(44);
    await page.addScriptTag({ path: axePath });
    const violations = await page.evaluate(async () =>
      (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
        .map(v => `${v.id} (${v.nodes.length})`));
    expect(violations).toEqual([]);
  });
