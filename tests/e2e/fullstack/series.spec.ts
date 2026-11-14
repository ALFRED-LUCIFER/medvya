import { expect, test, type Page } from '@playwright/test';
import { choosePatient, expectNoAxeViolations, signIn, syntheticPatient, watchUrls } from './support';

// MDV-P1-026: a recurring series through the real browser, Host.Api and PostgreSQL (no mocked API). A weekly series of four from the first
// free slot of Dr. Rao four Tuesdays on; the second week is already taken, so the review shows the conflict and the desk moves it a day later. Then "this and
// following" from the third visit cancels the third and fourth and keeps the first two. Synthetic people only (docs/00-overview/personas.md).
const joseph = /A\. Joseph/;
const doctor = 'Dr. Arjun Rao (synthetic)';
const patient = syntheticPatient('Kavya', 'Pillai');
// The browser's today is Tuesday 3 November 2026 (E2E_CLOCK_START). The series starts four weeks later, on days no other spec books (#1251):
// the journey specs that run before this one fill Dr. Rao's today and book a follow-up two weeks on (17 Nov). It falls on four Tuesdays, the second moved to Wednesday.
const days = { first: '2026-11-24', taken: '2026-12-01', moved: '2026-12-02', third: '2026-12-08', fourth: '2026-12-15' };

const rao = (page: Page) => page.getByRole('list', { name: /Dr\. Arjun Rao/ });
const slot = (page: Page, time: string) => rao(page).getByRole('button', { name: `Book ${doctor} at ${time}` });
const visitOf = (page: Page) => rao(page).getByRole('button', { name: new RegExp(`^${patient.name} Booked`) });
const bookingPanel = (page: Page) => page.getByRole('complementary', { name: 'Book appointment' });

test('a weekly series: the review shows the conflict, the desk moves it, four visits are booked; this and following cancels the rest', async ({ page, browser }) => {
  const watch = watchUrls(page);
  let time = '';

  await test.step('register the patient and take the first free slot time of the first day', async () => {
    await signIn(page, joseph);
    await page.goto('/app/front-desk');
    await page.getByRole('link', { name: 'Walk-in' }).click();
    await page.getByRole('link', { name: 'New patient: quick registration' }).click();
    await page.getByLabel('Full name').fill(patient.name);
    await page.getByLabel('Mobile').fill(patient.mobile);
    await page.getByRole('textbox', { name: 'Date of birth (required)', exact: true }).fill(patient.dob);
    await page.getByRole('radio', { name: 'Female', exact: true }).check();
    await page.getByRole('button', { name: 'Register patient' }).click();
    await expect(page.getByRole('link', { name: 'Check in now (walk-in)' })).toBeVisible();
    await page.goto(`/app/schedule?date=${days.first}`);
    const first = await rao(page).getByRole('button', { name: /^Book / }).first().textContent(); // named by its content (MDV-P1-097)
    time = /at (\d\d:\d\d)$/.exec(first ?? '')![1]!;
  });

  await test.step('the same time next week is already booked for another patient', async () => {
    await page.goto(`/app/schedule?date=${days.taken}`);
    await slot(page, time).click();
    await choosePatient(page, 'Meera', /^Meera Iyer \(synthetic\), 54 y, Female/);
    await bookingPanel(page).getByRole('button', { name: 'Confirm booking' }).click();
    await expect(bookingPanel(page)).toContainText('Appointment booked');
  });

  await test.step('review a weekly series of four: the second week shows its conflict before anything is booked', async () => {
    await page.goto(`/app/schedule?date=${days.first}`);
    await slot(page, time).click();
    const panel = await choosePatient(page, patient.mobile, new RegExp(`^${patient.name}, \\d+ y, Female`));
    await panel.locator('summary').click(); // UX-10: repeating is folded until asked
    await panel.getByLabel('Number of visits').fill('4');
    await panel.getByRole('button', { name: 'Review series' }).click();
    const rows = panel.getByRole('listitem');
    await expect(rows).toHaveCount(4);
    await expect(rows.nth(0)).toContainText(`Tue, 24 Nov at ${time}`);
    await expect(rows.nth(1)).toContainText(`Tue, 1 Dec at ${time}`);
    await expect(rows.nth(1)).toContainText('This slot is no longer available'); // words, not colour alone
    await expect(rows.nth(1).getByRole('checkbox')).not.toBeChecked(); // a conflict starts skipped
    for (const i of [0, 2, 3]) await expect(rows.nth(i)).toContainText('Free');
    await expectNoAxeViolations(page);
  });

  await test.step('move the conflicting week to Wednesday and book the four in one go', async () => {
    const panel = bookingPanel(page);
    const rows = panel.getByRole('listitem');
    const day = rows.nth(1).getByLabel('Move to (day)');
    await day.fill(days.moved);
    await day.blur();
    await expect(rows.nth(1)).toContainText(`Wed, 2 Dec at ${time}`);
    await expect(rows.nth(1).getByRole('checkbox')).toBeChecked();
    const booked = page.waitForResponse(r => r.request().method() === 'POST' && new URL(r.url()).pathname === '/v1/appointments/series');
    await panel.getByRole('button', { name: 'Book visits (4)' }).click();
    const result = await (await booked).json() as { occurrences: { appointment: { id: string } | null; conflict: string | null }[] };
    expect(result.occurrences.map(o => [!!o.appointment?.id, o.conflict])).toEqual([[true, null], [true, null], [true, null], [true, null]]);
    await expect(panel).toContainText('Visits booked: 4');
    await expect(panel).not.toContainText('Not booked');
  });

  await test.step('each visit is an ordinary booked appointment on its day, and the taken week has none', async () => {
    for (const d of [days.first, days.moved, days.third, days.fourth]) {
      await page.goto(`/app/schedule?date=${d}`);
      await expect(visitOf(page)).toHaveCount(1);
    }
    await page.goto(`/app/schedule?date=${days.taken}`);
    await expect(rao(page)).toBeVisible();
    await expect(visitOf(page)).toHaveCount(0);
  });

  await test.step('cancel this and following from the third visit with one reason: the third and fourth go, the first two stay', async () => {
    await page.goto(`/app/schedule?date=${days.third}`);
    await visitOf(page).click();
    const visit = page.getByRole('complementary', { name: 'Appointment' });
    await visit.getByRole('button', { name: 'Cancel appointment' }).click();
    await visit.getByRole('combobox', { name: 'Reason' }).click();
    await page.getByRole('option', { name: 'Patient unwell' }).click();
    const cancelled = page.waitForRequest(r => r.method() === 'POST' && new URL(r.url()).pathname.endsWith('/cancel-following'));
    await visit.getByRole('button', { name: 'Cancel this and following' }).click();
    expect((await cancelled).postDataJSON()).toEqual({ reason: 'PatientUnwell', note: null });
    await expect(visit).toContainText('Cancelled this and the following visits (2)');
    for (const d of [days.third, days.fourth]) {
      await page.goto(`/app/schedule?date=${d}`);
      await expect(slot(page, time)).toBeVisible(); // the slot is free again
      await expect(visitOf(page)).toHaveCount(0);
    }
    for (const d of [days.first, days.moved]) {
      await page.goto(`/app/schedule?date=${d}`);
      await expect(visitOf(page)).toHaveCount(1);
    }
  });

  await test.step('the series review is axe-clean in Arabic, right to left', async () => {
    const ar = await browser.newPage();
    try {
      await signIn(ar, joseph, 'ar');
      await ar.goto(`/app/schedule?date=${days.fourth}`);
      await expect(ar.locator('html')).toHaveAttribute('dir', 'rtl');
      await rao(ar).getByRole('button').first().click(); // the slot freed by the cancel; names are Arabic here
      const panel = ar.getByRole('complementary').first();
      await panel.getByRole('searchbox').fill(patient.mobile);
      await panel.getByRole('searchbox').press('Enter');
      await panel.getByRole('list').last().getByRole('button').click();
      await panel.locator('summary').click(); // UX-10: repeating is folded until asked
      await panel.locator('#booking-repeat').fill('2');
      await panel.locator('button[type=submit]').click();
      await expect(panel.getByRole('checkbox')).toHaveCount(2);
      await expectNoAxeViolations(ar);
    } finally {
      await ar.close(); // nothing booked: the review is not confirmed, and the hold lapses
    }
  });

  expect(watch.leaks(['Kavya', 'Pillai', patient.dob, patient.mobile.slice(-5), 'Meera'])).toEqual([]);
});
