import { createRequire } from 'node:module';
import { type Page, expect, test } from '@playwright/test';
import { browserNow, choosePatient, expectNoAxeViolations, mailTo, signIn, stepUp, watchUrls } from './support';

// MDV-P0-029: journey J1 (first owner-doctor visit, docs/02-design/journeys.md) through the real stack: a brand-new person signs up at the
// development identity provider, sets up a practice, then registers a patient, books and checks in, and the patient's second visit is the
// returning check-in the pilot metrics time (MDV-P1-041, exit gate 3). Then the rest of the pilot flow: the owner adds a doctor and attests the
// licence, the receptionist joins from the emailed invitation, the patient's confirmation arrives by email, and the owner offboards the
// receptionist. Step 2 (fees catalogue) and steps 5-8 (consent, recording, note, invoice) have no screens yet and are not faked here. Synthetic people only.
const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
// MDV-P1-106: a check-in under one second is stored as not measured (no desk is that fast), so the script reads the name and
// date of birth for a moment, as a receptionist does, before confirming.
const deskPauseMs = 1100;
const n = Math.floor(Math.random() * 9000) + 1000;
const letters = [...String(n)].map(d => 'abcdefghij'[+d]).join('');
const owner = `Arjun Menon${letters} (synthetic)`; // unique per run: the development sign-in lists every person by name, and a kept stack holds earlier runs
const patient = {
  name: `Latha Iyengar${letters}`, email: `latha${letters}@example.test`, dob: `${1950 + (n % 50)}-${String(1 + (n % 12)).padStart(2, '0')}-${String(1 + (n % 28)).padStart(2, '0')}`,
  mobile: `+91 90000 0${n}`, emergency: 'Gopal Iyengar', emergencyMobile: `+91 90000 1${n}`,
};

/** The owner's screen in Arabic, right to left and axe-clean, then English again. The owner signed up in this browser, so the locale is switched here. */
async function arabicAxeClean(page: Page, path: string) {
  await page.evaluate(() => localStorage.setItem('medvya.locale', 'ar'));
  await page.goto(path);
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
  await expect(page.getByRole('main')).toBeVisible();
  await page.waitForLoadState('networkidle');
  await expectNoAxeViolations(page);
  await page.evaluate(() => localStorage.setItem('medvya.locale', 'en'));
}

test('J1: a new clinic owner sets up the practice, Medvya approves it, then a patient is registered, booked, checked in and comes back', async ({ page, browser }) => {
  test.setTimeout(300_000); // one story of twelve steps; the confirmation alone waits for the minutely notification run
  const watch = watchUrls(page);
  await page.addInitScript({ path: axePath });
  await page.clock.setFixedTime(browserNow());

  await test.step('J1.1 sign up with multi-factor sign-in and set up the practice (licence, clinic, hours, receptionist invited)', async () => {
    await page.goto('/app/signup?country=IN');
    await page.getByLabel('Practice name').fill(`Menon Clinic ${n} (synthetic)`);
    await page.getByLabel('City').fill('Chennai');
    await page.getByRole('button', { name: 'Continue' }).click();
    await page.getByLabel('Full name').fill(owner);
    await page.getByLabel('Work email').fill(`owner${n}@example.test`);
    await page.getByRole('button', { name: 'Continue' }).click();
    await page.getByRole('link', { name: /New synthetic person/ }).click();
    await page.waitForURL(/\/app\/onboarding/);
    // The country came from sign-up, so onboarding opens on step 1 without asking again (#1365 F-022).
    // The create-account answers (MDV-P1-059) are already in the draft: practice type, name, practice name, and the city as the address start.
    await expect(page.getByLabel('Full name')).toHaveValue(owner);
    await expect(page.getByLabel('Practice name')).toHaveValue(`Menon Clinic ${n} (synthetic)`);
    await page.getByLabel(/^Address/).fill('12 Test Street, Chennai');
    await page.getByLabel('Clinical establishment registration').fill('CE-0001'); // a clinic gives the numbers of the certificates it uploads
    await page.getByLabel('GSTIN').fill('33AAAAA0000A1Z9'); // synthetic: nobody's PAN, with its real check character
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
    // The invitation is saved with the practice and emailed after the response (#205): a slow or failing mail server never fails the set-up. J1.10 reads it.
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
    await stepUp(ops, /K\. Lakshmi/); // an organisation decision needs a fresh identity check (#1417)
    const act = (action: string, reason: string) => ops.request.post(`/v1/operations/organisations/${tenantId}/${action}`, { headers: { 'X-Medvya-Csrf': '1' }, data: { reason } });
    const queue = await ops.request.get('/v1/operations/organisations/?filter=Waiting');
    expect(await queue.json()).toContainEqual(expect.objectContaining({ tenantId, state: 'Submitted', type: 'Clinic' }));
    expect((await act('request-changes', 'The registration certificate is unreadable')).status()).toBe(204);

    await page.reload();
    await expect(page.getByText('The registration certificate is unreadable')).toBeVisible();
    await page.getByLabel('Clinic registration certificate').setInputFiles(pdf);
    await page.getByRole('button', { name: 'Send again to Medvya' }).click();
    await expect(page.getByRole('status').filter({ hasText: 'Sent. Waiting for Medvya to review it.' })).toBeVisible();
    // The operator opens every document of the resubmitted request before approving: the server refuses an approve without those logged views.
    const detail = await (await ops.request.get(`/v1/operations/organisations/${tenantId}`)).json();
    for (const document of detail.documents) expect((await ops.request.get(`/v1/operations/organisations/${tenantId}/documents/${document.id}`)).status()).toBe(200);
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
    await page.getByRole('textbox', { name: 'Date of birth (required)', exact: true }).fill(patient.dob);
    await page.getByRole('radio', { name: 'Female', exact: true }).check();
    await page.getByRole('button', { name: 'Continue' }).click();
    await page.getByLabel('Mobile').fill(patient.mobile);
    await page.getByLabel('Email', { exact: true }).fill(patient.email); // where J1.11's confirmation goes
    await page.getByRole('button', { name: 'Continue' }).click();
    await page.getByLabel('Emergency contact name').fill(patient.emergency);
    await page.getByRole('combobox', { name: 'Relationship (required)', exact: true }).click();
    await page.getByRole('option', { name: 'Spouse', exact: true }).click();
    await page.getByLabel('Emergency contact phone').fill(patient.emergencyMobile);
    for (let i = 0; i < 2; i++) await page.getByRole('button', { name: 'Continue' }).click();
    await page.getByRole('switch', { name: 'Treatment' }).check(); // consents: never pre-ticked (#1365 F-040)
    await page.getByRole('switch', { name: 'Email', exact: true }).check(); // reminders by email: the patient said yes (MDV-P1-032 sends only then)
    await page.getByRole('button', { name: 'Continue' }).click();
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
    await page.waitForTimeout(deskPauseMs);
    await card.getByRole('group', { name: 'Confirm identity' }).getByRole('button', { name: 'Confirm arrival' }).click();
    await expect(page.getByRole('region', { name: /^Ready for doctor/ }).getByRole('article', { name: patient.name })).toBeVisible(); // nurse intake is off (the default): check-in goes straight to Ready for doctor (#1104)
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
    const panel = await choosePatient(page, patient.mobile, new RegExp(`^${patient.name}, \\d+ y, Female`));
    await expect(panel).toContainText(`${patient.name} · MRN`);
    await panel.getByRole('button', { name: 'Confirm booking' }).click();
    await expect(panel).toContainText('Appointment booked');
    await page.getByRole('link', { name: 'Front desk' }).first().click();
    const card = page.getByRole('region', { name: /^Booked/ }).getByRole('article', { name: patient.name }); // the first visit is already in Ready for doctor
    const checkIn = card.getByRole('button', { name: 'Check in' });
    await expect(checkIn).toBeVisible();
    const started = Date.now(); // the stopwatch: tap "Check in" to the card in Ready for doctor
    await checkIn.click();
    await page.waitForTimeout(deskPauseMs);
    await card.getByRole('group', { name: 'Confirm identity' }).getByRole('button', { name: 'Confirm arrival' }).click();
    await expect(page.getByRole('region', { name: /^Ready for doctor/ }).getByRole('article', { name: patient.name })).toHaveCount(2);
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

  await test.step('J1.4 a walk-in is timed from opening Walk-in to the token: 3 clicks for a returning patient, never 0 s (MDV-P1-106)', async () => {
    await page.reload(); // English again
    await page.getByRole('link', { name: 'Front desk' }).first().click();
    await page.getByRole('link', { name: 'Walk-in' }).click(); // click 1
    const search = page.getByRole('searchbox', { name: 'Name, MRN, phone or ID' });
    await expect(search).toBeVisible();
    const opened = Date.now(); // Walk-in is open: the clock is running
    await search.fill(patient.mobile);
    await search.press('Enter');
    await page.getByRole('button', { name: new RegExp(`^${patient.name}, \\d+ y, Female`) }).click(); // click 2
    await expect(page.getByText(`Patient: ${patient.name} · MRN`, { exact: false })).toBeVisible();
    await page.waitForTimeout(deskPauseMs);
    await page.getByRole('button', { name: 'Check in now' }).click(); // click 3
    // She already has two visits today, so the desk is warned and chooses (MDV-P1-052); a first visit of the day has no such step.
    await page.getByRole('button', { name: 'Make a second visit anyway' }).click();
    await expect(page.getByText(/^Token /)).toBeVisible();
    const stopwatchSeconds = (Date.now() - opened) / 1000;

    const facilityId = (await (await page.request.get('/v1/me/context')).json()).active.facilityId as string;
    const { checkIn } = await (await page.request.get(`/v1/schedule/metrics?facilityId=${facilityId}&days=14`)).json();
    // The walk-in is a timed check-in (count 3, none unmeasured): it took over a second because it ran from opening Walk-in.
    // Timed from its last tap alone it would be under a second, so it would be stored as not measured (count 2, unmeasured 1).
    expect(checkIn).toMatchObject({ count: 3, returningCount: 2, unmeasured: 0 });
    expect(checkIn.returningMedianSeconds).toBeGreaterThanOrEqual(1);
    expect(checkIn.returningMedianSeconds).toBeLessThanOrEqual(stopwatchSeconds);
    await page.goto('/app/settings?tab=metrics');
    const report = page.getByRole('region', { name: 'Pilot metrics' });
    await expect(report).toContainText('2 returning of 3 timed check-ins');
    await expect(report).not.toContainText(/(^|[^\d])0 s/);
  });

  const doctor = { name: `Dr. Kavya Nair${letters} (synthetic)`, email: `doctor${n}@example.test` };
  await test.step('J1.9 the owner adds a doctor and attests the licence; the doctor list says who attested, and the invitation is emailed (MDV-P1-007, MDV-P1-065)', async () => {
    await page.reload(); // English again
    await page.goto('/app/settings?tab=doctors');
    await page.getByRole('button', { name: 'Add doctor' }).first().click();
    await page.getByLabel('Full name').fill(doctor.name);
    await page.getByLabel('Email (required)').fill(doctor.email);
    await page.getByLabel('Specialty').fill('Paediatrics');
    await page.getByRole('button', { name: 'Next' }).click();
    await expect(page.getByText('Step 2 of 5: Licence')).toBeVisible();
    await page.getByRole('combobox', { name: 'Medical council' }).click();
    await page.getByRole('option', { name: /TNMC|Tamil Nadu Medical Council/ }).click();
    await page.getByLabel('Registration number').fill('51327');
    await page.getByLabel('Registration category').fill('Permanent');
    await page.getByLabel('Expiry date').fill('2031-12-31');
    await page.getByRole('button', { name: 'Next' }).click();
    await expect(page.getByText('Step 2 of 5: Licence')).toBeVisible(); // ADR-016: not without the owner's attestation
    await page.getByLabel(/I confirm this licence belongs to this doctor/).check();
    await expectNoAxeViolations(page);
    await page.getByRole('button', { name: 'Next' }).click();
    await expect(page.getByText('Step 3 of 5: Schedule')).toBeVisible();
    await page.getByRole('button', { name: 'Next' }).click();
    await expect(page.getByText('Step 4 of 5: Default fees')).toBeVisible();
    await page.getByRole('button', { name: 'Next' }).click();
    await expect(page.getByText('Step 5 of 5: Access')).toBeVisible();
    await page.getByRole('button', { name: 'Add doctor and send invitation' }).click();
    await expect(page.getByText(`${doctor.name} was added and the invitation was sent.`, { exact: false })).toBeVisible();
    const card = page.getByRole('listitem').filter({ has: page.getByRole('heading', { name: doctor.name }) });
    await expect(card).toContainText(/Attested by .+ on \d{1,2} \w{3} \d{4}/); // the owner's attestation, no Medvya check (ADR-016)
    await expectNoAxeViolations(page);
    expect((await mailTo(page.request, doctor.email, /./)).text).toContain(`Menon Clinic ${n} (synthetic)`);
  });
  await arabicAxeClean(page, '/app/settings?tab=doctors');

  const reception = `reception${n}@example.test`;
  const desk = await browser.newPage();
  await test.step('J1.10 the receptionist opens the emailed invitation, signs in and joins; the owner sees them Active (MDV-P0-013)', async () => {
    // The link exactly as the email gives it (the token in the path or in the fragment): nothing here builds it.
    const link = (await mailTo(page.request, reception, /./)).text.match(/https?:\/\/\S+\/invite\S*/)?.[0];
    expect(link, 'the invitation email holds a link').toBeTruthy();
    const arabic = await browser.newPage();
    await arabic.addInitScript(() => localStorage.setItem('medvya.locale', 'ar'));
    await arabic.addInitScript({ path: axePath });
    await arabic.goto(link!); // the preview does not use the invitation up
    await expect(arabic.locator('html')).toHaveAttribute('dir', 'rtl');
    await expect(arabic.getByRole('button', { name: 'سجّل الدخول وانضم' })).toBeVisible();
    await expectNoAxeViolations(arabic);
    await arabic.close();

    const deskUrls = watchUrls(desk);
    await desk.addInitScript({ path: axePath });
    await desk.clock.setFixedTime(browserNow());
    await desk.goto(link!);
    await expect(desk.getByRole('heading', { level: 1 })).toHaveText(`Join Menon Clinic ${n} (synthetic)`);
    await expect(desk.getByText('invited you as Receptionist.', { exact: false })).toBeVisible();
    await expectNoAxeViolations(desk);
    await desk.getByRole('button', { name: 'Sign in & join' }).click();
    await desk.getByLabel('Invited email').fill(reception); // the development provider: a new person who reports the invited address
    await desk.getByRole('button', { name: 'Join as a new synthetic person with this email' }).click();
    await expect(desk.getByRole('navigation').first()).toBeVisible();
    await expect(desk.getByRole('link', { name: 'Front desk' }).first()).toBeVisible();
    expect((await (await desk.request.get('/v1/me/context')).json()).active.role).toBe('Receptionist');
    // The invited row became the joined person (the list shows an address only while a member is still invited).
    const staff = await (await page.request.get('/v1/staff')).json() as { email: string | null; role: string; status: string; name: string }[];
    expect(staff.filter(m => m.email === reception)).toEqual([]);
    expect(staff).toContainEqual(expect.objectContaining({ role: 'Receptionist', status: 'Active', mfa: 'enrolled' }));
    expect(deskUrls.leaks(['Latha', 'Iyengar', patient.mobile])).toEqual([]); // patient data; the staff address rides only in the development provider's own form
  });

  await test.step('J1.11 the booking confirmation reaches the patient by email, the channel she said yes to, with no clinical details (MDV-P1-032)', async () => {
    const { subject, text } = await mailTo(page.request, patient.email, /Appointment confirmed/);
    expect(subject).toContain('Appointment confirmed');
    expect(text).toContain(`Menon Clinic ${n} (synthetic)`);
    for (const detail of [patient.name, patient.dob, 'Long-term condition review']) expect(text).not.toContain(detail);
  });

  await test.step('J1.12 the owner offboards the receptionist with a reason and the identity step-up; their access ends at once (MDV-P1-009, MDV-P1-107)', async () => {
    const name = ((await (await page.request.get('/v1/staff')).json()) as { role: string; status: string; name: string }[])
      .find(m => m.role === 'Receptionist' && m.status === 'Active')!.name; // the one person who joined in J1.10
    const offboard = async () => {
      await page.getByRole('button', { name: `Actions for ${name}` }).click();
      await page.getByRole('menuitem', { name: `Offboard ${name}` }).click();
      await page.getByLabel('Reason (kept in the audit trail)').fill('Left the practice (synthetic)');
      await page.getByRole('button', { name: 'Offboard and sign out everywhere' }).click();
    };
    await page.goto('/app/settings?tab=staff');
    await offboard();
    // Any offboarding needs a fresh identity check; the server refused and nothing changed.
    await expect(page.getByText('Confirm your identity first, then press Offboard again. Nothing has changed.')).toBeVisible();
    await expectNoAxeViolations(page);
    await page.getByRole('button', { name: 'Confirm your identity' }).click();
    // The development provider has no password: picking the same person again is the step-up (Entra's prompt=login and MFA are not exercised).
    await page.getByRole('link', { name: owner }).click();
    await page.waitForURL(/\/app\/settings\?tab=staff$/);
    await offboard();
    await expect(page.getByText('Offboarded. Their access has ended and their sessions are signed out.')).toBeVisible();
    expect((await desk.request.get('/v1/me/context')).status()).toBe(401); // their session is over on the server, not only hidden
    await desk.close();
  });
  await arabicAxeClean(page, '/app/settings?tab=staff');

  expect(watch.leaks(['Latha', 'Iyengar', 'Gopal', patient.dob, patient.email, `0${n}`])).toEqual([]);
});
