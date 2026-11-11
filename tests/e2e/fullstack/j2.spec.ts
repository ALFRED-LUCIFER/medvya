import { expect } from '@playwright/test';
import { journey } from '../journeys/storyboard';
import { expectArabicAxeClean, expectNoAxeViolations, signIn, watchUrls } from './support';

// MDV-P0-023: journey J2 (returning diabetic patient, docs/02-design/journeys.md) through the real stack. Only the start of the visit is built;
// every later step names the ticket that builds it. Meera Iyer is the seeded synthetic patient (docs/00-overview/personas.md).
const rao = /Dr\. Arjun Rao/;
const meera = 'Meera Iyer (synthetic)';

journey('J2', {
  // MDV-P2-001: Meera is booked, arrives and is ready; Dr. Rao starts the consultation from her card and sees who it is for, in Room 3.
  // shortcut: started from the reception board, which doctors share in a small clinic; the doctor's own Today list is MDV-P2-002.
  1: async ({ page, browser }) => {
    const watch = watchUrls(page);
    await signIn(page, rao);
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
    await page.getByRole('link', { name: rao }).click();
    await page.waitForURL(/\/app\/consult\/[0-9a-f-]{36}$/);
    await note.getByRole('button', { name: 'Sign note' }).click();
    await page.getByRole('dialog').getByRole('button', { name: /^Sign as Dr\. Arjun Rao/ }).click();
    await expect(note).toContainText('SIGNED v1 · read-only');
    await expect(note).toContainText(/Signed by Dr\. Arjun Rao.*licence .*SYN-TCMC-\d+/);
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
  6: 'MDV-P2-017', // E-05 Rx and orders authorised separately (composers: MDV-P2-015, MDV-P2-018)
  7: 'MDV-P2-020', // E-10 print or e-Rx to the pharmacy
  8: 'MDV-P4-016', // C-02 care plan with an owner
});
