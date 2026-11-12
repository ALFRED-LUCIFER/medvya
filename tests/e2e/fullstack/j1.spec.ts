import { createRequire } from 'node:module';
import { expect, test } from '@playwright/test';
import { browserNow, choosePatient, expectNoAxeViolations, signIn, watchUrls } from './support';

// MDV-P0-029: journey J1 (first owner-doctor visit, docs/02-design/journeys.md) through the real stack: a brand-new person signs up at the
// development identity provider, sets up a practice, then registers a patient, books and checks in, and the patient's second visit is the
// returning check-in the pilot metrics time (MDV-P1-041, exit gate 3). Step 2 (fees catalogue) and steps 5-8
// (consent, recording, note, invoice) have no screens yet and are not faked here. Synthetic people only.
const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
const n = Math.floor(Math.random() * 9000) + 1000;
const patient = {
  name: `Latha Iyengar${[...String(n)].map(d => 'abcdefghij'[+d]).join('')}`, dob: `${1950 + (n % 50)}-${String(1 + (n % 12)).padStart(2, '0')}-${String(1 + (n % 28)).padStart(2, '0')}`,
  mobile: `+91 90000 0${n}`, emergency: 'Gopal Iyengar', emergencyMobile: `+91 90000 1${n}`,
};

test('J1: a new clinic owner sets up the practice, Medvya approves it, then a patient is registered, booked, checked in and comes back', async ({ page, browser }) => {
  const watch = watchUrls(page);
  await page.addInitScript({ path: axePath });
  await page.clock.setFixedTime(browserNow());

  await test.step('J1.1 sign up with multi-factor sign-in and set up the practice (licence, clinic, hours, receptionist invited)', async () => {
    await page.goto('/app/signup?country=IN');
    await page.getByLabel('Practice name').fill(`Menon Clinic ${n} (synthetic)`);
    await page.getByLabel('City').fill('Chennai');
    await page.getByRole('button', { name: 'Continue' }).click();
    await page.getByLabel('Full name').fill('Arjun Menon (synthetic)');
    await page.getByLabel('Work email').fill(`owner${n}@example.test`);
    await page.getByRole('button', { name: 'Continue' }).click();
    await page.getByRole('link', { name: /New synthetic person/ }).click();
    await page.waitForURL(/\/app\/onboarding/);
    await page.getByRole('button', { name: 'Start setup' }).click();
    // The create-account answers (MDV-P1-059) are already in the draft: practice type, name, practice name, and the city as the address start.
    await expect(page.getByLabel('Full name')).toHaveValue('Arjun Menon (synthetic)');
    await expect(page.getByLabel('Practice name')).toHaveValue(`Menon Clinic ${n} (synthetic)`);
    await page.getByLabel(/^Address/).fill('12 Test Street, Chennai');
    await page.getByLabel('A clinic').check(); // ADR-016: Clinic or Hospital is chosen first; a doctor who works alone registers a clinic
    await page.getByLabel('Email Medvya writes to about this request').fill(`owner${n}@example.test`);
    await page.getByLabel('General practice').check();
    await page.getByRole('combobox', { name: 'Medical council' }).click();
    await page.getByRole('option', { name: /TNMC/ }).click();
    await page.getByLabel('Registration number').fill('84213');
    await page.getByLabel('Registration expiry date').fill('2031-12-31'); // required since #998
    await page.getByLabel('I confirm this registration is my own and that it is valid.').check(); // ADR-016: the owner-doctor attests their own licence
    await page.getByRole('button', { name: 'Continue' }).click();
    await page.getByRole('button', { name: 'Continue' }).click(); // hours and fees: the design defaults
    await page.getByLabel('Email address').fill(`reception${n}@example.test`);
    await page.getByRole('button', { name: 'Invite', exact: true }).click(); // front desk is the default role
    await page.waitForLoadState('networkidle'); // the draft autosave must settle before axe reads the page
    await expectNoAxeViolations(page);
    await page.getByRole('button', { name: 'Continue' }).click();
    await page.getByLabel('Start fresh').check();
    await page.getByRole('button', { name: 'Continue' }).click();
    const finished = page.waitForResponse(r => new URL(r.url()).pathname === '/v1/onboarding/finish');
    await page.getByRole('button', { name: 'Finish setup' }).click();
    // The invitation is saved with the practice and emailed after the response (#205), so the stack's missing mail server does not fail it.
    expect(await (await finished).json()).toMatchObject({ role: 'SoloDoctor', invitationsSent: 1, invitationsFailed: 0 });
    await expect(page.getByRole('heading', { level: 1, name: `Menon Clinic ${n} (synthetic) is ready.` })).toBeVisible();
    await expect(page.getByText(/could not be sent/)).toHaveCount(0);
    await expectNoAxeViolations(page);
    await expect(page.getByText(/add the documents Medvya needs and send the request/)).toBeVisible();
    // ADR-016: the new organisation is set-up only until Medvya approves it. The ready screen leads to the onboarding request.
    await page.getByRole('button', { name: 'Add documents and send the request' }).click();
    await expect(page.getByRole('heading', { level: 1, name: 'Onboarding request' })).toBeVisible();
    await expect(page.getByRole('note').filter({ hasText: 'Medvya has not approved it yet' })).toBeVisible();
    const refused = await page.request.post('/v1/patients/search', { headers: { 'X-Medvya-Csrf': '1' }, data: {} });
    expect(refused.status()).toBe(403); // the server, not the screen, keeps patients off before approval
    // The staff list of the new organisation: set-up works at once for the owner (Staff, roles, integrations = Config).
    const staff = await page.request.get('/v1/staff');
    expect(staff.status(), await staff.text()).toBe(200);
    // shortcut: the stack has no SMTP sink (that is `pnpm services:up`), so delivery is not asserted here; the integration tests check the email.
    expect(await staff.json()).toContainEqual(expect.objectContaining({ email: `reception${n}@example.test`, status: 'Invited' }));
  });

  await test.step('J1.1 submit the onboarding request, Medvya asks for a change, the owner resubmits, Medvya approves', async () => {
    const pdf = { name: 'document.pdf', mimeType: 'application/pdf', buffer: Buffer.from('%PDF-1.4 synthetic') };
    const tenantId = (await (await page.request.get('/v1/me/context')).json()).memberships[0].tenantId as string;
    await expect(page.getByRole('button', { name: 'Send request to Medvya' })).toBeDisabled();
    for (const label of ['Clinic registration certificate', 'Tax registration (GSTIN or TRN)', /^Owner’s government ID/, 'Your medical council registration'])
      await page.getByLabel(label).setInputFiles(pdf);
    await expect(page.getByText('Not added yet')).toHaveCount(0);
    await expectNoAxeViolations(page);
    await page.getByRole('button', { name: 'Send request to Medvya' }).click();
    await expect(page.getByRole('status').filter({ hasText: 'Sent. Waiting for Medvya to review it.' })).toBeVisible();
    await expect(page.getByRole('note').filter({ hasText: 'Waiting for approval' })).toBeVisible();

    // Medvya operations, through the interim API (docs/runbooks/organisation-approval.md), in their own browser.
    const ops = await browser.newPage();
    await signIn(ops, /K\. Lakshmi/);
    const act = (action: string, reason: string) => ops.request.post(`/v1/operations/organisations/${tenantId}/${action}`, { headers: { 'X-Medvya-Csrf': '1' }, data: { reason } });
    const queue = await ops.request.get('/v1/operations/organisations/?filter=Waiting');
    expect(await queue.json()).toContainEqual(expect.objectContaining({ tenantId, state: 'Submitted', type: 'Clinic' }));
    expect((await act('request-changes', 'The registration certificate is unreadable')).status()).toBe(204);

    await page.reload();
    await expect(page.getByText('The registration certificate is unreadable')).toBeVisible();
    await page.getByLabel('Clinic registration certificate').setInputFiles(pdf);
    await page.getByRole('button', { name: 'Send again to Medvya' }).click();
    await expect(page.getByRole('status').filter({ hasText: 'Sent. Waiting for Medvya to review it.' })).toBeVisible();
    expect((await act('approve', 'Documents checked')).status()).toBe(204);
    await ops.close();

    await page.goto('/app/');
    await expect(page.getByRole('navigation').first()).toBeVisible();
    await expect(page.getByText(/Waiting for approval|being set up/)).toHaveCount(0); // approved: patients, booking and notes are on
    await expect(page).not.toHaveURL(/onboarding/);
  });

  await test.step('J1.3 from an empty slot, register the first patient with the India minimum, ABHA left empty', async () => {
    // MDV-P1-021: the patient is not on file yet, so the desk registers them from the booking panel, which holds the slot meanwhile.
    await page.getByRole('link', { name: 'Schedule' }).first().click();
    const slots = page.getByRole('list', { name: /Arjun Menon/ });
    await expect(slots).toBeVisible();
    await slots.getByRole('button', { name: /^Book / }).first().click();
    const panel = page.getByRole('complementary', { name: 'Book appointment' });
    await expect(panel).toContainText('Held for you until');
    await panel.getByRole('link', { name: 'Register a new patient' }).click();
    await page.getByLabel('Full name').fill(patient.name);
    await page.getByRole('textbox', { name: 'Date of birth', exact: true }).fill(patient.dob);
    await page.getByRole('radio', { name: 'Female', exact: true }).check();
    await page.getByRole('button', { name: 'Continue' }).click();
    await page.getByLabel('Mobile').fill(patient.mobile);
    await page.getByRole('button', { name: 'Continue' }).click();
    await page.getByLabel('Emergency contact name').fill(patient.emergency);
    await page.getByRole('combobox', { name: 'Relationship', exact: true }).click();
    await page.getByRole('option', { name: 'Spouse', exact: true }).click();
    await page.getByLabel('Emergency contact phone').fill(patient.emergencyMobile);
    for (let i = 0; i < 3; i++) await page.getByRole('button', { name: 'Continue' }).click();
    await page.getByRole('button', { name: 'Register patient' }).click();
    await expect(page.getByRole('heading', { name: `${patient.name} is registered` })).toBeVisible();
  });

  await test.step('J1.4 back to the held slot with the new patient chosen, book, and check in on the board', async () => {
    await page.getByRole('link', { name: 'Back to the booking' }).click();
    const panel = page.getByRole('complementary', { name: 'Book appointment' });
    await expect(panel).toContainText('Held for you until'); // held again for this desk
    await expect(panel).toContainText(`${patient.name} · MRN`); // the new patient is chosen
    await expectNoAxeViolations(page);
    await panel.getByRole('combobox', { name: 'Reason category' }).click();
    await page.getByRole('option').first().click();
    await panel.getByRole('button', { name: 'Confirm booking' }).click();
    await expect(panel).toContainText('Appointment booked');
    await page.getByRole('link', { name: 'Front desk' }).first().click();
    const card = page.getByRole('article', { name: patient.name });
    await card.getByRole('button', { name: 'Check in' }).click();
    await card.getByRole('group', { name: 'Confirm identity' }).getByRole('button', { name: 'Confirm arrival' }).click();
    await expect(page.getByRole('region', { name: /^Checked in/ }).getByRole('article', { name: patient.name })).toBeVisible();
    await expectNoAxeViolations(page);
  });

  await test.step('J1.4 the owner sees the timed check-in in the pilot metrics and records a baseline (MDV-P1-041)', async () => {
    await page.goto('/app/settings?tab=metrics');
    const report = page.getByRole('region', { name: 'Pilot metrics' });
    // The first visit of a new patient: timed, but not a returning patient's check-in (exit gate 3 counts those).
    await expect(report).toContainText('0 returning of 1 timed check-ins');
    await page.getByLabel('Value').fill('22');
    await page.getByLabel('How it was measured').fill('Paper register, every visit');
    await page.getByLabel('Measured from').fill('2026-10-19');
    await page.getByLabel('Measured to').fill('2026-11-01');
    await page.getByRole('button', { name: 'Record baseline' }).click();
    await expect(report).toContainText('Before Medvya: 22% (');
    await expectNoAxeViolations(page);
  });

  await test.step('J1.4 a second visit of the same patient is a returning check-in; its time is what exit gate 3 reads (MDV-P1-041)', async () => {
    await page.getByRole('link', { name: 'Schedule' }).first().click();
    // The clock is fixed and the first visit took the first free slot, so the first free slot now is later: this visit is the returning one.
    await page.getByRole('list', { name: /Arjun Menon/ }).getByRole('button', { name: /^Book / }).first().click();
    const panel = await choosePatient(page, patient.mobile, /^L I, \d+ y, Female/);
    await expect(panel).toContainText(`${patient.name} · MRN`);
    await panel.getByRole('button', { name: 'Confirm booking' }).click();
    await expect(panel).toContainText('Appointment booked');
    await page.getByRole('link', { name: 'Front desk' }).first().click();
    const card = page.getByRole('region', { name: /^Booked/ }).getByRole('article', { name: patient.name }); // the first visit is already in Checked in
    const checkIn = card.getByRole('button', { name: 'Check in' });
    await expect(checkIn).toBeVisible();
    const started = Date.now(); // the stopwatch: tap "Check in" to the card in Checked in
    await checkIn.click();
    await card.getByRole('group', { name: 'Confirm identity' }).getByRole('button', { name: 'Confirm arrival' }).click();
    await expect(page.getByRole('region', { name: /^Checked in/ }).getByRole('article', { name: patient.name })).toHaveCount(2);
    const stopwatchSeconds = (Date.now() - started) / 1000;

    await page.goto('/app/settings?tab=metrics');
    const report = page.getByRole('region', { name: 'Pilot metrics' });
    await expect(report).toContainText('1 returning of 2 timed check-ins');
    await expect(report).toContainText('Target of 60 s met.'); // a desk run by a script is well inside the gate; a gap would read "s over the 60 s target"
    // The time the app recorded is the desk's real time: more than nothing, and never more than the stopwatch around it.
    const facilityId = (await (await page.request.get('/v1/me/context')).json()).active.facilityId as string;
    const metrics = await (await page.request.get(`/v1/schedule/metrics?facilityId=${facilityId}&days=14`)).json();
    expect(metrics.checkIn.returningMedianSeconds).toBeGreaterThan(0);
    expect(metrics.checkIn.returningMedianSeconds).toBeLessThanOrEqual(stopwatchSeconds);
    await expectNoAxeViolations(page);
    // The same report in Arabic: right to left and axe-clean (the owner signed up in this browser, so the locale is switched here).
    await page.evaluate(() => localStorage.setItem('medvya.locale', 'ar'));
    await page.reload();
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    await expect(page.getByRole('main')).toBeVisible();
    await page.waitForLoadState('networkidle');
    await expectNoAxeViolations(page);
    await page.evaluate(() => localStorage.setItem('medvya.locale', 'en'));
  });

  expect(watch.leaks(['Latha', 'Iyengar', 'Gopal', patient.dob, `0${n}`])).toEqual([]);
});
