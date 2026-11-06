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
    await page.getByRole('button', { name: 'Create account' }).click();
    await page.getByRole('link', { name: /New synthetic person/ }).click();
    await page.waitForURL(/\/app\/onboarding/);
    await page.getByRole('button', { name: 'Start setup' }).click();
    await page.getByLabel('Full name').fill('Arjun Menon (synthetic)');
    await page.getByLabel('Specialty').fill('General medicine');
    await page.getByRole('button', { name: 'Continue' }).click();
    await page.getByRole('combobox', { name: 'Medical council' }).click();
    await page.getByRole('option', { name: /TNMC/ }).click();
    await page.getByLabel('Registration number').fill('84213');
    await page.getByRole('button', { name: 'Continue' }).click();
    await page.getByLabel('Clinic name').fill(`Menon Clinic ${n} (synthetic)`);
    await page.getByLabel('Address').fill('12 Test Street, Chennai');
    await page.getByRole('button', { name: 'Continue' }).click();
    await page.getByRole('button', { name: 'Continue' }).click(); // hours and fees: the design defaults
    await page.getByLabel('General OPD').check();
    await page.getByRole('button', { name: 'Continue' }).click();
    await page.getByLabel('Receptionist email').fill(`reception${n}@example.test`);
    await page.waitForLoadState('networkidle'); // the draft autosave must settle before axe reads the page
    await expectNoAxeViolations(page);
    const finished = page.waitForResponse(r => new URL(r.url()).pathname === '/v1/onboarding/finish');
    await page.getByRole('button', { name: 'Finish · go to Today' }).click();
    expect(await (await finished).json()).toMatchObject({ role: 'SoloDoctor' });
    // shortcut: the stack has no SMTP sink (that is `pnpm services:up`), so the invitation mail fails and the screen says so; add a sink to assert delivery.
    await expect(page.getByText('1 invitation(s) could not be sent. You can send them again from Staff.')).toBeVisible();
    await page.getByRole('button', { name: 'Finish · go to Today' }).click();
    await expect(page.getByText('Verification pending · usually within 1 working day.')).toBeVisible();
    await page.reload(); // the server, not the screen, holds the practice
    await expect(page.getByRole('navigation').first()).toBeVisible();
    await expect(page).not.toHaveURL(/onboarding/);
    // The owner of a solo practice administers it (Staff, roles, integrations = Config): the staff list opens before the licence is verified.
    const staff = await page.request.get('/v1/staff');
    expect(staff.status(), await staff.text()).toBe(200);
  });

  await test.step('J1.3 register the first patient with the India minimum, ABHA left empty', async () => {
    await page.goto('/app/patients/new');
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

  await test.step('J1.4 book the first free slot and check in on the board', async () => {
    await page.getByRole('link', { name: 'Schedule' }).first().click();
    const slots = page.getByRole('list', { name: /Arjun Menon/ });
    await expect(slots).toBeVisible();
    await slots.getByRole('button', { name: /^Book / }).first().click();
    const panel = page.getByRole('complementary', { name: 'Book appointment' });
    await panel.getByRole('searchbox', { name: 'Name, MRN, phone or ID' }).fill(patient.mobile);
    await panel.getByRole('button', { name: 'Search' }).click();
    await panel.getByRole('button', { name: /^L I, \d+ y, Female/ }).click();
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

  expect(watch.leaks(['Latha', 'Iyengar', 'Gopal', patient.dob, `0${n}`])).toEqual([]);
});
