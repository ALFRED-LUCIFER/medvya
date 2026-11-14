import { expect, test, type Page } from '@playwright/test';
import { choosePatient, expectNoAxeViolations, signIn, watchUrls } from './support';

// MDV-P0-029: the front-desk journey of docs/02-design/journeys.md J3 (busy reception) through the real browser, real Host.Api and real
// PostgreSQL: sign in -> register -> book -> check in -> board -> cancel. No API is mocked. Synthetic people only.
const joseph = /A\. Joseph/;
// A date of birth and mobile of this run only, so a re-run on a kept database is not flagged as a duplicate (synthetic +91 90000 0xxxx range).
const n = Math.floor(Math.random() * 9000) + 1000;
const patient = {
  name: `Kavya Pillai${[...String(n)].map(d => 'abcdefghij'[+d]).join('')}`, dob: `${1950 + (n % 50)}-${String(1 + (n % 12)).padStart(2, '0')}-${String(1 + (n % 28)).padStart(2, '0')}`,
  mobile: `+91 90000 0${n}`, emergency: 'Ravi Pillai', emergencyMobile: `+91 90000 1${n}`,
};
const phiWords = ['Kavya', 'Pillai', 'Ravi', patient.dob, `0${n}`];

const rao = (page: Page) => page.getByRole('list', { name: /Dr\. Arjun Rao/ });

test('front desk: register, book, check in, see the board, cancel', async ({ page }) => {
  const watch = watchUrls(page);
  let patientId = '';

  await test.step('sign in as the receptionist and land on the board', async () => {
    await signIn(page, joseph);
    await page.goto('/app/front-desk');
    await expect(page.getByRole('heading', { level: 1, name: /^Front desk/ })).toBeVisible();
    await expect(page.getByText('Loading Front desk…')).toBeHidden();
    await expectNoAxeViolations(page);
  });

  await test.step('register a patient with the India minimum', async () => {
    await page.getByRole('link', { name: 'Register patient' }).first().click();
    await page.getByLabel('Full name').fill(patient.name);
    await page.getByRole('textbox', { name: 'Date of birth (required)', exact: true }).fill(patient.dob);
    await page.getByRole('radio', { name: 'Female', exact: true }).check();
    await page.getByRole('button', { name: 'Continue' }).click();
    await page.getByLabel('Mobile').fill(patient.mobile);
    await page.getByRole('button', { name: 'Continue' }).click();
    await page.getByLabel('Emergency contact name').fill(patient.emergency);
    await page.getByRole('combobox', { name: 'Relationship (required)', exact: true }).click();
    await page.getByRole('option', { name: 'Spouse', exact: true }).click();
    await page.getByLabel('Emergency contact phone').fill(patient.emergencyMobile);
    await page.getByRole('button', { name: 'Continue' }).click();
    await page.getByRole('button', { name: 'Continue' }).click();
    await page.getByRole('button', { name: 'Continue' }).click();
    const created = page.waitForResponse(r => r.request().method() === 'POST' && new URL(r.url()).pathname === '/v1/patients');
    await page.getByRole('button', { name: 'Register patient' }).click();
    const body = await (await created).json() as { patientId: string };
    patientId = body.patientId;
    await expect(page.getByRole('heading', { name: `${patient.name} is registered` })).toBeVisible();
    await expectNoAxeViolations(page);
  });

  let reference = '';
  await test.step('book the first free slot of Dr. Rao, with an Idempotency-Key the server dedupes', async () => {
    await page.getByRole('link', { name: 'Schedule' }).first().click();
    await expect(rao(page)).toBeVisible();
    await expectNoAxeViolations(page);
    await rao(page).getByRole('button', { name: /^Book / }).first().click();
    const panel = await choosePatient(page, patient.mobile, new RegExp(`^${patient.name}, \\d+ y, Female`));
    await expect(panel).toContainText(`${patient.name} · MRN`);
    const booking = page.waitForRequest(r => r.method() === 'POST' && new URL(r.url()).pathname === '/v1/appointments');
    const booked = page.waitForResponse(r => r.request().method() === 'POST' && new URL(r.url()).pathname === '/v1/appointments');
    await panel.getByRole('button', { name: 'Confirm booking' }).click();
    const request = await booking;
    const key = request.headers()['idempotency-key'];
    expect(key).toBeTruthy();
    const first = await (await booked).json() as { id: string; reference: string };
    reference = first.reference;
    await expect(panel).toContainText('Appointment booked');
    // The same request again (a retry after a lost answer) replays the first result and books nothing new.
    const again = await page.request.post('/v1/appointments', { data: request.postDataJSON(), headers: { ...request.headers(), 'content-type': 'application/json' } });
    expect(again.status()).toBeLessThan(300);
    expect(await again.json()).toMatchObject({ id: first.id, reference: first.reference });
  });

  await test.step('check in on the board after confirming name and date of birth, then mark ready for the doctor (no nurse step)', async () => {
    await page.getByRole('link', { name: 'Front desk' }).first().click();
    const card = page.getByRole('article', { name: patient.name });
    await expect(card).toBeVisible();
    await card.getByRole('button', { name: 'Check in' }).click();
    const confirm = card.getByRole('group', { name: 'Confirm identity' });
    await expect(confirm).toContainText(`${patient.name} · born`);
    await confirm.getByRole('button', { name: 'Confirm arrival' }).click();
    const ready = page.getByRole('region', { name: /^Ready for doctor/ }); // nurse intake is off (the default): check-in goes straight to Ready for doctor (#1104)
    await expect(ready.getByRole('article', { name: patient.name })).toBeVisible();
    await expectNoAxeViolations(page);
    await page.reload(); // the server, not the screen, holds the state
    await expect(page.getByRole('region', { name: /^Ready for doctor/ }).getByRole('article', { name: patient.name })).toBeVisible();
  });

  await test.step('book a second visit and cancel it with a reason; the slot is free again', async () => {
    await page.getByRole('link', { name: 'Schedule' }).first().click();
    const slots = rao(page).getByRole('button', { name: /^Book / });
    await slots.first().click();
    const panel = await choosePatient(page, patient.mobile, new RegExp(`^${patient.name}, \\d+ y, Female`));
    await panel.getByRole('button', { name: 'Confirm booking' }).click();
    await expect(panel).toContainText('Appointment booked');
    await panel.getByRole('button', { name: 'Done' }).click();
    await rao(page).getByRole('button', { name: new RegExp(`^${patient.name} Booked`) }).click();
    const visit = page.getByRole('complementary', { name: 'Appointment' });
    await visit.getByRole('button', { name: 'Cancel appointment' }).click();
    await visit.getByRole('combobox', { name: 'Reason' }).click();
    await page.getByRole('option').first().click();
    await visit.getByRole('button', { name: 'Cancel appointment' }).click();
    await expect(visit).toContainText('Appointment cancelled. The slot is free again.');
    await page.getByRole('link', { name: 'Front desk' }).first().click();
    await expect(page.getByRole('region', { name: /^Ready for doctor/ }).getByRole('article', { name: patient.name })).toBeVisible();
    await expect(page.getByRole('article', { name: patient.name })).toHaveCount(1); // the cancelled visit left the board
  });

  await test.step('sign out ends the provider session too and comes back to the sign-in start (#912)', async () => {
    const providerSignOut = page.waitForRequest(r => new URL(r.url()).pathname === '/dev-idp/logout');
    await page.getByRole('button', { name: /^Signed in as / }).click();
    await page.getByRole('menuitem', { name: 'Sign out' }).click();
    expect(new URL((await providerSignOut).url()).searchParams.get('post_logout_redirect_uri')).toMatch(/\/signout-callback-oidc$/);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Sign in to Medvya');
  });

  expect(patientId).toMatch(/^[0-9a-f-]{36}$/);
  expect(reference).toMatch(/^APT-/);
  expect(watch.leaks(phiWords)).toEqual([]);
});
