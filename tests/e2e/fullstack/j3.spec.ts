import { expect } from '@playwright/test';
import { journey } from '../journeys/storyboard';
import { expectArabicAxeClean, expectNoAxeViolations, signIn, syntheticPatient, walkIn, watchUrls } from './support';

// MDV-P0-023: journey J3 (busy reception → nurse handoff, docs/02-design/journeys.md) through the real stack, one test per storyboard step.
// Each step makes its own synthetic walk-in, so a step runs alone. The register → book → check-in → cancel story is front-desk.spec.ts.
const joseph = /A\. Joseph/;
const owner = /K\. Lakshmi/; // the practice admin: only an owner switches nurse intake on
const nurseUser = /S\. Thomas/;
const rao = /Dr\. Arjun Rao/;
const board = '/app/front-desk';

journey('J3', {
  // MDV-P1-025 / MDV-P1-052: a walk-in is checked in from the board; the desk reads the token on the success screen; the card waits tagged Walk-in.
  1: async ({ page, browser }) => {
    const watch = watchUrls(page);
    const patient = syntheticPatient('Anjali', 'Varma');
    await signIn(page, joseph);
    await walkIn(page, patient, rao);
    await expect(page.getByText(/^Token /)).toContainText(/[A-Z]-\d+/);
    await expectNoAxeViolations(page);
    await page.getByRole('link', { name: 'Back to the board' }).first().click();
    const card = page.getByRole('region', { name: /^Checked in/ }).getByRole('article', { name: patient.name });
    await expect(card.getByText('Walk-in', { exact: true })).toBeVisible();
    await expect(card.getByText(/^Token [A-Z]-\d+$/)).toBeVisible();
    await expect(card.getByText('Registration incomplete')).toBeVisible(); // quick-registered at the desk (J3.2)
    await expectArabicAxeClean(browser, joseph, '/app/front-desk/walk-in');
    expect(watch.leaks([patient.name, patient.dob, patient.mobile, patient.mobile.replace(/\D/g, '')])).toEqual([]);
  },
  // MDV-P1-015: quick registration takes five fields; the record stays "Incomplete registration", with the completion one click away.
  2: async ({ page }) => {
    const patient = syntheticPatient('Lakshmi', 'Rajan');
    await signIn(page, joseph);
    await page.goto('/app/patients/new?mode=quick');
    await page.getByLabel('Full name').fill(patient.name);
    await page.getByLabel('Mobile').fill(patient.mobile);
    await page.getByRole('textbox', { name: 'Date of birth', exact: true }).fill(patient.dob);
    await page.getByRole('radio', { name: 'Female', exact: true }).check();
    await page.getByRole('button', { name: 'Register patient' }).click();
    await expect(page.getByRole('heading', { name: `${patient.name} is registered` })).toBeVisible();
    await expect(page.getByText('Attention · quick registration.')).toBeVisible();
    await page.getByRole('link', { name: 'Open record' }).click();
    await expect(page.getByText('Incomplete registration').first()).toBeVisible();
    await expect(page.getByRole('link', { name: 'Complete registration', exact: true })).toBeVisible();
    await expectNoAxeViolations(page);
  },
  // MDV-P1-016 / MDV-P1-017: the same name and date of birth as an existing record; the desk asks for a merge review instead of guessing.
  3: async ({ page }) => {
    await signIn(page, joseph);
    await page.goto('/app/patients/new?mode=quick');
    await page.getByLabel('Full name').fill('Deepak Nair (synthetic)'); // seeded: born 20 May 1979
    await page.getByLabel('Mobile').fill('+91 90000 00102');
    await page.getByRole('textbox', { name: 'Date of birth', exact: true }).fill('1979-05-20');
    await page.getByRole('radio', { name: 'Male', exact: true }).check();
    const review = page.getByRole('region', { name: 'Attention · possible existing record' });
    await expect(review).toContainText('Matched on name + date of birth.');
    await expect(page.getByRole('button', { name: 'Resolve duplicate to save' })).toBeDisabled();
    await expectNoAxeViolations(page);
    await review.getByRole('button', { name: 'Request merge review (P-03)' }).click();
    const saved = page.waitForResponse(r => r.request().method() === 'POST' && new URL(r.url()).pathname === '/v1/patients');
    await page.getByRole('button', { name: 'Register patient' }).click();
    expect((await saved).status()).toBeLessThan(300);
    await expect(page.getByRole('heading', { name: 'Deepak Nair (synthetic) is registered' })).toBeVisible();
  },
  // MDV-P1-028: reception sends a checked-in patient to the nurse; the card moves to "With nurse" and stays there after a reload.
  4: async ({ page }) => {
    const patient = syntheticPatient('Meenakshi', 'Sundaram');
    await signIn(page, joseph);
    await walkIn(page, patient, rao);
    await page.goto(board);
    await page.getByRole('article', { name: patient.name }).getByRole('button', { name: 'Send to nurse' }).click();
    await expect(page.getByRole('region', { name: /^With nurse/ }).getByRole('article', { name: patient.name })).toBeVisible();
    await page.reload();
    await expect(page.getByRole('region', { name: /^With nurse/ }).getByRole('article', { name: patient.name })).toBeVisible();
  },
  // MDV-P2-007 / MDV-P2-008: the owner switches nurse intake on (audited), the nurse opens the intake from the With nurse card, records a vital,
  // confirms "no known allergies" on purpose and hands off; the card is Ready for doctor. The flag is put back, so step 6's board move still works.
  5: async ({ page, browser }) => {
    const patient = syntheticPatient('Kavitha', 'Menon');
    const switchIntake = async (enabled: boolean) => {
      const admin = await browser.newPage();
      try {
        await signIn(admin, owner);
        const r = await admin.request.put('/v1/settings/nurse-intake', { data: { enabled }, headers: { 'X-Medvya-Csrf': '1' } });
        expect(r.status()).toBe(200);
      } finally {
        await admin.close();
      }
    };
    await switchIntake(true);
    try {
      await signIn(page, joseph);
      await walkIn(page, patient, rao);
      await page.goto(board);
      await page.getByRole('article', { name: patient.name }).getByRole('button', { name: 'Send to nurse' }).click();
      await expect(page.getByRole('region', { name: /^With nurse/ }).getByRole('article', { name: patient.name })).toBeVisible();

      const nurse = await browser.newPage();
      await signIn(nurse, nurseUser); // after the switch: the screens learn of it from the session
      await nurse.goto(board);
      await nurse.getByRole('article', { name: patient.name }).getByRole('link', { name: `Open intake for ${patient.name}` }).click();
      await nurse.getByRole('textbox', { name: 'Pulse' }).fill('72');
      await nurse.getByRole('button', { name: 'Save vitals' }).click();
      await expect(nurse.getByRole('button', { name: 'Save vitals' })).toBeEnabled(); // the save has finished
      await expect(nurse.getByRole('button', { name: 'Hand off · Ready for doctor' })).toBeDisabled(); // allergies not confirmed yet: never assumed
      await expectNoAxeViolations(nurse);

      // MDV-P2-002: the nurse escalates; the treating doctor's open Today shows it within 2 seconds, without a reload (the board hub push).
      const doctor = await browser.newPage();
      await signIn(doctor, rao);
      await doctor.goto('/app/today');
      await doctor.getByRole('button', { name: 'My patients' }).click();
      const visit = doctor.getByRole('region', { name: "Today's visits" }).getByRole('article', { name: new RegExp(patient.name) });
      await expect(visit).toBeVisible();
      await expect(doctor.getByRole('status').filter({ hasText: /^Live updates$/ })).toBeVisible(); // joined the hub before the push
      await nurse.getByRole('button', { name: 'Escalate to doctor' }).click();
      await nurse.getByRole('textbox', { name: 'Reason for the doctor' }).fill('Synthetic chest pain on arrival');
      await nurse.getByRole('button', { name: 'Send urgent item' }).click();
      await expect(visit).toContainText('Nurse escalation: Synthetic chest pain on arrival', { timeout: 2_000 });
      await expectNoAxeViolations(doctor);
      await doctor.close();

      await nurse.getByRole('button', { name: 'Confirm: no known allergies' }).click();
      await nurse.getByRole('button', { name: 'Hand off · Ready for doctor' }).click();
      await expect(nurse).toHaveURL(/\/nurse$/);
      await expect(nurse.getByRole('heading', { name: 'Nurse intake queue' })).toBeVisible(); // the shared database holds other patients with the nurse
      await expect(nurse.getByRole('article', { name: patient.name })).toHaveCount(0); // this one has left the queue
      await nurse.close();

      await page.goto(board);
      await expect(page.getByRole('region', { name: /^Ready for doctor/ }).getByRole('article', { name: patient.name })).toBeVisible();
      await expectArabicAxeClean(browser, nurseUser, '/app/nurse');
    } finally {
      await switchIntake(false);
    }
  },
  // MDV-P1-028 / MDV-P2-001: the nurse marks intake done; the card is Ready for doctor and the doctor can start the consultation from it.
  // shortcut: the doctor sees it on the shared board, not on Today; Doctor Today (D-01) is MDV-P2-002.
  6: async ({ page, browser }) => {
    const patient = syntheticPatient('Revathi', 'Krishnan');
    await signIn(page, joseph);
    await walkIn(page, patient, rao);
    await page.goto(board);
    await page.getByRole('article', { name: patient.name }).getByRole('button', { name: 'Send to nurse' }).click();
    await expect(page.getByRole('region', { name: /^With nurse/ }).getByRole('article', { name: patient.name })).toBeVisible();

    const nurse = await browser.newPage();
    await signIn(nurse, /S\. Thomas/);
    await nurse.goto(board);
    await nurse.getByRole('region', { name: /^With nurse/ }).getByRole('article', { name: patient.name }).getByRole('button', { name: 'Intake done' }).click();
    await expect(nurse.getByRole('region', { name: /^Ready for doctor/ }).getByRole('article', { name: patient.name })).toBeVisible();
    await expectNoAxeViolations(nurse);
    await nurse.close();

    const doctor = await browser.newPage();
    await signIn(doctor, rao);
    await doctor.goto(board);
    const card = doctor.getByRole('region', { name: /^Ready for doctor/ }).getByRole('article', { name: patient.name });
    await expect(card.getByRole('button', { name: `Start consultation with ${patient.name}` })).toBeVisible();
    await doctor.close();
  },
  // MDV-P1-030: the clinic overview shows the shared waiting room; the desk moves a waiting patient to another doctor with a reason.
  7: async ({ page, browser }) => {
    const patient = syntheticPatient('Sowmya', 'Raghavan');
    await signIn(page, joseph);
    await walkIn(page, patient, rao);
    await page.goto(board);
    await page.getByRole('link', { name: 'Clinic overview' }).click();
    await expect(page.getByRole('heading', { level: 1, name: 'Clinic overview' })).toBeVisible();
    const waiting = page.getByRole('region', { name: /^Waiting room/ }).getByRole('listitem').filter({ hasText: patient.name });
    await expect(waiting).toContainText(/With Dr\. Arjun Rao/);
    await expectNoAxeViolations(page);
    await waiting.getByRole('button', { name: 'Move to another doctor' }).click();
    const move = page.getByRole('form', { name: `Move ${patient.name}` });
    await move.getByRole('combobox', { name: 'Move to' }).click();
    await page.getByRole('option', { name: /Dr\. S\. Menon/ }).click();
    await move.getByRole('combobox', { name: 'Reason' }).click();
    await page.getByRole('option', { name: 'Balance the queues' }).click();
    await move.getByRole('button', { name: 'Move patient' }).click();
    await expect(waiting).toContainText(/With Dr\. S\. Menon/);
    await page.reload(); // the server, not the screen, holds the move
    await expect(waiting).toContainText(/With Dr\. S\. Menon/);
    await expectArabicAxeClean(browser, joseph, '/app/front-desk/overview');
  },
});
