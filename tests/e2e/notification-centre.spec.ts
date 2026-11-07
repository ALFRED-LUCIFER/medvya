import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';

// MDV-P1-034: the notification centre (D-03) and the board's failed-message mark. CI has no API, so the endpoints are mocked in the
// shapes the server sends (IStaffNotifications). No journey row covers this, so it is a spec of its own.
const persona = (role: string, capabilities: Record<string, string>) => ({
  user: { id: 'u-r', displayName: 'A. Joseph' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role, flags: [] as string[], capabilities },
});
const reception = persona('Receptionist', { RegisterBookPatients: 'Full', CheckInQueue: 'Full' });
const card = {
  id: 'a-1', reference: 'APT-0A1B2C3D', patientId: 'p-1', patientName: 'Farhan Qureshi (synthetic)', dateOfBirth: '1988-05-02', registrationIncomplete: false,
  calendarId: 'd-1', calendarName: 'Dr. Arjun Rao (synthetic)', visitType: 'FollowUp', reasonCategory: 'General',
  start: '2026-10-19T04:50:00Z', end: '2026-10-19T05:10:00Z', state: 'Booked', since: '2026-10-19T04:50:00Z', room: null, token: null,
};

type Item = { id: string; kind: string; refId: string | null; detail?: number; createdAt: string; read: boolean };
async function open(page: Page, { locale = 'en', as = reception, path = '/front-desk', extra = [] as Item[] } = {}) {
  const items: Item[] = [...extra,
    { id: 'n-1', kind: 'MessageFailed', refId: 'a-1', createdAt: '2026-10-19T04:00:00Z', read: false },
    { id: 'n-2', kind: 'MessageFailed', refId: 'a-9', createdAt: '2026-10-18T04:00:00Z', read: true },
  ];
  const calls = { marked: [] as { url: string; body: unknown; csrf: string | undefined }[] };
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: as }));
  await page.route('**/v1/notifications/inbox?*', r => r.fulfill({ json: { unread: items.filter(i => !i.read).length, items } }));
  await page.route('**/v1/notifications/inbox/**', r => {
    calls.marked.push({ url: r.request().url(), body: r.request().postDataJSON(), csrf: r.request().headers()['x-medvya-csrf'] });
    if (r.request().url().endsWith('/read-all')) items.forEach(i => (i.read = true));
    else items.find(i => r.request().url().includes(i.id))!.read = true;
    return r.fulfill({ status: 204 });
  });
  await page.route('**/v1/notifications/failed-visits?*', r => r.fulfill({ json: ['a-1'] }));
  await page.route('**/v1/front-desk/board?*', r => r.fulfill({ json: { facilityId: 'f-1', timeZone: 'Asia/Kolkata', date: '2026-10-19', now: '2026-10-19T05:02:00Z', cards: [card] } }));
  await page.goto(path);
  return calls;
}

test('the bell shows the unread count; Mark all read clears it and is kept on the server', async ({ page }) => {
  const calls = await open(page);
  const bell = page.getByRole('button', { name: 'Notifications, 1 unread' });
  await expect(bell).toBeVisible();
  await bell.click();
  const panel = page.getByRole('dialog', { name: 'Notifications' });
  await expect(panel).toBeFocused();
  await expect(panel.getByRole('listitem')).toHaveCount(2);
  await expect(panel).toContainText('A message to a patient was not delivered');
  expect(await panel.textContent()).not.toMatch(/Farhan|Qureshi/); // no patient name in the centre
  await panel.getByRole('button', { name: 'Mark all read' }).click();
  await expect(page.getByRole('button', { name: 'Notifications', exact: true })).toBeVisible();
  expect(calls.marked).toEqual([{ url: expect.stringContaining('/v1/notifications/inbox/read-all'), body: { facilityId: 'f-1' }, csrf: '1' }]);
});

test('Escape closes the overlay and returns focus to the bell; an item marks itself read and opens the board', async ({ page }) => {
  const calls = await open(page, { path: '/schedule' });
  const bell = page.getByRole('button', { name: 'Notifications, 1 unread' });
  await bell.click();
  await page.keyboard.press('Escape');
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await expect(bell).toBeFocused();
  await bell.click();
  await page.getByRole('dialog').getByRole('link').first().click();
  await expect(page).toHaveURL(/\/front-desk$/);
  expect(calls.marked.map(m => m.url)).toEqual([expect.stringContaining('/v1/notifications/inbox/n-1/read')]);
});

for (const [locale, soon, today] of [
  ['en', 'The clinic registration expires in 30 days or less', 'The clinic registration expires today or has expired'],
  ['ar', 'ينتهي تسجيل العيادة خلال 30 يومًا أو أقل', 'ينتهي تسجيل العيادة اليوم أو انتهى'],
] as const)
  test(`the practice registration reminder reads in words and opens the profile (${locale}, MDV-P1-010)`, async ({ page }) => {
    const owner = persona('PracticeAdmin', { RegisterBookPatients: 'Full', CheckInQueue: 'Full', StaffRolesIntegrations: 'Full' });
    await open(page, { locale, as: owner, extra: [
      { id: 'r-1', kind: 'RegistrationExpiring', refId: null, detail: 30, createdAt: '2026-10-19T03:00:00Z', read: false },
      { id: 'r-2', kind: 'RegistrationExpiring', refId: null, detail: 0, createdAt: '2026-10-19T03:30:00Z', read: false },
    ] });
    await page.locator('header').getByRole('button', { name: /^(Notifications|الإشعارات)/ }).click();
    const panel = page.getByRole('dialog');
    await expect(panel).toContainText(soon);
    await expect(panel).toContainText(today);
    await panel.getByText(soon).click();
    await expect(page).toHaveURL(/\/settings\?tab=profile/);
  });

test('the board marks the visit whose message failed, in words', async ({ page }) => {
  await open(page);
  await expect(page.getByRole('article', { name: 'Farhan Qureshi (synthetic)' })).toContainText('Message not delivered');
});

test('a role without the desk gets the inert bell and no notification calls', async ({ page }) => {
  let asked = false;
  await page.route('**/v1/notifications/**', r => { asked = true; return r.fulfill({ status: 403 }); });
  await page.addInitScript(() => localStorage.setItem('medvya.locale', 'en'));
  await page.route('**/v1/me/context', r => r.fulfill({ json: persona('HospitalIt', { AuditLog: 'Read' }) }));
  await page.goto('/');
  await expect(page.getByRole('button', { name: 'Notifications' })).toBeDisabled();
  expect(asked).toBe(false);
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
for (const locale of ['en', 'ar'])
  test(`axe finds no violations with the overlay open (${locale}); 44 px targets`, async ({ page }) => {
    await open(page, { locale });
    if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    await page.getByRole('button', { name: /^(Notifications|الإشعارات)، ?|^Notifications, / }).click();
    const panel = page.getByRole('dialog');
    for (const b of await panel.getByRole('button').all()) expect((await b.boundingBox())!.height).toBeGreaterThanOrEqual(44);
    for (const l of await panel.getByRole('link').all()) expect((await l.boundingBox())!.height).toBeGreaterThanOrEqual(44);
    await page.addScriptTag({ path: axePath });
    const violations = await page.evaluate(async () =>
      (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
        .map(v => `${v.id} (${v.nodes.length})`));
    expect(violations).toEqual([]);
  });
