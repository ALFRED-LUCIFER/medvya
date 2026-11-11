import { expect, type Page } from '@playwright/test';
import { journey } from '../journeys/storyboard';
import { expectArabicAxeClean, expectNoAxeViolations, signIn, watchUrls } from './support';

// MDV-P0-023: journey J2 (returning diabetic patient, docs/02-design/journeys.md) through the real stack. Only the start of the visit is built;
// every later step names the ticket that builds it. Meera Iyer is the seeded synthetic patient (docs/00-overview/personas.md).
const rao = /Dr\. Arjun Rao/;
const meera = 'Meera Iyer (synthetic)';
let consultPath = ''; // step 6's consultation, which step 7 shares (the steps run in order on one database)

/** Books Meera with Dr. Rao, takes her through the front desk and starts the consultation from her card (MDV-P2-001). */
async function startVisit(page: Page) {
  await page.goto('/app/schedule');
  await page.getByRole('list', { name: rao }).getByRole('button', { name: /^Book / }).first().click();
  const panel = page.getByRole('complementary', { name: 'Book appointment' });
  await panel.getByRole('searchbox', { name: 'Name, MRN, phone or ID' }).fill('Meera');
  await panel.getByRole('button', { name: 'Search' }).click();
  await panel.getByRole('button', { name: /^M I, 54 y, Female/ }).click();
  await panel.getByRole('combobox', { name: 'Reason category' }).click();
  await page.getByRole('option', { name: 'Long-term condition review' }).click();
  await panel.getByRole('button', { name: 'Confirm booking' }).click();
  await expect(panel).toContainText('Appointment booked');

  await page.goto('/app/front-desk');
  const card = () => page.getByRole('article', { name: meera });
  await card().getByRole('button', { name: 'Check in' }).click();
  await card().getByRole('group', { name: 'Confirm identity' }).getByRole('button', { name: 'Confirm arrival' }).click();
  await page.getByRole('region', { name: /^Checked in/ }).getByRole('article', { name: meera }).getByRole('button', { name: 'Send to nurse' }).click();
  await page.getByRole('region', { name: /^With nurse/ }).getByRole('article', { name: meera }).getByRole('button', { name: 'Intake done' }).click();
  await page.getByRole('region', { name: /^Ready for doctor/ }).getByRole('article', { name: meera })
    .getByRole('button', { name: `Start consultation with ${meera}` }).click();
  await expect(page).toHaveURL(/\/app\/consult\/[0-9a-f-]{36}$/); // an opaque id, never patient data
}

/** The dev sign-in provider has no password: picking the same doctor again is the step-up (the real Entra prompt=login and MFA are not exercised). */
async function stepUp(page: Page) {
  await page.getByRole('link', { name: rao }).click();
  await page.waitForURL(/\/app\/consult\/[0-9a-f-]{36}$/);
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
    await expect(note.getByRole('status')).toHaveText('Saved');
    await page.reload();
    await expect(note.getByLabel('Chief complaint')).toHaveValue('Increased thirst for two weeks (synthetic)');

    // MDV-P2-012 and ADR-016: no Medvya licence check stands in the way, so the gate message is gone. The signing itself is below, after the unsigned note
    // has been found again on Today.
    await expect(note).not.toContainText('Only a doctor can sign this note.');

    await page.getByRole('button', { name: 'End consultation' }).click(); // leaves no open consultation for a later run
    await expect(page.getByRole('status').filter({ hasText: 'Consultation ended' })).toHaveText(/^Consultation ended at \d\d:\d\d\.$/);
    // MDV-P2-002: the unsigned note waits on Today (from any day) until it is signed, and opens its consultation again.
    await page.getByRole('link', { name: 'Today' }).first().click();
    const pending = page.getByRole('region', { name: /notes? to sign$/ });
    const row = pending.getByRole('listitem').filter({ hasText: meera }).filter({ hasText: 'Draft, not signed' }).last(); // newest of this run; earlier runs leave theirs
    await row.getByRole('link', { name: 'Open to finish' }).click();
    await expect(page.getByRole('region', { name: 'Consultation note' }).getByLabel('Chief complaint')).toHaveValue('Increased thirst for two weeks (synthetic)');

    // MDV-P2-012: sign on the real API and PostgreSQL by a doctor whose licence the owner attested. The first try needs a fresh sign-in (the dev provider has no
    // password: picking the same user again is the step-up); the real Entra prompt=login and MFA are not exercised here. The save, the 5-minute window, the
    // immutable v1 and its signer line with the attested licence number are the server's.
    await note.getByLabel('Assessment').fill('Raised blood sugar, review (synthetic)');
    await note.getByLabel('Plan').fill('Diet advice, review in two weeks (synthetic)');
    await expect(note.getByRole('status')).toHaveText('Saved');
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
  6: async ({ page, browser }) => {
    const watch = watchUrls(page);
    await signIn(page, rao);
    await startVisit(page);
    consultPath = new URL(page.url()).pathname;
    const rx = page.getByRole('region', { name: 'Prescription' });
    await rx.getByLabel('Medicine', { exact: true }).fill('Metformin');
    await rx.getByRole('list', { name: 'Formulary matches' }).getByRole('button', { name: /^Add Metformin 500 mg/ }).click();
    const line = rx.getByRole('list', { name: 'Medicines to prescribe' }).getByRole('listitem').first();
    await line.getByLabel('Dose', { exact: true }).fill('1');
    for (const [field, option] of [['Unit', 'Tablet'], ['Route', 'By mouth'], ['How often', 'Twice a day (BD)']] as const) {
      await line.getByRole('combobox', { name: field, exact: true }).click();
      await page.getByRole('option', { name: option, exact: true }).click();
    }
    await expect(line).toContainText('Take 1 tablet by mouth twice a day.');

    await rx.getByRole('button', { name: 'Authorise prescription' }).click();
    await expect(rx.getByRole('alert')).toContainText('Confirm your identity to authorise.');
    await rx.getByRole('button', { name: 'Confirm identity' }).click();
    await stepUp(page);
    await rx.getByRole('button', { name: 'Authorise prescription' }).click();
    await expect(rx.getByRole('status').filter({ hasText: 'Authorised' })).toHaveText(/^Authorised · version 1 · RX-[0-9A-F]{8}$/);

    const orders = page.getByRole('region', { name: 'Orders' });
    await expect(orders).not.toContainText('Ordered'); // authorising the prescription placed nothing
    await orders.getByLabel('Test', { exact: true }).fill('HbA1c');
    await orders.getByRole('button', { name: 'Add test' }).click();
    await orders.getByRole('button', { name: 'Place orders' }).click();
    await expect(orders.getByText('Orders placed.')).toBeVisible();
    await expectNoAxeViolations(page);

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
  },
  8: 'MDV-P4-016', // C-02 care plan with an owner
});
