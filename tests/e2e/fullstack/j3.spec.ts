import { expect } from '@playwright/test';
import { journey } from '../journeys/storyboard';
import { expectArabicAxeClean, expectNoAxeViolations, setNurseIntake, signIn, syntheticPatient, walkIn, watchUrls } from './support';

// MDV-P0-023: journey J3 (busy reception → nurse handoff, docs/02-design/journeys.md) through the real stack, one test per storyboard step.
// Each step makes its own synthetic walk-in, so a step runs alone. The register → book → check-in → cancel story is front-desk.spec.ts.
const joseph = /A\. Joseph/;
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
    await page.getByRole('link', { name: 'Back to Front desk' }).first().click();
    const card = page.getByRole('region', { name: /^Ready for doctor/ }).getByRole('article', { name: patient.name }); // nurse intake is off (the default): check-in goes straight to Ready for doctor (#1104)
    await expect(card.getByText('Walk-in', { exact: true })).toBeVisible();
    // The token and how many wait ahead in Dr. Rao's own queue, in words (MDV-P1-103, decision 10).
    await expect(card.getByText(/^Token [A-Z]-\d+ · (Next in line|\d+ ahead)$/)).toBeVisible();
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
    await page.getByRole('textbox', { name: 'Date of birth (required)', exact: true }).fill(patient.dob);
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
    await page.getByRole('textbox', { name: 'Date of birth (required)', exact: true }).fill('1979-05-20');
    await page.getByRole('radio', { name: 'Male', exact: true }).check();
    const review = page.getByRole('region', { name: 'Attention · possible existing record' });
    await expect(review).toContainText('Matched on name + date of birth.');
    await expect(page.getByRole('button', { name: 'Resolve duplicate to save' })).toBeDisabled();
    await expectNoAxeViolations(page);
    await review.getByRole('button', { name: 'Request merge review' }).click();
    const saved = page.waitForResponse(r => r.request().method() === 'POST' && new URL(r.url()).pathname === '/v1/patients');
    await page.getByRole('button', { name: 'Register patient' }).click();
    expect((await saved).status()).toBeLessThan(300);
    await expect(page.getByRole('heading', { name: 'Deepak Nair (synthetic) is registered' })).toBeVisible();
  },
  // MDV-P1-028: with nurse intake on, reception sends a checked-in patient to the nurse; the card moves to "With nurse" and stays there after a reload.
  4: async ({ page, browser }) => {
    const patient = syntheticPatient('Meenakshi', 'Sundaram');
    await setNurseIntake(browser, true);
    try {
      await signIn(page, joseph); // after the switch: the screens learn of it from the session
      await walkIn(page, patient, rao);
      await page.goto(board);
      await page.getByRole('article', { name: patient.name }).getByRole('button', { name: 'Send to nurse' }).click();
      await expect(page.getByRole('region', { name: /^With nurse/ }).getByRole('article', { name: patient.name })).toBeVisible();
      await page.reload();
      await expect(page.getByRole('region', { name: /^With nurse/ }).getByRole('article', { name: patient.name })).toBeVisible();
    } finally {
      await setNurseIntake(browser, false);
    }
  },
  // MDV-P2-007 / MDV-P2-008: the owner switches nurse intake on in Settings (audited), the nurse opens the intake from the With nurse card, records a vital,
  // confirms "no known allergies" on purpose and hands off; the card is Ready for doctor. The flag is put back for step 6, a clinic without the nurse step.
  5: async ({ page, browser }) => {
    const patient = syntheticPatient('Kavitha', 'Menon');
    const complaint = 'Fever and sore throat for three days (synthetic)';
    await setNurseIntake(browser, true);
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
      await nurse.getByRole('textbox', { name: "Chief complaint, in the patient's words" }).fill(complaint);
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
      const visit = doctor.getByRole('region', { name: "Today's visits" }).getByRole('listitem').filter({ hasText: patient.name });
      await expect(visit).toBeVisible();
      await expect(doctor.getByRole('status').filter({ hasText: /^Live updates$/ })).toBeVisible(); // joined the hub before the push
      await nurse.getByRole('button', { name: 'Escalate to doctor' }).click();
      await nurse.getByRole('textbox', { name: 'Reason for the doctor' }).fill('Synthetic chest pain on arrival');
      await nurse.getByRole('button', { name: 'Send urgent item' }).click();
      await expect(visit).toContainText('Nurse escalation: Synthetic chest pain on arrival', { timeout: 2_000 });
      // MDV-P1-103: the escalated patient is pinned above Next (or Current) patient, with the reason in words.
      const pinned = doctor.getByRole('region', { name: 'Escalated by the nurse' });
      await expect(pinned.getByRole('listitem').filter({ hasText: patient.name })).toContainText('Nurse escalation: Synthetic chest pain on arrival');
      const next = doctor.getByRole('region', { name: /^(Next|Current) patient$/ });
      if (await next.count()) expect((await pinned.boundingBox())!.y).toBeLessThan((await next.boundingBox())!.y);
      await expectNoAxeViolations(doctor);
      await doctor.close();

      await nurse.getByRole('button', { name: 'Confirm: no known allergies' }).click();
      await nurse.getByRole('button', { name: 'Hand off · Ready for doctor' }).click();
      await expect(nurse).toHaveURL(/\/nurse$/);
      await expect(nurse.getByRole('heading', { name: 'Nurse queue' })).toBeVisible(); // the shared database holds other patients with the nurse
      await expect(nurse.getByRole('article', { name: patient.name })).toHaveCount(0); // this one has left the queue
      await nurse.close();

      await page.goto(board);
      await expect(page.getByRole('region', { name: /^Ready for doctor/ }).getByRole('article', { name: patient.name })).toBeVisible();
      await expectArabicAxeClean(browser, nurseUser, '/app/nurse');

      // MDV-P1-104: the doctor opens the consultation and Chief complaint holds the nurse's words, marked from nurse intake; nobody types it twice.
      const consult = await browser.newPage();
      await signIn(consult, rao);
      await consult.goto(board);
      await consult.getByRole('button', { name: `Start consultation with ${patient.name}` }).click();
      const leaveOpen = consult.getByRole('button', { name: 'Leave it open and start this one' }); // the shared database may hold another open consultation of Dr. Rao's
      await expect.poll(async () => /\/app\/consult\//.test(consult.url()) || await leaveOpen.isVisible()).toBe(true);
      if (await leaveOpen.isVisible()) await leaveOpen.click();
      const field = consult.getByRole('region', { name: 'Consultation note' }).getByRole('textbox', { name: /^Chief complaint/ });
      await expect(field).toHaveValue(complaint);
      await expect(field).toHaveAccessibleDescription(/^From nurse intake/);
      await expectNoAxeViolations(consult);
      await consult.close();
    } finally {
      await setNurseIntake(browser, false);
    }
  },
  // MDV-P1-028 / MDV-P2-001 / MDV audit UX-05: in a clinic without the nurse step (the shared database may still hold earlier steps' With nurse visits), check-in puts the patient
  // Ready for doctor and the doctor can start the consultation from the card.
  // shortcut: the doctor sees it on the shared board, not on Today; Doctor Today (D-01) is MDV-P2-002.
  6: async ({ page, browser }) => {
    const patient = syntheticPatient('Revathi', 'Krishnan');
    await signIn(page, joseph);
    await walkIn(page, patient, rao);
    await page.goto(board); // no nurse step: the walk-in is already Ready for doctor (#1104)
    await expect(page.getByRole('region', { name: /^Ready for doctor/ }).getByRole('article', { name: patient.name })).toBeVisible();
    await expectNoAxeViolations(page);

    const doctor = await browser.newPage();
    await signIn(doctor, rao);
    await doctor.goto(board);
    const card = doctor.getByRole('region', { name: /^Ready for doctor/ }).getByRole('article', { name: patient.name });
    await expect(card.getByRole('button', { name: `Start consultation with ${patient.name}` })).toBeVisible();
    await doctor.close();
  },
  // MDV-P1-030: the board's "By doctor" view shows the shared waiting room; the desk moves a waiting patient to another doctor with a reason.
  7: async ({ page, browser }) => {
    const patient = syntheticPatient('Sowmya', 'Raghavan');
    await signIn(page, joseph);
    await walkIn(page, patient, rao);
    await page.goto(board);
    await page.getByRole('group', { name: 'Front desk views' }).getByRole('button', { name: 'By doctor' }).click();
    await expect(page).toHaveURL(/\/front-desk\/overview$/);
    const waiting = page.getByRole('region', { name: /^Waiting room/ }).getByRole('row').filter({ hasText: patient.name });
    await expect(waiting).toContainText(/Dr\. Arjun Rao/);
    await expectNoAxeViolations(page);
    await waiting.getByRole('button', { name: `Move to another doctor: ${patient.name}` }).click();
    const move = page.getByRole('form', { name: `Move ${patient.name}` });
    await move.getByRole('combobox', { name: 'Move to' }).click();
    await page.getByRole('option', { name: /Dr\. S\. Menon/ }).click();
    await move.getByRole('combobox', { name: 'Reason' }).click();
    await page.getByRole('option', { name: 'Balance the queues' }).click();
    await move.getByRole('button', { name: 'Move patient' }).click();
    await expect(waiting).toContainText(/Dr\. S\. Menon/);
    await page.reload(); // the server, not the screen, holds the move
    await expect(waiting).toContainText(/Dr\. S\. Menon/);
    await expectArabicAxeClean(browser, joseph, '/app/front-desk/overview');
  },
  // MDV-P1-100 (F-05): a wrong Call in is undone from the status line within 30 seconds; the card is back with the same token.
  10: async ({ page }) => {
    const patient = syntheticPatient('Nandini', 'Shankar');
    await signIn(page, joseph);
    await walkIn(page, patient, rao);
    await page.goto(board);
    const ready = page.getByRole('region', { name: /^Ready for doctor/ }).getByRole('article', { name: patient.name });
    const token = (await ready.getByText(/^Token [A-Z]-\d+/).textContent())!.match(/[A-Z]-\d+/)![0]; // the tag also says how many are ahead
    await ready.getByRole('button', { name: 'Call in' }).click();
    await expect(page.getByRole('region', { name: /^In consult/ }).getByRole('article', { name: patient.name })).toBeVisible();
    await page.getByRole('button', { name: 'Undo' }).click();
    await expect(ready).toContainText(`Token ${token}`);
    await expect(page.getByRole('status').filter({ hasText: 'Undone' })).toContainText(`${patient.name} is back in Ready for doctor`);
    await page.reload(); // the server holds the undo
    await expect(ready).toBeVisible();
    await expectNoAxeViolations(page);
  },
  // MDV-P1-100: a walk-in who leaves is marked from the card menu with a reason; the card leaves the board and the shared waiting room.
  11: async ({ page, browser }) => {
    const patient = syntheticPatient('Kavitha', 'Murali');
    await signIn(page, joseph);
    await walkIn(page, patient, rao);
    await page.goto(board);
    await page.getByRole('button', { name: `More for ${patient.name}` }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Left without being seen' }).click();
    const form = page.getByRole('dialog', { name: `${patient.name} left without being seen` });
    await form.getByRole('combobox', { name: 'Reason' }).click();
    await page.getByRole('option', { name: 'Patient asked to cancel' }).click();
    await expectNoAxeViolations(page);
    await form.getByRole('button', { name: 'Mark as left' }).click();
    await expect(page.getByRole('article', { name: patient.name })).toHaveCount(0);
    await expect(page.getByText(/^Left without being seen today: \d+$/)).toBeVisible();
    await page.goto('/app/front-desk/overview');
    await expect(page.getByRole('region', { name: /^Waiting room/ }).getByRole('row').filter({ hasText: patient.name })).toHaveCount(0);
    await expectArabicAxeClean(browser, joseph, board);
  },
  // MDV-P1-100: with the doctor's consultation open the desk card reads "With the doctor"; a direct close is refused with 409.
  12: async ({ page, browser }) => {
    const patient = syntheticPatient('Geetha', 'Balan');
    await signIn(page, joseph);
    await walkIn(page, patient, rao);
    const doctor = await browser.newPage();
    await signIn(doctor, rao);
    await doctor.goto(board);
    await doctor.getByRole('button', { name: `Start consultation with ${patient.name}` }).click();
    // The shared database may hold another open consultation of Dr. Rao's (one open per doctor): keep it open and start this one.
    const leaveOpen = doctor.getByRole('button', { name: 'Leave it open and start this one' });
    const opened = async () => /\/app\/consult\//.test(doctor.url()) || await leaveOpen.isVisible();
    await expect.poll(opened).toBe(true);
    if (await leaveOpen.isVisible()) await leaveOpen.click();
    await expect(doctor).toHaveURL(/\/app\/consult\/[0-9a-f-]{36}$/);

    await page.goto(board);
    const card = page.getByRole('region', { name: /^In consult/ }).getByRole('article', { name: patient.name });
    await expect(card.getByText('With the doctor')).toBeVisible();
    await expect(card.getByRole('button')).toHaveCount(0);
    const id = (await card.getAttribute('aria-labelledby'))!.replace('board-card-', '');
    const close = await page.request.post(`/v1/appointments/${id}/status`, { headers: { 'X-Medvya-Csrf': '1' }, data: { state: 'Done' } });
    expect(close.status()).toBe(409);
    expect((await close.json()).code).toBe('with_doctor');
    await expectNoAxeViolations(page);
    await doctor.close();
  },
  // MDV-P1-102 (F-06): the doctor sets a follow-up of 2 weeks and ends the visit; the desk sees "Follow-up due" with the date and the doctor on the Done
  // card and the desk record, books it from Book (the schedule on that day, patient and doctor chosen) and the line is gone.
  13: async ({ page, browser }) => {
    const watch = watchUrls(page);
    const patient = syntheticPatient('Revathi', 'Kannan');
    await signIn(page, joseph);
    await walkIn(page, patient, rao);
    const doctor = await browser.newPage();
    await signIn(doctor, rao);
    await doctor.goto(board);
    await doctor.getByRole('button', { name: `Start consultation with ${patient.name}` }).click();
    const leaveOpen = doctor.getByRole('button', { name: 'Leave it open and start this one' });
    const opened = async () => /\/app\/consult\//.test(doctor.url()) || await leaveOpen.isVisible();
    await expect.poll(opened).toBe(true);
    if (await leaveOpen.isVisible()) await leaveOpen.click();
    await expect(doctor).toHaveURL(/\/app\/consult\/[0-9a-f-]{36}$/);
    const fu = doctor.getByRole('group', { name: 'Follow-up' });
    await fu.getByRole('button', { name: '2 weeks' }).click();
    await expect(fu.getByRole('button', { name: '2 weeks' })).toHaveAttribute('aria-pressed', 'true');
    await doctor.getByRole('button', { name: 'End consultation' }).click();
    await expect(doctor.getByRole('status').filter({ hasText: 'Consultation ended' })).toBeVisible();
    await doctor.close();

    await page.goto(board);
    const line = page.getByRole('region', { name: /^Done/ }).getByRole('article', { name: patient.name }).getByRole('group', { name: /^Follow-up due from/ });
    await expect(line).toContainText('Dr. Arjun Rao');
    await expectNoAxeViolations(page);
    const book = line.getByRole('link', { name: `Book the follow-up for ${patient.name}` });
    const href = new URL((await book.getAttribute('href'))!, 'http://x');
    const patientId = href.searchParams.get('patient')!;

    // The desk record shows the same line, in English and Arabic, axe clean.
    await page.goto(`/app/patients/directory/${patientId}`);
    await expect(page.getByRole('group', { name: /^Follow-up due from/ })).toContainText('Dr. Arjun Rao');
    await expectNoAxeViolations(page);
    await expectArabicAxeClean(browser, joseph, `/app/patients/directory/${patientId}`);

    await page.goto(board);
    await book.click();
    await expect(page).toHaveURL(new RegExp(`/app/schedule\\?date=${href.searchParams.get('date')}&view=day&patient=${patientId}&doctor=`));
    await page.getByRole('list', { name: /Dr\. Arjun Rao/ }).getByRole('button', { name: /^Book / }).first().click();
    const booked = page.waitForResponse(r => r.request().method() === 'POST' && new URL(r.url()).pathname === '/v1/appointments');
    await page.getByRole('button', { name: 'Confirm booking' }).click();
    expect((await booked).status()).toBeLessThan(300);
    await page.goto(board);
    await expect(page.getByRole('region', { name: /^Done/ }).getByRole('article', { name: patient.name })).toBeVisible();
    await expect(page.getByRole('group', { name: /^Follow-up due from/ }).filter({ hasText: patient.name })).toHaveCount(0);
    await expect(page.getByRole('region', { name: /^Done/ }).getByRole('article', { name: patient.name }).getByRole('group')).toHaveCount(0);
    expect(watch.leaks([patient.name, patient.dob, patient.mobile])).toEqual([]);
  },
});
