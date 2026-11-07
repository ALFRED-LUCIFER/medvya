import { createRequire } from 'node:module';
import { expect, test } from '@playwright/test';
import { browserNow, expectNoAxeViolations, watchUrls } from './support';

// MDV-P0-029: journey J1 (first solo-doctor visit, docs/02-design/journeys.md) through the real stack: a brand-new person signs up at the
// development identity provider, sets up a practice, then registers a patient, books and checks in. Step 2 (fees catalogue) and steps 5-8
// (consent, recording, note, invoice) have no screens yet and are not faked here. Synthetic people only.
const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
const n = Math.floor(Math.random() * 9000) + 1000;
const patient = {
  name: `Latha Iyengar${[...String(n)].map(d => 'abcdefghij'[+d]).join('')}`, dob: `${1950 + (n % 50)}-${String(1 + (n % 12)).padStart(2, '0')}-${String(1 + (n % 28)).padStart(2, '0')}`,
  mobile: `+91 90000 0${n}`, emergency: 'Gopal Iyengar', emergencyMobile: `+91 90000 1${n}`,
};

test('J1: a new solo doctor sets up the practice, registers a patient, books and checks in', async ({ page }) => {
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
    await page.getByLabel('General practice').check();
    await page.getByRole('combobox', { name: 'Medical council' }).click();
    await page.getByRole('option', { name: /TNMC/ }).click();
    await page.getByLabel('Registration number').fill('84213');
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
    await page.getByRole('button', { name: 'Open Today' }).click();
    await expect(page.getByText('Verification pending · usually within 1 working day.')).toBeVisible();
    await page.reload(); // the server, not the screen, holds the practice
    await expect(page.getByRole('navigation').first()).toBeVisible();
    await expect(page).not.toHaveURL(/onboarding/);
    // The owner of a solo practice administers it (Staff, roles, integrations = Config): the staff list opens before the licence is verified.
    const staff = await page.request.get('/v1/staff');
    expect(staff.status(), await staff.text()).toBe(200);
    // shortcut: the stack has no SMTP sink (that is `pnpm services:up`), so delivery is not asserted here; the integration tests check the email.
    expect(await staff.json()).toContainEqual(expect.objectContaining({ email: `reception${n}@example.test`, status: 'Invited' }));
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

  expect(watch.leaks(['Latha', 'Iyengar', 'Gopal', patient.dob, `0${n}`])).toEqual([]);
});
