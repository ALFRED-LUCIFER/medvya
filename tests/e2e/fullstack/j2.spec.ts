import { readFile } from 'node:fs/promises';
import { expect, type Page } from '@playwright/test';
import { journey } from '../journeys/storyboard';
import { openPart } from '../support/disclosure';
import { choosePatient, expectArabicAxeClean, expectNoAxeViolations, notOpaque, setNurseIntake, signIn, syntheticPatient, walkIn, watchUrls } from './support';

// MDV-P0-023: journey J2 (returning diabetic patient, docs/02-design/journeys.md) through the real stack. Steps not built yet
// name the ticket that builds them. Meera Iyer is the seeded synthetic patient (docs/00-overview/personas.md).
const rao = /Dr\. Arjun Rao/;
const meera = 'Meera Iyer (synthetic)';
let consultPath = ''; // step 6's consultation, which step 7 shares (the steps run in order on one database)
let chartPath = ''; // step 9's chart and consultation, which steps 10 to 12 share
let studioPath = '';

/** Books Meera with Dr. Rao, takes her through the front desk and starts the consultation from her card (MDV-P2-001). `intake` stands for the
 * nurse's part when nurse intake is on; without it the desk marks the patient ready for the doctor. */
/** Meera's chart from the patient search in the top bar. */
async function openChart(page: Page) {
  await page.getByRole('button', { name: 'Search patients' }).click();
  const search = page.getByRole('dialog', { name: 'Search patients' });
  const input = search.getByRole('combobox', { name: 'Name, MRN or phone' });
  await input.fill('Meera');
  await expect(search.getByRole('option')).toHaveCount(1);
  await input.press('ArrowDown');
  await input.press('Enter');
  await expect(page).toHaveURL(/\/app\/patients\/[0-9a-f-]{36}$/);
  return new URL(page.url()).pathname;
}

async function startVisit(page: Page, intake?: () => Promise<void>) {
  await page.goto('/app/schedule');
  await page.getByRole('list', { name: rao }).getByRole('button', { name: /^Book / }).first().click();
  const panel = await choosePatient(page, 'Meera', /^Meera Iyer \(synthetic\), 54 y, Female/);
  await panel.getByRole('button', { name: 'Confirm booking' }).click();
  await expect(panel).toContainText('Appointment booked');

  await page.goto('/app/front-desk');
  const card = () => page.getByRole('article', { name: meera });
  await card().getByRole('button', { name: 'Check in' }).click();
  await card().getByRole('group', { name: 'Confirm identity' }).getByRole('button', { name: 'Confirm arrival' }).click();
  // With nurse intake on the patient goes to the nurse; without it check-in goes straight to Ready for doctor (MDV audit UX-05, #1104).
  if (intake) {
    await page.getByRole('region', { name: /^Checked in/ }).getByRole('article', { name: meera }).getByRole('button', { name: 'Send to nurse' }).click();
    await intake();
    await page.goto('/app/front-desk');
  } // else check-in already put the patient in Ready for doctor (#1104)
  await page.getByRole('region', { name: /^Ready for doctor/ }).getByRole('article', { name: meera })
    .getByRole('button', { name: `Start consultation with ${meera}` }).click();
  await expect(page).toHaveURL(/\/app\/consult\/[0-9a-f-]{36}$/); // an opaque id, never patient data
}

/** The dev sign-in provider has no password: picking the same doctor again is the step-up (the real Entra prompt=login and MFA are not exercised). */
async function stepUp(page: Page) {
  await page.getByRole('link', { name: rao }).click();
  await page.waitForURL(/\/app\/consult\/[0-9a-f-]{36}(\?resume=(authorise|sign))?$/); // MDV-P2-047: the page then drops the word
}

/** The verify page shows `answer` for the paper's QR token (public: opened signed out). The stack's link origin is the dev server's, so the path is opened here. */
async function expectVerified(page: Page, token: string, answer: string | RegExp) {
  const scan = await page.context().browser()!.newPage({ baseURL: new URL(page.url()).origin });
  await scan.goto(`/app/verify/rx/${token}`);
  await expect(scan.getByRole('status').filter({ hasText: answer })).toBeVisible();
  await scan.close();
}

journey('J2', {
  // MDV-P2-001: Meera is booked, arrives and is ready; Dr. Rao starts the consultation from her card and sees who it is for, in Room 3.
  // shortcut: started from the reception board, which doctors share in a small clinic; the doctor's own Today list is MDV-P2-002.
  1: async ({ page, browser }) => {
    const watch = watchUrls(page);
    await signIn(page, rao);
    await startVisit(page);
    const banner = page.getByRole('region', { name: 'Patient identity' });
    await expect(banner.getByRole('heading', { name: meera })).toBeVisible();
    await expect(banner).toContainText('54 years · Female');
    await expect(banner).toContainText('DQ-10-4482');
    await expect(banner).toContainText('Allergies: not recorded yet'); // unknown is never shown as "none"
    await expect(banner).toContainText('Room 3');
    await expectNoAxeViolations(page);
    await page.reload(); // the server holds the encounter
    await expect(banner.getByRole('heading', { name: meera })).toBeVisible();
    await expectArabicAxeClean(browser, rao, new URL(page.url()).pathname);

    // MDV-P2-009: the doctor types the note; it autosaves on the server and is still there after a reload.
    const note = page.getByRole('region', { name: 'Consultation note' });
    await note.getByLabel('Chief complaint').fill('Increased thirst for two weeks (synthetic)');
    await expect(note.getByRole('status').filter({ hasText: /^Saved \d\d:\d\d$/ })).toBeVisible(); // the one-page view has a second status (the chart panel)
    await page.reload();
    await expect(note.getByLabel('Chief complaint')).toHaveValue('Increased thirst for two weeks (synthetic)');

    // MDV-P2-012 and ADR-016: no Medvya licence check stands in the way, so the gate message is gone. The signing itself is below, after the unsigned note
    // has been found again on Today.
    await expect(note).not.toContainText('Only a doctor can sign this note.');

    // MDV audit P0-2: the doctor records allergies while the consultation is open; once it has ended the vitals and allergies are read only, so this comes before End.
    await note.getByRole('button', { name: 'Confirm: no known allergies' }).click();
    // MDV-P1-091: the doctor confirms name and date of birth with the patient while they are in the room; the server stores who and when.
    await page.getByRole('region', { name: 'Patient identity' }).getByRole('button', { name: 'Confirmed with the patient' }).click();
    await expect(page.getByRole('region', { name: 'Patient identity' })).toContainText(/Identity confirmed with the patient at \d\d:\d\d/);
    await expect(note).toContainText('No known allergies, confirmed at this visit.');

    await page.getByRole('button', { name: 'End consultation' }).click(); // leaves no open consultation for a later run
    await expect(page.getByRole('status').filter({ hasText: 'Consultation ended' })).toHaveText(/^Consultation ended at \d\d:\d\d\.$/);
    // MDV-P2-002: the unsigned note waits on My day (from any day) until it is signed, and opens its consultation again.
    await page.getByRole('link', { name: 'My day' }).first().click();
    const pending = page.getByRole('region', { name: /notes? to sign$/ });
    const row = pending.getByRole('listitem').filter({ hasText: meera }).filter({ hasText: 'Draft, not signed' }).last(); // newest of this run; earlier runs leave theirs
    await row.getByRole('link', { name: 'Open to finish' }).click();
    await expect(page.getByRole('region', { name: 'Consultation note' }).getByLabel('Chief complaint')).toHaveValue('Increased thirst for two weeks (synthetic)');

    // MDV-P2-012: sign on the real API and PostgreSQL by a doctor whose licence the owner attested. The first try needs a fresh sign-in (the dev provider has no
    // password: picking the same user again is the step-up); the real Entra prompt=login and MFA are not exercised here. The save, the 5-minute window, the
    // immutable v1 and its signer line with the attested licence number are the server's.
    await note.getByLabel('Assessment').fill('Raised blood sugar, review (synthetic)');
    await note.getByLabel('Plan').fill('Diet advice, review in two weeks (synthetic)');
    // MDV-AUDIT-1092: no recording consent step before recording exists. MDV audit P0-2: Sign stays locked until allergies are
    // recorded (done above, before End consultation).
    await expect(note.getByRole('button', { name: 'Sign note' })).toBeEnabled();
    await expect(note.getByRole('status').filter({ hasText: /^Saved \d\d:\d\d$/ })).toBeVisible(); // the one-page view has a second status (the chart panel)
    await expect(note.getByRole('button', { name: 'Sign note' })).toHaveAccessibleDescription('Everything reviewed. Ready to sign.');
    await note.getByRole('button', { name: 'Sign note' }).click();
    const dialog = page.getByRole('dialog', { name: `Sign note for ${meera}?` });
    await expect(dialog).toContainText('I have reviewed every section');
    await dialog.getByRole('button', { name: /^Sign as Dr\. Arjun Rao/ }).click();
    await stepUp(page);
    await note.getByRole('button', { name: 'Sign note' }).click();
    await page.getByRole('dialog').getByRole('button', { name: /^Sign as Dr\. Arjun Rao/ }).click();
    await expect(note).toContainText('SIGNED v1 · read-only');
    await expect(note).toContainText(/Signed by Dr\. Arjun Rao.*licence .*SYN-\d+/);
    await expect(note.getByLabel('Plan')).toHaveJSProperty('readOnly', true);
    await expect(note.getByRole('button', { name: 'Sign note' })).toHaveCount(0);
    await page.reload(); // the signature is the server's, not the page's
    await expect(note).toContainText('SIGNED v1 · read-only');
    expect(watch.leaks(['Meera', 'Iyer', '1972-03-12', 'DQ-10-4482', 'thirst'])).toEqual([]); // whole values: a short digit run can occur by chance in a random id
  },
  2: 'MDV-P2-004', // E-01 Patient 360 brief (verified facts vs AI summary)
  3: 'MDV-P3-005', // A-03 live capture; recording consent first is MDV-P3-003
  4: 'MDV-P3-009', // A-04 speaker confirmation
  5: 'MDV-P3-012', // A-05 note review, conflicts and sign (signing itself is MDV-P2-012)
  // E-05 (MDV-P2-015, MDV-P2-017, MDV-P2-018): on the real API and PostgreSQL, the doctor prescribes from the formulary, authorises with the step-up (separate
  // from the note), places the HbA1c order as its own action, prints, and the paper's QR verifies as valid. Cancelling it (a reason, no replacement) makes
  // the paper verify as cancelled and stop printing; authorising again issues version 2.
  // MDV-P1-087 (audit finding F-01) comes first: a medicine typed as free text cannot be matched to an allergy, so for a patient with recorded allergies the
  // server refuses to authorise until the doctor gives a reason for that line, and the paper carries the reason; with "no known allergies" confirmed only
  // the "Not checked" note shows. Meera has no known allergies at this point of the journey (step 2; her penicillin allergy is recorded in step 9), so
  // both cases use a walk-in patient of this run, in a page of their own: its step-up must not count for Meera's authorisation below.
  6: async ({ page, browser }) => {
    const typed = await browser.newPage();
    try {
      const typedUrls = watchUrls(typed);
      await signIn(typed, rao);
      const rx = typed.getByRole('region', { name: 'Prescription' });
      const checks = rx.getByRole('region', { name: 'Safety checks' });
      const status = rx.getByRole('status').filter({ hasText: 'Authorised' });
      /** A walk-in patient in consultation with Dr. Rao, with Augmentin 625 Duo typed as free text and given a complete instruction. */
      const typeFor = async (patient: ReturnType<typeof syntheticPatient>, recordAllergies: () => Promise<void>) => {
        await walkIn(typed, patient, rao);
        await typed.goto('/app/front-desk');
        await typed.getByRole('region', { name: /^Ready for doctor/ }).getByRole('article', { name: patient.name })
          .getByRole('button', { name: `Start consultation with ${patient.name}` }).click();
        await expect(typed).toHaveURL(/\/app\/consult\/[0-9a-f-]{36}$/);
        await recordAllergies();
        await rx.getByLabel('Medicine', { exact: true }).fill('Augmentin 625 Duo');
        await rx.getByRole('button', { name: 'Add as free text' }).click();
        const line = rx.getByRole('region', { name: 'Medicines to prescribe' }).getByRole('row', { name: 'Augmentin 625 Duo' });
        await line.getByLabel('Dose', { exact: true }).fill('1');
        for (const [field, option] of [['Unit', 'Tablet'], ['Route', 'By mouth'], ['How often', 'Twice a day (BD)']] as const) {
          await line.getByRole('combobox', { name: field, exact: true }).click();
          await typed.getByRole('option', { name: option, exact: true }).click();
        }
        await expect(line).toContainText('Take 1 tablet by mouth twice a day.');
      };
      const note = typed.getByRole('region', { name: 'Consultation note' });

      // Recorded allergies (two, so the alert names every one): refused without a reason, authorised with one, the reason on the paper.
      const allergic = syntheticPatient('Kavya', 'Nair');
      await typeFor(allergic, async () => {
        for (const substance of ['Penicillin', 'Sulfa']) {
          await note.getByLabel('Allergic to').fill(substance);
          await note.getByRole('button', { name: 'Add allergy' }).click();
          await expect(note.getByLabel('Allergic to')).toHaveValue('');
        }
      });
      const allergicPath = new URL(typed.url()).pathname;
      // Severity and finding in words (an alert role, an icon and text: never colour alone).
      await expect(checks.getByRole('alert')).toContainText('Severe · Not checked against: Penicillin, Sulfa. Augmentin 625 Duo was typed as free text.');
      await expect(checks).toContainText('Augmentin 625 Duo: typed as free text, so not checked for allergies, interactions or dose.'); // the honest note stays
      await rx.getByRole('button', { name: 'Authorise prescription' }).click();
      await expect(rx.getByRole('alert').filter({ hasText: 'A severe alert has no reason yet.' })).toBeVisible(); // the server's refusal: still a draft
      await expect(rx.getByRole('status').filter({ hasText: /^Draft/ })).toBeVisible();

      // The reason step by keyboard alone, then axe with the alert and the field open, in English and in Arabic.
      await checks.getByRole('button', { name: 'Give a reason to go ahead' }).focus();
      await typed.keyboard.press('Enter');
      const reason = checks.getByLabel('Reason to go ahead');
      await reason.focus();
      await expectNoAxeViolations(typed);
      const arabic = await browser.newPage();
      try {
        await signIn(arabic, rao, 'ar');
        await arabic.goto(allergicPath);
        await expect(arabic.locator('html')).toHaveAttribute('dir', 'rtl');
        await expect(arabic.getByRole('alert').filter({ hasText: 'لم يُفحص مقابل: Penicillin, Sulfa. Augmentin 625 Duo مكتوب نصًا حرًا.' })).toBeVisible();
        await arabic.getByRole('button', { name: 'اذكر سبب المتابعة' }).click();
        await expect(arabic.getByLabel('سبب المتابعة')).toBeVisible();
        await arabic.waitForLoadState('networkidle');
        await expectNoAxeViolations(arabic);
      } finally {
        await arabic.close();
      }
      await typed.keyboard.type('Tolerated co-amoxiclav before, patient confirms (synthetic)');
      await typed.keyboard.press('Enter');
      await expect(checks).toContainText('Overridden with reason: Tolerated co-amoxiclav before, patient confirms (synthetic)');
      await rx.getByRole('button', { name: 'Authorise prescription' }).click();
      await rx.getByRole('button', { name: 'Confirm identity' }).click(); // asked only now: the server checks the alerts before the step-up
      await typed.getByRole('link', { name: rao }).click(); // the step-up, as stepUp() does for the journey's own page
      await typed.waitForURL(/\/app\/consult\/[0-9a-f-]{36}(\?resume=authorise)?$/);
      await rx.getByRole('button', { name: 'Authorise prescription' }).click();
      await expect(status).toHaveText(/^Authorised · version 1 · RX-[0-9A-F]{8}$/);
      await rx.getByRole('link', { name: 'Print prescription' }).click();
      await expect(typed.getByText('Allergies: Penicillin, Sulfa')).toBeVisible();
      await expect(typed.getByRole('listitem').filter({ hasText: 'Augmentin 625 Duo' })).toContainText('Reason: Tolerated co-amoxiclav before, patient confirms (synthetic)');
      await typed.goto(allergicPath);
      await typed.getByRole('button', { name: 'End consultation' }).click(); // one open consultation at a time
      await expect(typed.getByRole('status').filter({ hasText: 'Consultation ended' })).toBeVisible();

      // No known allergies, confirmed: the "Not checked" note only, no severe alert, no reason asked (the step-up above is still fresh).
      const clear = syntheticPatient('Divya', 'Pillai');
      await typeFor(clear, async () => {
        await note.getByRole('button', { name: 'Confirm: no known allergies' }).click();
        await expect(note).toContainText('No known allergies, confirmed at this visit.');
      });
      await expect(checks).toContainText('Allergies: no known allergies (recorded)');
      await expect(checks).toContainText('No alerts from the checks that could run.');
      await expect(checks).toContainText('Augmentin 625 Duo: typed as free text, so not checked for allergies, interactions or dose.');
      await expect(checks.getByRole('button', { name: 'Give a reason to go ahead' })).toHaveCount(0);
      await rx.getByRole('button', { name: 'Authorise prescription' }).click();
      await expect(status).toHaveText(/^Authorised · version 1 · RX-[0-9A-F]{8}$/);
      await typed.getByRole('button', { name: 'End consultation' }).click(); // Meera's consultation below is the open one
      await expect(typed.getByRole('status').filter({ hasText: 'Consultation ended' })).toBeVisible();
      // The typed medicine travels in the formulary search (GET /v1/formulary?q=, no patient in that URL, by design in prescriptionApi.ts), so it is
      // not in this list; the allergies, the reason and the patients are.
      expect(typedUrls.leaks(['Penicillin', 'Sulfa', 'co-amoxiclav', 'Tolerated', allergic.name, clear.name, 'Kavya', 'Divya'])).toEqual([]);
    } finally {
      await typed.close();
    }

    const watch = watchUrls(page);
    await signIn(page, rao);
    await startVisit(page);
    consultPath = new URL(page.url()).pathname;
    const rx = page.getByRole('region', { name: 'Prescription' });
    await rx.getByLabel('Medicine', { exact: true }).fill('Metformin');
    await rx.getByRole('list', { name: 'Formulary matches' }).getByRole('button', { name: /^Add Metformin 500 mg/ }).click();
    const line = rx.getByRole('region', { name: 'Medicines to prescribe' }).getByRole('row', { name: 'Metformin' });
    await line.getByLabel('Dose', { exact: true }).fill('1');
    for (const [field, option] of [['Unit', 'Tablet'], ['Route', 'By mouth'], ['How often', 'Twice a day (BD)']] as const) {
      await line.getByRole('combobox', { name: field, exact: true }).click();
      await page.getByRole('option', { name: option, exact: true }).click();
    }
    await expect(line).toContainText('Take 1 tablet by mouth twice a day.');

    const authorise = rx.getByRole('button', { name: 'Authorise prescription' });
    await authorise.click();
    await expect(rx.getByRole('alert')).toContainText('Confirm your identity to authorise.');
    // MDV-P2-047: the round trip carries opaque identifiers only (AC4): every address (the provider's address that /bff/reauth answers is the
    // next request, so it is in the list) and the provider's state value, which the server
    // protects (encrypted), so the return address and the session inside it cannot be read from it either.
    const trip: string[] = [];
    const states: string[] = [];
    let authorisePosts = 0;
    page.on('request', r => {
      trip.push(r.url());
      const state = new URL(r.url()).searchParams.get('state');
      if (state) states.push(state);
      if (r.url().endsWith('/bff/reauth')) trip.push((r.postDataJSON() as { returnUrl: string }).returnUrl);
      if (r.method() === 'POST' && r.url().endsWith('/prescription/authorisation')) authorisePosts++;
    });
    await rx.getByRole('button', { name: 'Confirm identity' }).click();
    await stepUp(page);
    // Back on the section that asked (AC1, AC2): Authorise is in view and focused, and the status says what to press.
    await expect(authorise).toBeFocused();
    await expect(authorise).toBeInViewport();
    await expect(rx.getByRole('status').filter({ hasText: 'Identity confirmed. Press Authorise prescription.' })).toBeVisible();
    await expect(page).toHaveURL(new RegExp(`${consultPath}$`)); // the word leaves the address once read
    expect(trip).toContain(`${consultPath}?resume=authorise`);
    expect(notOpaque(trip)).toEqual([]);
    expect(states.length).toBeGreaterThan(0);
    for (const state of states) expect(Buffer.from(state, 'base64url').toString('latin1')).not.toMatch(/resume|consult|reauth|Meera/);
    await expectNoAxeViolations(page); // AC6 in English here; Arabic after the return is in rx-print.spec.ts
    expect(authorisePosts).toBe(0); // AC3: coming back authorised nothing
    await expect(rx.getByRole('status').filter({ hasText: /^Draft/ })).toBeVisible();
    await authorise.click();
    await expect(rx.getByRole('status').filter({ hasText: 'Authorised' })).toHaveText(/^Authorised · version 1 · RX-[0-9A-F]{8}$/);
    expect(authorisePosts).toBe(1);

    await openPart(page, 'Orders');
    const orders = page.getByRole('region', { name: 'Orders' });
    await expect(orders).not.toContainText('Ordered'); // authorising the prescription placed nothing
    await orders.getByLabel('Test', { exact: true }).fill('HbA1c');
    await orders.getByRole('button', { name: 'Add test' }).click();
    await orders.getByRole('button', { name: 'Place orders' }).click();
    await expect(orders.getByText('Orders placed.')).toBeVisible();
    await expectNoAxeViolations(page);
    // MDV-P2-018: the slip's server PDF, stored in Blob with its hash and QR (the QR check itself is proven in PaperPdfTests).
    await page.getByRole('link', { name: 'Investigation slip' }).click();
    const slipPdf = page.waitForEvent('download');
    await page.getByRole('button', { name: 'Download PDF' }).click();
    const slip = await slipPdf;
    expect(slip.suggestedFilename()).toBe('investigation-slip.pdf');
    expect((await readFile((await slip.path())!)).subarray(0, 5).toString()).toBe('%PDF-');
    await page.goto(consultPath);

    await rx.getByRole('link', { name: 'Print prescription' }).click();
    await expect(page.getByText('Take 1 tablet by mouth twice a day.')).toBeVisible();
    const paper = (await page.getByText(/^Check this prescription is valid/).textContent())!.match(/\/verify\/rx\/([A-Za-z0-9_-]+)/)![1]!;
    await expectVerified(page, paper, 'Valid prescription');

    await page.goto(consultPath);
    await rx.getByRole('button', { name: 'Cancel prescription' }).click();
    await rx.getByLabel('Reason for cancelling').fill('Written for the wrong visit (synthetic)');
    await rx.getByRole('button', { name: 'Confirm cancellation' }).click(); // still inside the step-up's 5 minutes
    await expect(rx.getByRole('status').filter({ hasText: 'Cancelled' })).toHaveText(/^Cancelled · version 1 · RX-/);
    await expect(rx).toContainText('Cancelled: Written for the wrong visit (synthetic)');
    await expectVerified(page, paper, /^Cancelled: the doctor withdrew this prescription/);
    await page.goto(`${consultPath}/rx/print`);
    await expect(page.getByRole('alert')).toContainText('This prescription was cancelled, so it does not print.');
    await expectArabicAxeClean(browser, rao, consultPath);

    await page.goto(consultPath);
    await rx.getByRole('button', { name: 'Authorise again' }).click();
    await expect(rx.getByRole('status').filter({ hasText: 'Authorised' })).toHaveText(/^Authorised · version 2 · RX-/);

    // MDV-1087: the authorised line is on the chart's medicines and the timeline, linked to its visit and its printed prescription; the
    // cancelled version's line stays as stopped history.
    const chart = await openChart(page);
    const prescribed = page.getByRole('region', { name: 'Medicines', exact: true }).getByRole('listitem').filter({ hasText: 'Metformin 500 mg' });
    await expect(prescribed).toHaveCount(2);
    await expect(prescribed.filter({ hasText: 'Stopped' })).toHaveCount(1);
    const active = prescribed.filter({ hasText: 'Active' });
    await expect(active).toContainText('Take 1 tablet by mouth twice a day.');
    await expect(active.getByRole('link', { name: 'Prescribed at this visit' })).toHaveAttribute('href', consultPath);
    await expect(active.getByRole('link', { name: 'Printed prescription' })).toHaveAttribute('href', `${consultPath}/rx/print`);
    await page.goto(`${chart}/timeline`);
    await page.getByRole('button', { name: 'Medicines', exact: true }).click();
    const entries = page.getByRole('region', { name: 'Timeline entries' }).getByRole('listitem').filter({ hasText: 'Metformin 500 mg' });
    await expect(entries).toHaveCount(2);
    await expect(entries.first().getByRole('link', { name: 'Printed prescription' })).toHaveAttribute('href', `${consultPath}/rx/print`);
    await expectNoAxeViolations(page);
    await expectArabicAxeClean(browser, rao, chart);
    // The formulary search carries the medicine name the doctor types (GET /v1/formulary?q=, by design in prescriptionApi.ts), so it is not in this list.
    expect(watch.leaks(['Meera', 'Iyer', '1972-03-12', 'DQ-10-4482', 'HbA1c', 'wrong visit'])).toEqual([]);
  },
  // E-10 (MDV-P2-020): the authorised version 2 goes to a pharmacy by secure link from the print page; the message names only the clinic, and the dispatch is
  // recorded as Sent. The patient's own link needs a code by SMS, which waits for an SMS provider (MDV-P1-045).
  7: async ({ page }) => {
    expect(consultPath, 'step 6 opens the consultation this step shares').not.toBe('');
    await signIn(page, rao);
    await page.goto(`${consultPath}/rx/print`);
    const share = page.getByRole('region', { name: 'Share with the patient or a pharmacy' });
    await share.getByRole('combobox', { name: 'Send to' }).click();
    await page.getByRole('option', { name: 'A pharmacy, by email' }).click();
    await share.getByLabel('Pharmacy name').fill('Apollo Pharmacy Adyar (synthetic)');
    await share.getByLabel('Pharmacy email').fill('orders@pharmacy.example.test');
    await share.getByRole('button', { name: 'Send secure link' }).click();
    await expect(share.getByRole('row', { name: /Apollo Pharmacy Adyar/ })).toContainText('Sent to the pharmacy');
    await expectNoAxeViolations(page);
    await page.goto(consultPath);
    await page.getByRole('button', { name: 'End consultation' }).click(); // step 9 starts another; one open consultation at a time
    await expect(page.getByRole('status').filter({ hasText: 'Consultation ended' })).toBeVisible();
  },
  8: 'MDV-P4-016', // C-02 care plan with an owner
  // E-01 (MDV-P2-033): the nurse records Meera's penicillin allergy at intake (MDV-P2-008; the seed gives her none, and step 1 proves "not recorded"),
  // Dr. Rao starts the consultation and opens her chart from the search: one page, the allergy in the banner, the section buttons.
  // shortcut: the allergy stays recorded, so on a kept stack (E2E_REUSE_STACK) J2.1 and this step fail on a second run; CI always starts fresh. Upgrade if specs get a per-run reset.
  9: async ({ page, browser }) => {
    const watch = watchUrls(page);
    await setNurseIntake(browser, true);
    try {
      await signIn(page, rao);
      await startVisit(page, async () => {
        const nurse = await browser.newPage();
        try {
          await signIn(nurse, /S\. Thomas/);
          await nurse.goto('/app/front-desk');
          await nurse.getByRole('region', { name: /^With nurse/ }).getByRole('link', { name: `Open intake for ${meera}` }).click();
          const allergies = nurse.getByRole('region', { name: 'Allergies' });
          await allergies.getByLabel('Allergic to').fill('Penicillin');
          await allergies.getByLabel('Reaction').fill('Rash (synthetic)');
          await allergies.getByRole('button', { name: 'Add allergy' }).click();
          await expect(allergies.getByText('Penicillin · Rash (synthetic)')).toBeVisible();
          await nurse.getByRole('button', { name: 'Hand off · Ready for doctor' }).click();
          await expect(nurse).toHaveURL(/\/nurse$/);
        } finally {
          await nurse.close();
        }
      });
    } finally {
      await setNurseIntake(browser, false); // later journeys mark intake done from the board
    }
    studioPath = new URL(page.url()).pathname;
    await expect(page.getByRole('region', { name: 'Patient identity' })).toContainText('Penicillin');

    chartPath = await openChart(page);
    const banner = page.getByRole('region', { name: `Patient ${meera}` });
    await expect(banner).toContainText(/Allergy: .?Penicillin.? · /);
    await expect(page.getByRole('group', { name: 'Show part of the chart' }).getByRole('button', { name: 'Everything' })).toHaveAttribute('aria-pressed', 'true');
    for (const card of ['Problems', 'Medicines', 'Last vitals', 'Results and trends']) await expect(page.getByRole('region', { name: card, exact: true })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Open consultation' })).toHaveAttribute('href', studioPath);
    await expectNoAxeViolations(page);
    await expectArabicAxeClean(browser, rao, chartPath);
    expect(watch.leaks(['Meera', 'Iyer', '1972-03-12', 'DQ-10-4482', 'Penicillin', 'Rash'])).toEqual([]);
  },
  // E-05 / E-11 (MDV-P2-039, MDV-P2-016): Add medicine on the chart's Medicines card puts the line on the open consultation's draft. Amoxicillin is
  // blocked by the penicillin allergy (the server's class rule); closing without a reason switches that line off. Metformin goes on the draft.
  10: async ({ page, browser }) => {
    expect(chartPath, 'step 9 opens the chart this step uses').not.toBe('');
    const watch = watchUrls(page);
    await signIn(page, rao);
    await page.goto(chartPath);
    const open = () => page.getByRole('region', { name: 'Medicines', exact: true }).getByRole('button', { name: 'Add medicine' }).click();
    const panel = page.getByRole('dialog', { name: 'Add medicine' });
    const choose = (group: string, option: string) => panel.getByRole('group', { name: group }).getByRole('button', { name: option, exact: true }).click();

    await open();
    await expect(panel.getByRole('region', { name: 'Patient' })).toContainText('Penicillin');
    await panel.getByLabel('Search medicine').fill('amox');
    await panel.getByRole('button', { name: /^Amoxicillin/ }).first().click();
    await choose('How often', '1-1-1 · three times a day');
    await choose('When', 'After food');
    await choose('For how long', '5 days');
    await expect(panel.getByText('A serious finding blocks this medicine.', { exact: false })).toBeVisible();
    await expect(panel.getByRole('region', { name: 'Safety checks' })).toContainText(/Severe · .*Penicillin/);
    await expect(panel.getByRole('button', { name: 'Add to prescription' })).toBeDisabled();
    await expectNoAxeViolations(page);
    await panel.getByRole('button', { name: 'Cancel' }).click();
    await expect(panel).toBeHidden();

    await open();
    await panel.getByLabel('Search medicine').fill('metformin');
    await panel.getByRole('button', { name: /^Metformin/ }).first().click();
    await choose('How often', '1-0-1 · twice a day');
    await choose('When', 'After food');
    await choose('For how long', '1 month');
    await expect(panel.getByText('Quantity: 60')).toBeVisible();
    await panel.getByRole('button', { name: 'Add to prescription' }).click();
    await expect(page.getByRole('status').filter({ hasText: 'Metformin is on the draft prescription of the open consultation.' })).toBeVisible();

    await page.goto(studioPath); // the server holds the draft: the consultation shows the line, and the amoxicillin one (not included) is not listed
    const rx = page.getByRole('region', { name: 'Prescription' });
    const lines = rx.getByRole('region', { name: 'Medicines to prescribe' });
    await expect(lines.getByRole('row', { name: /^Metformin/ })).toHaveCount(1);
    await expect(lines.getByRole('row', { name: /^Amoxicillin/ })).toHaveCount(0); // a removed line is not listed (#1095)
    await expectArabicAxeClean(browser, rao, chartPath);
    // The formulary search carries the typed medicine name (GET /v1/formulary?q=, by design), so medicine names are not in this list.
    expect(watch.leaks(['Meera', 'Iyer', '1972-03-12', 'DQ-10-4482', 'Penicillin'])).toEqual([]);
  },
  // E-04 (MDV-P2-041, MDV-P2-044): the consultation opens on one page; the doctor writes it, and Sign stays locked until the checklist is complete.
  11: async ({ page, browser }) => {
    expect(studioPath, 'step 9 starts the consultation this step writes').not.toBe('');
    await signIn(page, rao);
    await page.goto(studioPath);
    await expect(page.getByRole('button', { name: 'One page' })).toHaveAttribute('aria-pressed', 'true'); // the default view
    const note = page.getByRole('region', { name: 'Consultation note' });
    await expect(note.getByRole('navigation', { name: 'Note sections' }).getByRole('link')).toHaveCount(7);
    const sign = note.getByRole('button', { name: 'Sign note' });
    await expect(sign).toBeDisabled();
    await expect(sign).toHaveAccessibleDescription(/^\d items? still needs? your review before signing$/);
    // MDV-P1-091: no identity item is done on opening; the doctor's "Confirmed with the patient" is stored on the server (who and when) and ticks it.
    const tools = note.getByRole('complementary', { name: 'Consultation tools' });
    const checklist = tools.getByRole('region', { name: 'Review and sign' }).getByRole('listitem');
    await tools.getByRole('tab', { name: /^Checklist / }).click();
    await expect(checklist.filter({ hasText: 'Confirmed with the patient' })).toContainText('To do');
    await expect(checklist.filter({ hasText: 'AI suggestion' })).toHaveCount(0); // no AI drafting in this clinic
    const strip = page.getByRole('region', { name: 'Patient identity' });
    await strip.getByRole('button', { name: 'Confirmed with the patient' }).click();
    await expect(strip).toContainText(/Identity confirmed with the patient at \d\d:\d\d/);
    await page.reload(); // the server holds it
    await expect(strip).toContainText(/Identity confirmed with the patient at \d\d:\d\d/);
    await tools.getByRole('tab', { name: /^Checklist / }).click();
    await expect(checklist.filter({ hasText: 'Confirmed with the patient' })).toContainText('Done');

    // a returning patient: each section also offers an Insert button for the last signed note's text
    // MDV-P1-089 (F-03, F-26): the last keystrokes survive a visit to Problems and a reload, both inside the autosave delay, and the note shows
    // what the server holds. Nothing of the note is in browser storage.
    await note.getByRole('textbox', { name: /^Assessment/ }).fill('Type 2 diabetes, fair control (synthetic)');
    await page.getByRole('link', { name: 'Problems and diagnoses' }).click();
    await page.getByRole('link', { name: 'Back to the consultation' }).click();
    await expect(note.getByRole('textbox', { name: /^Assessment/ })).toHaveValue('Type 2 diabetes, fair control (synthetic)');
    page.once('dialog', d => void d.accept()); // "Leave site?": the doctor confirms the reload
    await note.getByRole('textbox', { name: /^Plan/ }).fill('Continue metformin, HbA1c in three months (synthetic)');
    await page.reload();
    await expect(note.getByRole('textbox', { name: /^Plan/ })).toHaveValue('Continue metformin, HbA1c in three months (synthetic)');
    await expect(note.getByRole('textbox', { name: /^Assessment/ })).toHaveValue('Type 2 diabetes, fair control (synthetic)');
    const stored = await page.evaluate(async () => JSON.stringify([localStorage, sessionStorage, (await indexedDB.databases()).map(d => d.name)]));
    expect(stored).not.toMatch(/diabetes|metformin/i);
    expect(stored).toMatch(/\[\]\]$/); // no IndexedDB database at all
    await note.getByRole('textbox', { name: /^Chief complaint/ }).fill('Review of sugars (synthetic)');
    await expect(note.getByRole('status').filter({ hasText: /^Saved \d\d:\d\d$/ })).toBeVisible();
    // Step 6's authorised metformin is on Meera's chart, so this visit's medicines need their reconciliation (MDV-1087); there is no consent step (MDV-AUDIT-1092).
    await expect(sign).toBeDisabled();
    await expect(sign).toHaveAccessibleDescription('1 item still needs your review before signing');
    await tools.getByRole('tab', { name: /^Checklist 5\/6$/ }).click();
    await expect(tools.getByRole('region', { name: 'Review and sign' }).getByRole('listitem').filter({ hasText: 'Medicines reconciled' })).toContainText('To do');
    await expect(tools.getByRole('region', { name: 'Review and sign' }).getByRole('listitem').filter({ hasText: 'Recording consent' })).toHaveCount(0);
    await expect(tools.getByRole('region', { name: 'Review and sign' }).getByRole('listitem').filter({ hasText: 'Allergies reconciled' })).toContainText('Done');
    await expectNoAxeViolations(page);

    await page.goto(`${studioPath}/medicines`);
    const metformin = page.getByRole('group', { name: /^Decision for Metformin/ });
    await metformin.getByRole('button', { name: 'Keep' }).click();
    await expect(page.getByRole('status').filter({ hasText: 'decided' })).toContainText('1 of 1 decided');
    await page.getByRole('button', { name: 'Confirm changes' }).click();
    await expect(page.getByText(/^Confirmed at \d\d:\d\d\./)).toBeVisible();
    await page.goto(studioPath);
    await expect(sign).toBeEnabled();
    await expect(sign).toHaveAccessibleDescription('Everything reviewed. Ready to sign.');
    await expectArabicAxeClean(browser, rao, studioPath);
  },
  // E-06 (MDV-P2-012): signing asks for the step-up, then the note is v1 and read-only, on the server.
  12: async ({ page }) => {
    expect(studioPath, 'step 9 starts the consultation this step signs').not.toBe('');
    const watch = watchUrls(page);
    await signIn(page, rao);
    await page.goto(studioPath);
    const note = page.getByRole('region', { name: 'Consultation note' });
    // MDV-P1-090: step 10's metformin is still a draft, so Sign note first names it; signing does not authorise it.
    const unfinished = page.getByRole('dialog', { name: `Not finished for ${meera}` });
    await note.getByRole('button', { name: 'Sign note' }).click();
    await expect(unfinished.getByRole('region', { name: 'Prescription not authorised' })).toContainText('Metformin');
    await unfinished.getByRole('button', { name: 'Sign anyway' }).click();
    await page.getByRole('dialog', { name: `Sign note for ${meera}?` }).getByRole('button', { name: /^Sign as Dr\. Arjun Rao/ }).click();
    await stepUp(page);
    await note.getByRole('button', { name: 'Sign note' }).click();
    await unfinished.getByRole('button', { name: 'Sign anyway' }).click();
    await page.getByRole('dialog').getByRole('button', { name: /^Sign as Dr\. Arjun Rao/ }).click();
    await expect(note).toContainText('SIGNED v1 · read-only');
    await expect(note.getByRole('textbox', { name: /^Plan/ })).toHaveJSProperty('readOnly', true);
    await page.reload();
    await expect(note).toContainText('SIGNED v1 · read-only');
    await expectNoAxeViolations(page);
    await page.getByRole('button', { name: 'End consultation' }).click(); // leaves Dr. Rao no open consultation for the later journeys
    // The draft is discarded with a reason on the server (MDV-P1-090), so it does not wait on Dr. Rao's day in the later journeys.
    await unfinished.getByRole('button', { name: 'Discard the draft' }).click();
    await unfinished.getByLabel('Reason for discarding').fill('Journey test: not prescribed (synthetic)');
    await unfinished.getByRole('button', { name: 'Discard and continue' }).click();
    await expect(page.getByRole('status').filter({ hasText: 'Consultation ended' })).toBeVisible();
    expect(watch.leaks(['Meera', 'Iyer', '1972-03-12', 'DQ-10-4482', 'sugars'])).toEqual([]);
  },
});
