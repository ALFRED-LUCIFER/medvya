import { createRequire } from 'node:module';
import { expect, test, type Page, type Request } from '@playwright/test';

// MDV-P1-025 walk-in: quick registration (or search) → walk-in → checked in, card tagged Walk-in. CI has no API: the session,
// patients, schedule and appointment endpoints are mocked with the synthetic cast in the server's shapes; no double booking,
// queueing and audit are covered by WalkInTests on SQL Server.
const reception = {
  user: { id: 'u-joseph', displayName: 'A. Joseph' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role: 'Receptionist' }],
  active: {
    membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role: 'Receptionist', flags: [], country: 'IN',
    capabilities: { RegisterBookPatients: 'Full', CheckInQueue: 'Full', InvoicesPayments: 'Full', Patient360Chart: 'None', ClinicalNotes: 'None' },
  },
};
const patientId = '5e1d0001-0004-4000-8000-000000000777';
const ist = (hhmm: string) => {
  const [h, m] = hhmm.split(':').map(Number);
  return new Date(Date.parse('2026-10-19T00:00:00Z') + ((h! * 60 + m!) - 330) * 60_000).toISOString();
};
const column = (calendarId: string, name: string) => ({
  calendarId, calendarKind: 'Doctor', name, slotMinutes: 20, hours: [], slots: [], unavailable: [], appointments: [], visitLengths: [], capacity: 0,
});

type Calls = { walkIns: Request[]; checkIns: Request[] };

async function desk(page: Page, { locale = 'en', walkInAnswer = (_: { calendarId: string | null }) => ({ status: 200, json: {} as unknown }) } = {}) {
  const calls: Calls = { walkIns: [], checkIns: [] };
  let board: unknown[] = [];
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: reception }));
  await page.route('**/v1/schedule?*', r => r.fulfill({
    json: { facilityId: 'f-1', timeZone: 'Asia/Kolkata', date: '2026-10-19', start: ist('08:00'), end: ist('21:00'),
      columns: [column('d-1', 'Dr. Arjun Rao (synthetic)'), column('d-2', 'Dr. S. Menon (synthetic)'), { ...column('r-1', 'Procedure room (synthetic)'), calendarKind: 'Resource' }] },
  }));
  await page.route(`**/v1/schedule/patients/${patientId}?*`, r => r.fulfill({ json: { id: patientId, fullName: 'Arjun Nair (synthetic)', mrn: '000777' } }));
  await page.route('**/v1/patients/duplicate-check', r => r.fulfill({ json: { candidates: [] } }));
  await page.route('**/v1/patients', r => r.fulfill({ json: { patientId, mrn: '000777', registeredAt: '2026-10-19T05:00:00Z', status: 'Incomplete', warnings: [] } }));
  await page.route('**/v1/appointments/walk-in', r => {
    calls.walkIns.push(r.request());
    const body = r.request().postDataJSON() as { calendarId: string | null };
    const { status, json } = walkInAnswer(body);
    if (status !== 200) return r.fulfill({ status, json });
    const unscheduled = body.calendarId !== null && (json as { unscheduled?: boolean }).unscheduled === true;
    const visit = { id: 'a-9', calendarId: body.calendarId ?? 'd-1', start: unscheduled ? ist('19:45') : ist('10:40'), end: unscheduled ? ist('19:45') : ist('11:00'),
      walkIn: true, unscheduled, patientName: 'Arjun Nair (synthetic)', state: 'Booked' };
    board = [{ id: 'a-9', reference: 'APT-A9', patientId, patientName: 'Arjun Nair (synthetic)', dateOfBirth: '1990-01-01', registrationIncomplete: true,
      calendarId: visit.calendarId, calendarName: 'Dr. Arjun Rao (synthetic)', visitType: 'FollowUp', reasonCategory: 'General', start: visit.start, end: visit.end,
      state: 'Booked', since: visit.start, room: null, walkIn: true, unscheduled }];
    return r.fulfill({ json: visit });
  });
  await page.route('**/v1/appointments/*/check-in', r => {
    calls.checkIns.push(r.request());
    Object.assign(board[0] as object, { state: 'CheckedIn', since: ist('10:32') });
    return r.fulfill({ json: board[0] });
  });
  await page.route('**/v1/front-desk/board?*', r => r.fulfill({ json: { facilityId: 'f-1', timeZone: 'Asia/Kolkata', date: '2026-10-19', now: ist('10:44'), cards: board } }));
  return calls;
}

test('a new walk-in is quick-registered and checked in within a minute, then waits on the board tagged Walk-in', async ({ page }) => {
  const calls = await desk(page);
  const started = Date.now();
  await page.goto('/front-desk');
  await page.getByRole('link', { name: 'Walk-in' }).click();
  await page.getByRole('link', { name: 'New patient: quick registration' }).click();
  await expect(page.getByRole('link', { name: 'Quick (busy desk)' })).toHaveAttribute('aria-current', 'page');
  await page.getByLabel('Full name').fill('Arjun Nair (synthetic)');
  await page.getByLabel('Mobile').fill('+91 90000 00777');
  await page.getByRole('textbox', { name: 'Date of birth', exact: true }).fill('1990-01-01');
  await page.getByRole('radio', { name: 'Male', exact: true }).check();
  await page.getByRole('button', { name: 'Register patient' }).click();
  await page.getByRole('link', { name: 'Check in now (walk-in)' }).click();

  await expect(page).toHaveURL(`/front-desk/walk-in?patient=${patientId}`); // an opaque id, never patient data
  await expect(page.getByText('Patient: Arjun Nair (synthetic) · MRN 000777')).toBeVisible();
  await page.getByRole('button', { name: 'Check in now' }).click();
  await expect(page.getByRole('heading', { name: 'Arjun Nair (synthetic) is checked in' })).toBeVisible();
  await expect(page.getByText('Walk-in with Dr. Arjun Rao (synthetic) at 10:40.', { exact: false })).toBeVisible();
  expect(Date.now() - started).toBeLessThan(60_000);

  // The walk-in, then the board's own check-in with the time taken (exit gate 3).
  expect(calls.walkIns[0]!.postDataJSON()).toEqual({ facilityId: 'f-1', patientId, calendarId: null, visitType: 'FollowUp', reasonCategory: 'General' });
  expect(new URL(calls.walkIns[0]!.url()).search).toBe('');
  expect(calls.checkIns[0]!.url()).toContain('/v1/appointments/a-9/check-in');
  expect((calls.checkIns[0]!.postDataJSON() as { elapsedMs: number }).elapsedMs).toBeGreaterThan(0);

  await page.getByRole('link', { name: 'Back to the board' }).first().click();
  const checkedIn = page.getByRole('region', { name: /Checked in/ });
  await expect(checkedIn.getByText('Walk-in', { exact: true })).toBeVisible();
  await expect(checkedIn.getByText('Waiting 12 min')).toBeVisible();
});

test('with no free slot today the desk chooses a doctor and the patient is queued, not double-booked', async ({ page }) => {
  const calls = await desk(page, {
    walkInAnswer: b => b.calendarId === null
      ? { status: 409, json: { title: 'Slot no longer available', code: 'no_free_slot' } }
      : { status: 200, json: { unscheduled: true } },
  });
  await page.goto(`/front-desk/walk-in?patient=${patientId}`);
  await expect(page.getByText('Patient: Arjun Nair (synthetic) · MRN 000777')).toBeVisible();
  await page.getByRole('button', { name: 'Check in now' }).click();
  await expect(page.getByRole('alert')).toHaveText('No free slot today. Choose a doctor to queue the patient with.');
  expect(calls.checkIns).toHaveLength(0);

  await page.getByRole('combobox', { name: 'Doctor' }).click();
  await expect(page.getByRole('option', { name: 'Procedure room (synthetic)' })).toHaveCount(0); // doctors only
  await page.getByRole('option', { name: 'Dr. Arjun Rao (synthetic)' }).click();
  await page.getByRole('button', { name: 'Check in now' }).click();
  await expect(page.getByText('No free slot today: queued for Dr. Arjun Rao (synthetic) without taking a slot.', { exact: false })).toBeVisible();
  expect(calls.walkIns.at(-1)!.postDataJSON()).toMatchObject({ calendarId: 'd-1' });
  expect(calls.checkIns).toHaveLength(1);
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
for (const locale of ['en', 'hi', 'ar'])
  for (const width of [1366, 375])
    test(`walk-in page is axe clean and fits (${locale}, ${width}px)`, async ({ page }) => {
      await page.setViewportSize({ width, height: 900 });
      await desk(page, { locale });
      await page.goto(`/front-desk/walk-in?patient=${patientId}`);
      await expect(page.locator('main h1')).toBeVisible();
      await expect(page.locator('main [role=status]').first()).toBeVisible(); // the patient lookup has answered
      await expect(page.getByRole('combobox', { name: 'Doctor' }).or(page.locator('#walk-in-doctor'))).toBeEnabled(); // the doctors have loaded
      await page.waitForLoadState('networkidle'); // axe sees the settled page, not a transition
      expect(await page.evaluate(() => document.documentElement.dir)).toBe(locale === 'ar' ? 'rtl' : 'ltr');
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
      for (const b of await page.locator('main button, main a, main input').all())
        if (await b.isVisible()) expect((await b.boundingBox())!.height).toBeGreaterThanOrEqual(44);
      await page.addScriptTag({ path: axePath });
      const violations = await page.evaluate(async () =>
        (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
          .map(v => `${v.id} (${v.nodes.length}) ${JSON.stringify((v.nodes as { target: unknown }[]).map(n => n.target))}`));
      expect(violations).toEqual([]);
    });
