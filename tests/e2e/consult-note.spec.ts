import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';
import { encounterOf, mockNote, type NoteMock } from './support/note';

// MDV-P2-009: the consultation note editor on the consultation page, and the doctor's template choice (MDV-P2-025). CI has no API: the note and
// template endpoints are mocked in the shapes the server sends (versioning, 409 stale_note and the audit rows are ConsultNoteTests on PostgreSQL).
const doctor = {
  user: { id: 'u-rao', displayName: 'Dr. Arjun Rao (synthetic)' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role: 'SoloDoctor' }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role: 'SoloDoctor', flags: [] as string[],
    capabilities: { RegisterBookPatients: 'Full', CheckInQueue: 'Full', Patient360Chart: 'Full', ClinicalNotes: 'Sign' } },
};
const at = '2026-10-19T04:35:00Z';
// One name per signature: the doctor profile's (the licence-bearing one), never the account's display name above (u-rao is signed in as Dr. Arjun Rao).
const signer = 'Dr Asha Raman (synthetic)';

// These specs cover the guided steps (the note as one card); the one-page view (MDV-P2-041) is consult-studio.spec.ts, over the same mocks.
async function open(page: Page, seed: Parameters<typeof mockNote>[1] = {}, locale = 'en', context: typeof doctor = doctor, allergies = 'NoKnownAllergies') {
  await page.addInitScript(l => { localStorage.setItem('medvya.locale', l); localStorage.setItem('medvya.consultView', 'steps'); }, locale);
  let mock: NoteMock | null = null; // the encounter route reads the mock's identity confirmation when it is asked, after the mock exists
  await page.route('**/v1/me/context', r => r.fulfill({ json: context }));
  await page.route('**/v1/encounters/e-1', r => r.fulfill({ json: {
    encounter: encounterOf(mock),
    timeZone: 'Asia/Kolkata', treatmentConsent: true,
    patient: { id: 'p-a1', fullName: 'Meera Iyer (synthetic)', mrn: '000001', dateOfBirth: '1972-03-12', ageYears: 54, sexAtBirth: 'Female' },
    allergies: { state: allergies, allergies: [] } } }));
  const made = await mockNote(page, seed);
  mock = made;
  await page.goto('/consult/e-1');
  return made;
}
const note = (page: Page) => page.getByRole('region', { name: 'Consultation note' });

test('typing autosaves, shows the state, and names the manual path and the required sections', async ({ page }) => {
  const mock = await open(page);
  await expect(note(page)).toContainText('No recording for this visit. You can write the note manually.');
  await expect(note(page).getByText('(required)')).toHaveCount(3);
  await note(page).getByLabel('Chief complaint').fill('Thirst and tiredness (synthetic)');
  await expect(note(page).getByRole('status')).toHaveText(/^Saved \d\d:\d\d$/);
  expect(mock.puts).toEqual([{ templateKey: 'general-opd', manual: true, recordingConsent: null, baseVersion: 0, sections: { chiefComplaint: 'Thirst and tiredness (synthetic)' } }]);
  await note(page).getByLabel('Plan').fill('Review in two weeks');
  await expect.poll(() => mock.puts.length).toBe(2);
  await expect(note(page).getByRole('status')).toHaveText(/^Saved \d\d:\d\d$/);
  expect(mock.puts.at(-1)).toMatchObject({ baseVersion: 1, sections: { chiefComplaint: 'Thirst and tiredness (synthetic)', plan: 'Review in two weeks' } });
  expect(await page.evaluate(() => JSON.stringify([localStorage, sessionStorage]))).not.toContain('Thirst'); // no note text in browser storage
});

test('a failed save keeps the text on the page and retries', async ({ page }) => {
  const mock = await open(page);
  mock.next = 'offline';
  await note(page).getByLabel('Plan').fill('Rest and fluids');
  await expect(note(page).getByRole('status')).toHaveText('Not saved. Trying again.');
  await expect(note(page).getByLabel('Plan')).toHaveValue('Rest and fluids');
  await note(page).getByRole('button', { name: 'Retry now' }).click();
  await expect(note(page).getByRole('status')).toHaveText(/^Saved \d\d:\d\d$/);
  expect(mock.puts).toHaveLength(1);
  expect(mock.puts[0]!.sections).toEqual({ plan: 'Rest and fluids' });
});

test('a stale save keeps the doctor\'s text and offers reload and reapply, never an overwrite', async ({ page }) => {
  const mock = await open(page, { version: 1, sections: { plan: 'old plan' } });
  await expect(note(page).getByLabel('Plan')).toHaveValue('old plan');
  // Another device saved version 2 meanwhile.
  mock.note = { ...mock.note, version: 2, sections: { plan: 'old plan', assessment: 'Their assessment' } };
  mock.next = 'stale';
  await note(page).getByLabel('Plan').fill('My new plan');
  await expect(note(page).getByRole('alert')).toContainText('changed elsewhere');
  await expect(note(page).getByLabel('Plan')).toHaveValue('My new plan');
  expect(mock.puts).toEqual([]); // nothing was overwritten
  await note(page).getByRole('button', { name: 'Reload and reapply my changes' }).click();
  await expect(note(page).getByRole('status')).toHaveText(/^Saved \d\d:\d\d$/);
  await expect(note(page).getByLabel('Plan')).toHaveValue('My new plan');
  await expect(note(page).getByLabel('Assessment')).toHaveValue('Their assessment');
  expect(mock.puts).toEqual([{ templateKey: 'general-opd', manual: true, recordingConsent: null, baseVersion: 2, sections: { plan: 'My new plan', assessment: 'Their assessment' } }]);
});

// MDV-P1-089 (F-03): the note shows what the server holds. The save is slow here, so the doctor is back before it is answered (the hard case).
test('typing, then Problems and back at once: the note shows the new text and saves on the server\'s version', async ({ page }) => {
  const mock = await open(page, { version: 1, sections: { assessment: 'Old assessment' } });
  await page.route('**/v1/diagnosis-codes', r => r.fulfill({ json: { system: 'ICD-10', version: '2019-medvya-starter-1', codes: [] } }));
  mock.saveDelayMs = 500;
  await note(page).getByLabel('Assessment').fill('New assessment (synthetic)');
  await page.getByRole('link', { name: 'Problems and diagnoses' }).click();
  await page.getByRole('link', { name: 'Back to the consultation' }).click();
  await expect(note(page).getByLabel('Assessment')).toHaveValue('New assessment (synthetic)');
  expect(mock.puts).toHaveLength(1); // leaving saved it once
  await note(page).getByLabel('Plan').fill('Review in two weeks');
  await expect(note(page).getByRole('status')).toHaveText(/^Saved \d\d:\d\d$/);
  expect(mock.puts.at(-1)).toMatchObject({ baseVersion: 2, sections: { assessment: 'New assessment (synthetic)', plan: 'Review in two weeks' } }); // no conflict with itself
  // Each visit reads the note again: what another device saved meanwhile is shown, not this browser's earlier copy.
  await page.getByRole('link', { name: 'Problems and diagnoses' }).click();
  mock.note = { ...mock.note, version: 4, sections: { ...mock.note.sections, subjective: 'Added on another device (synthetic)' } };
  await page.getByRole('link', { name: 'Back to the consultation' }).click();
  await expect(note(page).getByLabel('Subjective')).toHaveValue('Added on another device (synthetic)');
  await expect(note(page).getByLabel('Assessment')).toHaveValue('New assessment (synthetic)');
});

// MDV-P1-089 (F-26): the page going away sends the unsent text at once. Playwright cannot intercept a keepalive request of a page that is
// unloading, so the event is raised here on a live page; the real reload inside the autosave delay is J2 step 11 on the real stack.
test('the page going away sends the unsent text at once, and no note text is in browser storage', async ({ page }) => {
  const mock = await open(page);
  await note(page).getByLabel('Plan').fill('Rest and fluids (synthetic)');
  const typedAt = Date.now();
  await page.evaluate(() => window.dispatchEvent(new Event('pagehide')));
  await expect.poll(() => mock.puts.length, { intervals: [50] }).toBe(1);
  expect(Date.now() - typedAt).toBeLessThan(1400); // not the autosave, which waits 1.5 s
  expect(mock.puts[0]!.sections).toEqual({ plan: 'Rest and fluids (synthetic)' });
  await page.waitForTimeout(1600);
  expect(mock.puts).toHaveLength(1); // and the autosave does not send it a second time
  const stored = await page.evaluate(async () => JSON.stringify([localStorage, sessionStorage, (await indexedDB.databases()).map(d => d.name)]));
  expect(stored).not.toContain('Rest');
  expect(stored).toMatch(/\[\]\]$/); // no IndexedDB database at all
});

// MDV-P1-089 AC3: the status never says Saved before the server answered.
test('a slow save says Saving… until the server answers, then Saved with the time', async ({ page }) => {
  const mock = await open(page);
  mock.saveDelayMs = 1500;
  await note(page).getByLabel('Plan').fill('Rest and fluids');
  const status = note(page).getByRole('status');
  await expect(status).toHaveText('Saving…');
  expect(mock.puts).toHaveLength(0); // not answered yet
  await expect(status).toHaveText(/^Saved \d\d:\d\d$/);
  expect(mock.puts).toHaveLength(1);
});

test('a failed save says Not saved. Trying again., and does try again by itself', async ({ page }) => {
  const mock = await open(page);
  mock.next = 'offline';
  await note(page).getByLabel('Plan').fill('Rest and fluids');
  const status = note(page).getByRole('status');
  await expect(status).toHaveText('Not saved. Trying again.');
  await expect(note(page).getByLabel('Plan')).toHaveValue('Rest and fluids');
  await expect(status).toHaveText(/^Saved \d\d:\d\d$/, { timeout: 10_000 }); // nobody pressed Retry
  expect(mock.puts).toHaveLength(1);
});

test('switching template asks first, keeps shared sections and names the text that has no place', async ({ page }) => {
  const mock = await open(page, { templateKey: 'diabetes-review', version: 1, sections: { plan: 'Diet advice', selfMonitoring: 'Fasting readings' } });
  await note(page).getByRole('combobox', { name: 'Template' }).click();
  await expect(page.getByRole('option')).toHaveText(['General OPD', 'Diabetes review']); // only the doctor's templates
  await page.getByRole('option', { name: 'General OPD' }).click();
  const dialog = page.getByRole('dialog', { name: 'Switch template?' });
  await expect(dialog).toContainText('Self-monitoring');
  await dialog.getByRole('button', { name: 'Keep current template' }).click();
  await expect(note(page).getByLabel('Self-monitoring')).toHaveValue('Fasting readings');
  expect(mock.puts).toEqual([]);
  await note(page).getByRole('combobox', { name: 'Template' }).click();
  await page.getByRole('option', { name: 'General OPD' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Switch template' }).click();
  await expect(note(page).getByRole('status')).toHaveText(/^Saved \d\d:\d\d$/);
  await expect(note(page).getByLabel('Plan')).toHaveValue('Diet advice');
  await expect(note(page).getByLabel('Self-monitoring')).toHaveCount(0);
  expect(mock.puts).toEqual([{ templateKey: 'general-opd', manual: true, recordingConsent: null, baseVersion: 1, sections: { plan: 'Diet advice' } }]);
});

test('a note that is not a draft is read-only', async ({ page }) => {
  await open(page, { version: 3, status: 'Signed', sections: { plan: 'Signed plan' } });
  await expect(note(page)).toContainText('SIGNED v1 · read-only');
  await expect(note(page).getByLabel('Plan')).toHaveJSProperty('readOnly', true);
  await expect(note(page).getByRole('combobox', { name: 'Template' })).toBeDisabled();
});

test('the doctor chooses which templates appear, and must keep at least one', async ({ page }) => {
  const mock = await open(page);
  await page.getByText('My templates', { exact: true }).click();
  const list = page.getByRole('list', { name: 'My templates' });
  await expect(list.getByRole('switch', { name: 'Diabetes review' })).toBeChecked();
  await expect(list.getByRole('switch', { name: 'Paediatrics' })).not.toBeChecked();
  await list.getByRole('switch', { name: 'Paediatrics' }).click();
  await page.getByRole('button', { name: 'Save my templates' }).click();
  await expect(page.getByText('Saved', { exact: true }).last()).toBeVisible();
  expect(mock.chosen).toEqual([['general-opd', 'diabetes-review', 'paediatrics']]);
  await list.getByRole('switch', { name: 'General OPD' }).click();
  await list.getByRole('switch', { name: 'Diabetes review' }).click();
  await list.getByRole('switch', { name: 'Paediatrics' }).click();
  await page.getByRole('button', { name: 'Save my templates' }).click();
  await expect(page.getByText('Choose at least one template.')).toBeVisible();
  expect(mock.chosen).toHaveLength(1);
});

// MDV-P2-012: sign and finalise. The 403 reauth_required / 409 / 400 answers are mocked in the shapes the server sends (ConsultNoteTests prove the rules).
test('sign: confirms what signing means, saves first, then shows the signed note read-only', async ({ page }) => {
  const mock = await open(page, { version: 2, sections: { chiefComplaint: 'Thirst', assessment: 'Review', plan: 'Rest' }, recordingConsent: 'Declined' });
  mock.reauthed = true;
  await note(page).getByLabel('Plan').fill('Rest and fluids'); // typed just before signing: saved first, then that version is signed
  await note(page).getByRole('button', { name: 'Sign note' }).click();
  const dialog = page.getByRole('dialog', { name: 'Sign note for Meera Iyer (synthetic)?' });
  await expect(dialog).toContainText(/000001.* · 12 Mar 1972/); // who it is for, so a signature cannot land on the wrong chart
  await expect(dialog).not.toContainText('encounter'); // UX-07: no internal visit id in the dialog
  await expect(dialog).toContainText('I have reviewed every section');
  await expect(dialog).toContainText('permanent and read-only');
  await expect(dialog).toContainText('addenda');
  await expect(dialog).toContainText('Prescriptions and orders are authorised separately.');
  await expect(dialog).toContainText('confirm your identity again');
  await dialog.getByRole('button', { name: 'Keep editing' }).click();
  expect(mock.signs).toEqual([]);
  await note(page).getByRole('button', { name: 'Sign note' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Sign as Dr Asha Raman (synthetic)' }).click();
  await expect(note(page)).toContainText('SIGNED v1 · read-only');
  await expect(note(page)).toContainText(/Signed by Dr Asha Raman \(synthetic\), licence .*Tamil Nadu Medical Council 84213.*, on 19 Oct 2026 at 10:10 · version 1/); // the licence the owner attested
  expect(mock.puts.at(-1)).toMatchObject({ baseVersion: 2, sections: { plan: 'Rest and fluids' } });
  expect(mock.signs).toEqual([{ baseVersion: 3 }]);
  await expect(note(page).getByLabel('Plan')).toHaveJSProperty('readOnly', true);
  await expect(note(page).getByRole('combobox', { name: 'Template' })).toBeDisabled();
  await expect(note(page).getByRole('button', { name: 'Sign note' })).toHaveCount(0);
});

test('sign: blank required sections keep Sign locked and are named in the checklist; the server\'s refusal is named and focused', async ({ page }) => {
  const mock = await open(page, { version: 1, sections: { assessment: 'Review' }, recordingConsent: 'Declined' });
  mock.reauthed = true;
  await expect(note(page).getByRole('button', { name: 'Sign note' })).toBeDisabled();
  await expect(note(page).getByRole('button', { name: 'Sign note' })).toHaveAccessibleDescription('2 items still need your review before signing');
  const list = note(page).getByRole('region', { name: 'Review and sign' }).getByRole('listitem');
  await expect(list.filter({ hasText: 'History reviewed' })).toContainText('To do');
  await expect(list.filter({ hasText: 'Plan reviewed' })).toContainText('To do');
  await expect(list.filter({ hasText: 'Diagnoses reviewed' })).toContainText('Done');
  expect(mock.signs).toEqual([]);
  // The server's answer is trusted too (another tab cleared a section): same words, same focus.
  await note(page).getByLabel('Chief complaint').fill('Thirst');
  await note(page).getByLabel('Plan').fill('Rest');
  await expect(note(page).getByText('Everything reviewed. Ready to sign.')).toBeVisible();
  mock.signNext = 'blank';
  await note(page).getByRole('button', { name: 'Sign note' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Sign as Dr Asha Raman (synthetic)' }).click();
  await expect(note(page).getByRole('alert')).toHaveText('These required sections are empty: Chief complaint, Plan. Fill them in, then sign.');
  await expect(note(page).getByLabel('Chief complaint')).toBeFocused();
});

// MDV audit P0-2: allergies not recorded lock Sign, and the sign message says so; it never reads "ready" beside them.
test('checklist: allergies not recorded keep Sign locked', async ({ page }) => {
  await open(page, { version: 1, sections: { chiefComplaint: 'Thirst', assessment: 'Review', plan: 'Rest' }, recordingConsent: 'Declined' }, 'en', doctor, 'NotRecorded');
  const sign = note(page).getByRole('button', { name: 'Sign note' });
  await expect(note(page).getByRole('region', { name: 'Review and sign' }).getByRole('listitem').filter({ hasText: 'Allergies reconciled' })).toHaveText('Allergies reconciledTo do');
  await expect(sign).toBeDisabled();
  await expect(sign).toHaveAccessibleDescription('1 item still needs your review before signing');
  // The guided steps have the same entry above the note, so the doctor is never locked out without a way to record them.
  await page.getByRole('region', { name: 'Vitals' }).getByRole('button', { name: 'Confirm: no known allergies' }).click();
  await expect(page.getByRole('region', { name: 'Vitals' })).toContainText('No known allergies, confirmed at this visit.');
});

// MDV audit UX-11: "Diagnoses reviewed" is Done only with a diagnosis on the problem list; an assessment without one is said, and does not lock Sign.
test('checklist: an assessment with no diagnosis on the problem list is not Done', async ({ page }) => {
  const mock = await open(page, { version: 1, sections: { chiefComplaint: 'Thirst', assessment: 'Review', plan: 'Rest' }, recordingConsent: 'Declined' });
  mock.problems = [];
  await page.reload();
  const item = note(page).getByRole('region', { name: 'Review and sign' }).getByRole('listitem').filter({ hasText: 'Diagnoses reviewed' });
  await expect(item).toHaveText('Diagnoses reviewedNo diagnosis on the problem list');
  await expect(note(page).getByRole('button', { name: 'Sign note' })).toBeEnabled();
});

// MDV-P2-044: the checklist is worked out from the note, the banner and the reconciliation; the server holds the consent and AI gates as well.
test('checklist: no recording consent step or row until recording exists, and Sign needs none; each item is in words', async ({ page }) => {
  const mock = await open(page, { version: 1, sections: { chiefComplaint: 'Thirst', assessment: 'Review', plan: 'Rest' } });
  mock.reauthed = true;
  const sign = note(page).getByRole('button', { name: 'Sign note' });
  await expect(sign).toBeEnabled();
  await expect(sign).toHaveAccessibleDescription('Everything reviewed. Ready to sign.');
  const checklist = note(page).getByRole('region', { name: 'Review and sign' });
  await expect(checklist.getByRole('listitem')).toHaveText([
    'Confirmed with the patient (name and date of birth)Done', 'Allergies reconciledDone', 'Medicines reconciledDone',
    'History reviewedDone', 'Diagnoses reviewedDone', 'Plan reviewedDone', // no AI line while there is no AI drafting (MDV-P1-091)
  ]);
  await expect(note(page).getByRole('group', { name: 'Recording consent for this visit' })).toHaveCount(0); // MDV-AUDIT-1092
  await sign.click();
  await page.getByRole('dialog').getByRole('button', { name: 'Sign as Dr Asha Raman (synthetic)' }).click();
  await expect(note(page)).toContainText('SIGNED v1 · read-only');
  await expect(note(page).getByRole('radio')).toHaveCount(0);
  await expect(note(page)).not.toContainText('Recording consent for this visit');
});

test('checklist: an undecided AI draft and an unconfirmed reconciliation lock Sign', async ({ page }) => {
  const mock = await open(page, { version: 1, sections: { chiefComplaint: 'Thirst', assessment: 'Review', plan: 'Rest' }, recordingConsent: 'Declined', undecidedAi: ['subjective'] });
  mock.reconciliation = { confirmedAt: null, total: 2 };
  await page.reload();
  const sign = note(page).getByRole('button', { name: 'Sign note' });
  await expect(sign).toHaveAccessibleDescription('3 items still need your review before signing');
  const list = note(page).getByRole('region', { name: 'Review and sign' }).getByRole('listitem');
  for (const item of ['Medicines reconciled', 'History reviewed', 'Every AI suggestion accepted or dismissed']) await expect(list.filter({ hasText: item })).toContainText('To do');
  await expect(sign).toBeDisabled();
  expect(mock.signs).toEqual([]);
});

test('checklist: the server refusing an unreviewed note is said in words and the note stays a draft', async ({ page }) => {
  const mock = await open(page, { version: 1, sections: { chiefComplaint: 'Thirst', assessment: 'Review', plan: 'Rest' }, recordingConsent: 'Declined' });
  mock.reauthed = true;
  mock.signNext = 'unreviewed'; // another device holds an AI draft this page has not loaded yet
  await note(page).getByRole('button', { name: 'Sign note' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Sign as Dr Asha Raman (synthetic)' }).click();
  await expect(note(page).getByRole('alert')).toHaveText('The note was not signed: some checklist items still need your review.');
  await expect(note(page).getByLabel('Plan')).toBeEditable();
});

test('sign: reauth_required sends the doctor to confirm identity, the draft survives, and signing works on return', async ({ page }) => {
  const mock = await open(page, { version: 1, sections: { chiefComplaint: 'Thirst', assessment: 'Review', plan: 'Rest' }, recordingConsent: 'Declined' });
  await note(page).getByLabel('Plan').fill('Rest and fluids (unsaved when Sign was pressed)');
  await note(page).getByRole('button', { name: 'Sign note' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Sign as Dr Asha Raman (synthetic)' }).click();
  await expect(page.getByText('Confirm your identity', { exact: true })).toBeVisible(); // the provider's stub page
  expect(mock.reauths).toEqual([{ returnUrl: '/consult/e-1?resume=sign' }]); // the opaque consult path and one fixed word (MDV-P2-047)
  expect(mock.signs).toHaveLength(1);
  expect(mock.note.sections.plan).toBe('Rest and fluids (unsaved when Sign was pressed)'); // saved before the redirect
  await page.goto('/consult/e-1?resume=sign'); // the provider returns here
  await expect(note(page).getByLabel('Plan')).toHaveValue('Rest and fluids (unsaved when Sign was pressed)'); // reloaded from the server
  // MDV-P2-047: Sign is in view and focused and the status says what to press; signing still waits for the press.
  await expect(note(page).getByRole('button', { name: 'Sign note' })).toBeFocused();
  await expect(note(page).getByRole('button', { name: 'Sign note' })).toBeInViewport();
  await expect(note(page).getByRole('status').filter({ hasText: 'Identity confirmed. Press Sign note.' })).toBeVisible();
  expect(mock.signs).toHaveLength(1);
  await note(page).getByRole('button', { name: 'Sign note' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Sign as Dr Asha Raman (synthetic)' }).click();
  await expect(note(page)).toContainText('version 1');
  expect(mock.signs).toHaveLength(2);
  expect(await page.evaluate(() => JSON.stringify([localStorage, sessionStorage]))).not.toContain('Rest and fluids');
});

test('sign: a stale note keeps the doctor\'s text and uses the editor\'s reload and reapply', async ({ page }) => {
  const mock = await open(page, { version: 1, sections: { chiefComplaint: 'Thirst', assessment: 'Review', plan: 'Rest' }, recordingConsent: 'Declined' });
  mock.reauthed = true;
  mock.signNext = 'stale';
  await expect(note(page).getByLabel('Plan')).toHaveValue('Rest');
  mock.note = { ...mock.note, version: 2, sections: { ...mock.note.sections, plan: 'Their plan' } }; // another device saved meanwhile
  await note(page).getByRole('button', { name: 'Sign note' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Sign as Dr Asha Raman (synthetic)' }).click();
  await expect(note(page).getByRole('alert')).toContainText('changed elsewhere');
  await expect(note(page).getByLabel('Plan')).toHaveValue('Rest');
  await note(page).getByRole('button', { name: 'Reload and reapply my changes' }).click();
  await expect(note(page).getByRole('status').filter({ hasText: 'Saved' })).toBeVisible();
  await expect(note(page).getByLabel('Plan')).toHaveValue('Their plan'); // the doctor changed nothing here, so theirs shows; nothing is signed unseen
  expect(mock.note.status).toBe('Draft');
});

test('sign: a refusal is said in words and the note stays a draft', async ({ page }) => {
  const mock = await open(page, { version: 1, sections: { chiefComplaint: 'Thirst', assessment: 'Review', plan: 'Rest' }, recordingConsent: 'Declined' });
  mock.reauthed = true;
  mock.signNext = 'denied';
  await note(page).getByRole('button', { name: 'Sign note' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Sign as Dr Asha Raman (synthetic)' }).click();
  await expect(note(page).getByRole('alert')).toContainText('You cannot sign this note.');
  await expect(note(page).getByLabel('Plan')).toBeEditable();
});

test('a signed note shows its signature and no way to edit or sign again', async ({ page }) => {
  await open(page, { version: 3, status: 'Signed', sections: { plan: 'Signed plan' },
    signature: { versionNo: 1, signedBy: 'u-other', signedAt: at, contentHash: 'b'.repeat(64) } });
  await expect(note(page)).toContainText('Signed by another clinician on 19 Oct 2026 at 10:05 · version 1');
  await expect(note(page).getByLabel('Plan')).toHaveJSProperty('readOnly', true);
  await expect(note(page).getByRole('button', { name: 'Sign note' })).toHaveCount(0);
});

// MDV-P2-013: addenda, version history and the print view. The server rules (v1 never altered, reason and step-up, PDF with identifiers on every
// page, stored with its hash) are SignNoteTests on PostgreSQL; here the screen shows them and sends the right calls.
const signedSeed = { version: 3, status: 'Signed', sections: { chiefComplaint: 'Thirst', assessment: 'Review', plan: 'Metformin 500 mg' },
  signature: { versionNo: 1, signedBy: 'u-rao', signedAt: at, contentHash: 'b'.repeat(64), signerName: signer } };
const history = (page: Page) => page.getByRole('region', { name: 'Version history' });

test('addendum: a reason is required, it is signed as version 2, and the original stays as it was', async ({ page }) => {
  const mock = await open(page, signedSeed);
  mock.reauthed = true;
  await expect(history(page)).toContainText('Original note');
  await expect(history(page)).toContainText('Version 1 · signed by Dr Asha Raman (synthetic) on 19 Oct 2026 at 10:05');
  await expect(history(page)).toContainText('Corrections are added as signed addenda.');
  await history(page).getByRole('button', { name: 'Add addendum' }).click();
  await history(page).getByLabel('Addendum', { exact: true }).fill('Synthetic: metformin dose is 1000 mg, not 500 mg');
  await history(page).getByRole('button', { name: 'Sign addendum' }).click();
  await expect(history(page).getByRole('alert')).toHaveText('Write the addendum and its reason, then sign.');
  expect(mock.addendumPosts).toEqual([]);
  await history(page).getByLabel('Reason for the addendum').fill('Dose typed wrongly');
  await history(page).getByRole('button', { name: 'Sign addendum' }).click();
  await expect(history(page)).toContainText('Addendum to version 1');
  await expect(history(page)).toContainText('Version 2 · signed by Dr Asha Raman (synthetic) on 19 Oct 2026 at 10:30');
  await expect(history(page)).toContainText('Reason: Dose typed wrongly');
  await expect(history(page).getByLabel('Addendum', { exact: true })).toHaveCount(0); // the composer closes
  expect(mock.addendumPosts).toEqual([{ text: 'Synthetic: metformin dose is 1000 mg, not 500 mg', reason: 'Dose typed wrongly' }]);
  await expect(note(page).getByLabel('Plan')).toHaveValue('Metformin 500 mg'); // v1 as signed
  await expect(note(page).getByLabel('Plan')).toHaveJSProperty('readOnly', true);
});

test('addendum: a step-up refusal keeps the text on screen until the doctor chooses to confirm identity', async ({ page }) => {
  const mock = await open(page, signedSeed);
  await history(page).getByRole('button', { name: 'Add addendum' }).click();
  await history(page).getByLabel('Addendum', { exact: true }).fill('Synthetic correction');
  await history(page).getByLabel('Reason for the addendum').fill('Synthetic reason');
  await history(page).getByRole('button', { name: 'Sign addendum' }).click();
  await expect(history(page).getByRole('alert')).toContainText('confirm your identity again. Copy your text first');
  await expect(history(page).getByLabel('Addendum', { exact: true })).toHaveValue('Synthetic correction');
  expect(mock.reauths).toEqual([]); // nothing leaves the page until the doctor says so
  await history(page).getByRole('button', { name: 'Confirm identity' }).click();
  await expect(page.getByText('Confirm your identity', { exact: true })).toBeVisible();
  expect(mock.reauths).toEqual([{ returnUrl: '/consult/e-1' }]);
  expect(await page.evaluate(() => JSON.stringify([localStorage, sessionStorage]))).not.toContain('Synthetic correction');
});

test('a role without Sign sees the history and the print view, but no addendum', async ({ page }) => {
  const nurse = { ...doctor, active: { ...doctor.active, capabilities: { ...doctor.active.capabilities, ClinicalNotes: 'Full' } } };
  await open(page, signedSeed, 'en', nurse);
  await expect(history(page)).toContainText('Original note');
  await expect(history(page).getByRole('button', { name: 'Add addendum' })).toHaveCount(0);
  await expect(history(page).getByRole('link', { name: 'Print view and PDF' })).toBeVisible();
});

const withAddendum = { ...signedSeed, addenda: [{ versionNo: 2, amendsVersionNo: 1, reason: 'Dose typed wrongly', text: 'Synthetic: metformin 1000 mg', signedBy: 'u-rao', signedAt: at, contentHash: 'c'.repeat(64), signerName: signer }] };

test('print view: the original then each addendum in order, with identifiers; the PDF is the server\'s', async ({ page }) => {
  const mock = await open(page, withAddendum);
  await history(page).getByRole('link', { name: 'Print view and PDF' }).click();
  await expect(page).toHaveURL(/\/consult\/e-1\/note\/print$/);
  const article = page.getByRole('article', { name: 'Signed consultation note' });
  await expect(article).toContainText('Meera Iyer (synthetic)');
  await expect(article).toContainText('000001');
  await expect(article).toContainText('12 Mar 1972');
  await expect(article.getByRole('heading', { level: 2 })).toHaveText(['Chief complaint', 'Assessment', 'Plan', 'Version history']);
  await expect(article.getByRole('listitem')).toHaveCount(2);
  await expect(article.getByRole('listitem').first()).toContainText(`signed by ${signer}`); // the same name as the banner and the sign button, not the account's
  await expect(article.getByRole('listitem').last()).toContainText(`signed by ${signer}`);
  await expect(article).not.toContainText('Arjun');
  await expect(article.getByRole('listitem').last()).toContainText('Addendum to version 1');
  await expect(article).toContainText('Corrections are added as signed addenda.');
  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Download PDF' }).click();
  expect((await download).suggestedFilename()).toBe('consultation-note.pdf'); // no patient data in the file name
  mock.pdfNext = 'unsupported';
  await page.getByRole('button', { name: 'Download PDF' }).click();
  await expect(page.getByRole('alert')).toHaveText('This note has text the PDF cannot show yet. Use Print instead.');
  expect(mock.pdfs).toBe(2);
});

test('a role without Sign sees no Sign button, only why', async ({ page }) => {
  const nurse = { ...doctor, active: { ...doctor.active, capabilities: { ...doctor.active.capabilities, ClinicalNotes: 'Full' } } };
  await open(page, { version: 1, sections: { plan: 'x' } }, 'en', nurse);
  await expect(note(page)).toContainText('Only a doctor can sign this note.');
  await expect(note(page).getByRole('button', { name: 'Sign note' })).toHaveCount(0);
});

// MDV-P2-009 (J2.5, E-04): verified prefill, quick phrases and earlier-note snippets. Nothing is added until the doctor presses a button, and
// every added line says where it came from. Which readings are verified is decided on the server (ConsultNoteAssistTests, mutation-checked).
const nurse = 'Anita Nair (synthetic)';
const verified = [
  { code: 'BloodPressureSystolic', value: 150, unit: 'mmHg', takenAt: at, source: 'ClinicianVerified', recordedByName: nurse, flag: 'High' },
  { code: 'Pulse', value: 88, unit: '/min', takenAt: at, source: 'ClinicianVerified', recordedByName: null, flag: null },
];
const earlier = [{ encounterId: 'e-0', signedAt: '2026-09-02T05:30:00Z', signedByName: 'Dr. Arjun Rao (synthetic)', templateKey: 'general-opd', sections: { chiefComplaint: 'Thirst (synthetic)', plan: 'Review in a month' } }];

test('J2.5 (note assist, MDV-P2-009) E-04 · Doctor: verified vitals are added to Objective only on request, each with its source', async ({ page }) => {
  const mock = await open(page, { version: 1, sections: { plan: 'x' } });
  mock.vitals = verified;
  await page.reload();
  const assist = note(page).getByRole('region', { name: 'Add to the note' });
  await expect(assist.getByRole('listitem')).toHaveText([
    'Blood pressure, systolic 150 mmHg (Anita Nair (synthetic), nurse intake, verified 19 Oct 2026 10:05) Flagged high by the nurse', // MDV-1090: words, not colour; the icon is decoration
    'Pulse 88 /min (nurse intake, verified 19 Oct 2026 10:05)', // no name known: the line says so by leaving it out, never guesses one
  ]);
  await expect(note(page).getByLabel('Objective')).toHaveValue(''); // shown, not added
  expect(mock.puts).toEqual([]);
  await assist.getByRole('button', { name: 'Add to Objective' }).click();
  await expect(note(page).getByRole('status')).toHaveText(/^Saved \d\d:\d\d$/);
  await expect(note(page).getByLabel('Objective')).toHaveValue(
    'Blood pressure, systolic 150 mmHg (Anita Nair (synthetic), nurse intake, verified 19 Oct 2026 10:05)\nPulse 88 /min (nurse intake, verified 19 Oct 2026 10:05)');
  expect(mock.puts.at(-1)!.sections.objective).toContain('nurse intake, verified');
});

test('J2.5 (note assist, MDV-P2-009) E-04 · Doctor: with nothing verified, Objective stays blank and says so', async ({ page }) => {
  await open(page);
  const assist = note(page).getByRole('region', { name: 'Add to the note' });
  await expect(assist).toContainText('No verified vitals for this visit. Objective stays blank until you write it.');
  await expect(assist.getByRole('button', { name: 'Add to Objective' })).toHaveCount(0);
  await expect(note(page).getByLabel('Objective')).toHaveValue('');
});

test('J2.5 (note assist, MDV-P2-009) E-04 · Doctor: a quick phrase and an earlier signed snippet go in at the cursor of the section being edited', async ({ page }) => {
  const mock = await open(page);
  mock.previous = earlier;
  await page.reload();
  const assist = note(page).getByRole('region', { name: 'Add to the note' });
  const plan = note(page).getByRole('textbox', { name: /^Plan/ });
  await plan.fill('Start. End.');
  await plan.evaluate((el: HTMLTextAreaElement) => { el.focus(); el.setSelectionRange(6, 6); });
  await assist.getByRole('group', { name: 'Quick phrases' }).getByRole('button', { name: 'Advised fluids and rest.' }).click();
  await expect(plan).toHaveValue('Start.\nAdvised fluids and rest. End.');
  await expect(assist).toContainText('Phrases and snippets go into: Plan');
  await assist.getByText('02 Sept 2026, signed by Dr. Arjun Rao (synthetic)').click();
  await assist.getByRole('button', { name: 'Insert Chief complaint text from 02 Sept 2026' }).click(); // goes to the section last edited, marked with its source
  await expect(plan).toHaveValue('Start.\nAdvised fluids and rest.\nThirst (synthetic) (copied from the note of 02 Sept 2026) End.');
  await expect(note(page).getByRole('status')).toHaveText(/^Saved \d\d:\d\d$/);
  expect(mock.puts.at(-1)!.sections.plan).toContain('copied from the note of 02 Sept 2026');
});

// MDV-P1-104 (J3 step 5 → J2): the nurse's chief complaint in the doctor's note. Wording awaits the pilot doctor (validation plan question 8).
const fromNurse = { text: 'Fever and sore throat for three days (synthetic)', byName: nurse, at };

test('MDV-P1-104 E-04 · Doctor: a blank Chief complaint holds the nurse\'s text, marked from nurse intake, and says edited once the doctor changes it', async ({ page }) => {
  const mock = await open(page);
  mock.chiefComplaint = fromNurse;
  await page.reload();
  const complaint = note(page).getByRole('textbox', { name: /^Chief complaint/ });
  await expect(complaint).toHaveValue(fromNurse.text);
  await expect(complaint).toHaveAccessibleDescription(`From nurse intake Saved by ${nurse} · 19 Oct 2026 10:05`); // a screen reader hears the source on focus
  await expect(note(page).getByRole('status')).toHaveText(/^Saved \d\d:\d\d$/); // saved like typing, so the server records the source
  expect(mock.puts.at(-1)!.sections.chiefComplaint).toBe(fromNurse.text);
  expect(mock.note.chiefComplaintSource?.source).toBe('NurseIntake');
  await complaint.fill(`${fromNurse.text}, worse at night`);
  await expect(complaint).toHaveAccessibleDescription(/^From nurse intake · edited by the doctor/);
  await expect.poll(() => mock.note.chiefComplaintSource?.source).toBe('EditedByDoctor');
  await expect(note(page).getByRole('status')).toHaveText(/^Saved \d\d:\d\d$/);
  await expect(complaint).toHaveAccessibleDescription(/^From nurse intake · edited by the doctor/); // the server's answer keeps the mark
});

test('MDV-P1-104 E-04 · Doctor: a complaint the doctor already typed is never replaced or marked', async ({ page }) => {
  const mock = await open(page, { version: 1, sections: { chiefComplaint: 'Cough since Monday (doctor, synthetic)' } });
  mock.chiefComplaint = fromNurse;
  await page.reload();
  await expect(note(page).getByRole('textbox', { name: /^Chief complaint/ })).toHaveValue('Cough since Monday (doctor, synthetic)');
  await expect(note(page).getByText('From nurse intake')).toHaveCount(0);
  expect(mock.puts).toEqual([]);
});

test('MDV-P1-104 E-04 · Doctor: no complaint from the nurse (or nurse intake off) leaves Chief complaint blank, nothing guessed', async ({ page }) => {
  const mock = await open(page);
  await expect(note(page).getByRole('region', { name: 'Add to the note' })).toBeVisible(); // the offer has been read
  await expect(note(page).getByRole('textbox', { name: /^Chief complaint/ })).toHaveValue('');
  await expect(note(page).getByText('From nurse intake')).toHaveCount(0);
  expect(mock.puts).toEqual([]);
});

test('MDV-P1-104 E-04 · Doctor: the signed note shows the source it was signed with', async ({ page }) => {
  await open(page, { version: 3, status: 'Signed', sections: { chiefComplaint: `${fromNurse.text}, worse`, plan: 'x' }, chiefComplaintSource: { source: 'EditedByDoctor', byName: nurse, at } });
  await expect(note(page).getByText('From nurse intake · edited by the doctor')).toBeVisible();
});

test('a signed note offers nothing to add', async ({ page }) => {
  await open(page, { version: 2, status: 'Signed', sections: { plan: 'x' } });
  await expect(note(page).getByRole('region', { name: 'Add to the note' })).toHaveCount(0);
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
for (const locale of ['en', 'ar'])
  test(`axe finds no violations on the add-to-note panel (${locale}); targets are 44 px`, async ({ page }) => {
    const mock = await open(page, { sections: { plan: 'x' }, version: 1 }, locale);
    mock.vitals = verified;
    mock.previous = earlier;
    await page.reload();
    const assist = page.getByRole('region', { name: locale === 'ar' ? 'ملاحظة الاستشارة' : 'Consultation note' }).getByRole('region', { name: locale === 'ar' ? 'الإضافة إلى الملاحظة' : 'Add to the note' });
    await assist.locator('summary').click();
    if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    for (const target of await assist.locator('button, summary').all()) expect((await target.boundingBox())!.height).toBeGreaterThanOrEqual(44);
    await page.addScriptTag({ path: axePath });
    const violations = await page.evaluate(async () =>
      (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
        .map(v => `${v.id} (${v.nodes.length})`));
    expect(violations).toEqual([]);
  });

for (const locale of ['en', 'ar'])
  test(`axe finds no violations on the note (${locale}); targets are 44 px`, async ({ page }) => {
    const mock = await open(page, { sections: { plan: 'x' }, version: 1 }, locale);
    mock.chiefComplaint = fromNurse; // MDV-P1-104: with the nurse-intake mark on Chief complaint
    await page.reload();
    await expect(page.locator('#note-chiefComplaint-source')).toContainText(locale === 'ar' ? 'من استقبال التمريض' : 'From nurse intake');
    await page.getByText(locale === 'ar' ? 'قوالبي' : 'My templates', { exact: true }).click();
    await expect(page.getByRole('region', { name: locale === 'ar' ? 'المراجعة والتوقيع' : 'Review and sign' })).toBeVisible(); // the checklist with items to do
    await expect(page.locator('textarea').first()).toBeVisible();
    if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    for (const target of await page.getByRole('main').locator('button:not([role=switch]), [role=combobox], label:has([role=switch]), textarea, summary').all()) expect((await target.boundingBox())!.height).toBeGreaterThanOrEqual(44);
    await page.addScriptTag({ path: axePath });
    const violations = await page.evaluate(async () =>
      (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
        .map(v => `${v.id} (${v.nodes.length})`));
    expect(violations).toEqual([]);
  });

const axeViolations = async (page: Page) => {
  await page.addScriptTag({ path: axePath });
  return page.evaluate(async () =>
    (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
      .map(v => `${v.id} (${v.nodes.length})`));
};

// MDV-P1-089 AC7: saving, saved and not saved are one polite status (the same element, so each change is read out), axe clean in both languages.
for (const locale of ['en', 'ar'])
  test(`the save status is one polite status element, axe clean while saved and while not saved (${locale})`, async ({ page }) => {
    const mock = await open(page, {}, locale);
    const status = page.getByRole('region', { name: locale === 'ar' ? 'ملاحظة الاستشارة' : 'Consultation note' }).getByRole('status');
    await expect(status).toHaveCount(1);
    await status.evaluate(el => { (el as HTMLElement).dataset.same = 'yes'; });
    await page.locator('#note-plan').fill('Rest and fluids');
    await expect(status).toHaveText(locale === 'ar' ? /^تم الحفظ \d\d:\d\d$/ : /^Saved \d\d:\d\d$/);
    expect(await axeViolations(page)).toEqual([]);
    mock.next = 'offline';
    await page.locator('#note-plan').fill('Rest and fluids, review');
    await expect(status).toHaveText(locale === 'ar' ? 'لم يتم الحفظ. نحاول مرة أخرى.' : 'Not saved. Trying again.');
    await expect(status).toHaveAttribute('data-same', 'yes'); // not a new element: a screen reader hears the change
    await expect(page.getByRole('alert')).toHaveCount(0); // polite, not an interruption
    expect(await axeViolations(page)).toEqual([]);
  });
for (const locale of ['en', 'ar'])
  test(`axe finds no violations on the signed note with the addendum composer and on the print view at 200% zoom (${locale})`, async ({ page }) => {
    await open(page, withAddendum, locale);
    if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    const versions = page.getByRole('region', { name: locale === 'ar' ? 'سجل النسخ' : 'Version history' });
    await versions.getByRole('button', { name: locale === 'ar' ? 'إضافة ملحق' : 'Add addendum' }).click();
    await versions.getByRole('button', { name: locale === 'ar' ? 'توقيع الملحق' : 'Sign addendum' }).click(); // the alert in words
    for (const target of await versions.locator('button, a, input, textarea').all()) expect((await target.boundingBox())!.height).toBeGreaterThanOrEqual(44);
    expect(await axeViolations(page)).toEqual([]);

    // WCAG 1.4.10: 200% zoom on a 1280 px screen leaves 640 CSS px; the print view reflows with no sideways scrolling.
    await page.setViewportSize({ width: 640, height: 800 });
    await versions.getByRole('link').click();
    await expect(page.getByRole('article')).toContainText('000001');
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    expect(await axeViolations(page)).toEqual([]);
  });

// MDV-P2-009 (4/4): the editor by keyboard only and at 200% zoom. No mouse call below: every control is reached with Tab and worked with Enter,
// Space, arrows and Escape, the focus ring is visible each time, and a dialog holds focus and gives it back.
const focused = (page: Page) => page.evaluate(() => {
  const el = document.activeElement as HTMLElement;
  const s = getComputedStyle(el);
  return { tag: `${el.tagName} ${el.textContent?.trim().slice(0, 30)}`, ring: (s.outlineStyle !== 'none' && parseFloat(s.outlineWidth) > 0) || s.boxShadow !== 'none' };
});
async function tabTo(page: Page, target: ReturnType<Page['locator']>, key = 'Tab', max = 60) {
  for (let i = 0; i < max; i++) {
    if (await target.evaluate(el => el === document.activeElement)) return;
    await page.keyboard.press(key);
    const f = await focused(page);
    expect(f.ring, `focus ring on step ${i}: ${f.tag}`).toBe(true);
  }
  throw new Error('not reachable by Tab');
}

test('keyboard only: write, add a phrase, switch template, and sign, with a visible focus ring, a focus trap and focus returned', async ({ page }) => {
  const mock = await open(page, { version: 1, templateKey: 'diabetes-review', sections: { chiefComplaint: 'Thirst', assessment: 'Review', plan: 'Rest', selfMonitoring: 'Fasting readings' }, recordingConsent: 'Declined' });
  mock.reauthed = true;
  mock.previous = earlier;
  await page.reload();
  const region = note(page);
  await expect(region.getByRole('region', { name: 'Add to the note' })).toContainText('02 Sept 2026'); // everything has loaded: nothing re-renders under the keyboard

  const plan = region.getByRole('textbox', { name: /^Plan/ });
  await tabTo(page, plan, 'Tab', 120); // diabetes review 2 asks 8 symptoms and 6 systems on the way (MDV-P2-042); every stop still shows its ring
  await page.keyboard.type(' and fluids');
  await expect(region.getByRole('status')).toHaveText(/^Saved \d\d:\d\d$/);
  expect(mock.puts.at(-1)!.sections.plan).toBe('Rest and fluids');

  const snippets = region.getByRole('region', { name: 'Add to the note' }).locator('summary');
  await tabTo(page, snippets, 'Shift+Tab'); // the add-to-note panel sits above the sections
  await page.keyboard.press('Enter');
  await tabTo(page, region.getByRole('button', { name: /^Insert .* text from 02 Sept 2026$/ }).first());
  await page.keyboard.press('Space');
  await expect.poll(() => mock.puts.at(-1)!.sections.chiefComplaint).toContain('copied from the note of 02 Sept 2026'); // into the section focus last passed through

  // Template switch: arrows choose, the dialog traps Tab, Escape returns focus to the template control.
  const template = region.getByRole('combobox', { name: 'Template' });
  await tabTo(page, template, 'Shift+Tab');
  await page.keyboard.press('Enter');
  await expect(page.getByRole('option', { name: 'General OPD' })).toBeVisible();
  await page.keyboard.press('ArrowUp');
  await expect(page.getByRole('option', { name: 'General OPD' })).toBeFocused();
  await page.keyboard.press('Enter');
  const switchDialog = page.getByRole('dialog', { name: 'Switch template?' });
  await expect(switchDialog).toBeVisible();
  for (let i = 0; i < 6; i++) {
    await page.keyboard.press('Tab');
    expect(await switchDialog.evaluate(d => d.contains(document.activeElement))).toBe(true);
  }
  await page.keyboard.press('Escape');
  await expect(switchDialog).toHaveCount(0);
  await expect(template).toBeFocused();

  // Sign: the dialog names who it is for, Tab and Shift+Tab stay inside it, Escape returns to the Sign button, Enter on the CTA signs.
  const sign = region.getByRole('button', { name: 'Sign note' });
  await tabTo(page, sign);
  await page.keyboard.press('Enter');
  const dialog = page.getByRole('dialog', { name: 'Sign note for Meera Iyer (synthetic)?' });
  await expect(dialog).toBeVisible();
  for (let i = 0; i < 6; i++) {
    await page.keyboard.press(i % 2 ? 'Shift+Tab' : 'Tab');
    expect(await dialog.evaluate(d => d.contains(document.activeElement))).toBe(true);
    expect((await focused(page)).ring).toBe(true);
  }
  await page.keyboard.press('Escape');
  await expect(dialog).toHaveCount(0);
  await expect(sign).toBeFocused();
  expect(mock.signs).toEqual([]);
  await page.keyboard.press('Enter');
  await tabTo(page, dialog.getByRole('button', { name: 'Sign as Dr Asha Raman (synthetic)' }));
  await page.keyboard.press('Enter');
  await expect(region).toContainText('SIGNED v1 · read-only');
  expect(mock.signs).toHaveLength(1);
});

test('200% zoom: the note, the sign dialog and its buttons stay readable and reachable with no sideways scrolling', async ({ page }) => {
  await open(page, { version: 1, sections: { chiefComplaint: 'Thirst', assessment: 'Review', plan: 'Rest' }, recordingConsent: 'Declined' });
  await page.setViewportSize({ width: 640, height: 360 }); // 1280 x 720 at 200%: WCAG 1.4.4 and 1.4.10
  await expect(note(page)).toBeVisible();
  const fits = () => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth);
  expect(await fits()).toBe(true);
  const sign = note(page).getByRole('button', { name: 'Sign note' });
  await sign.scrollIntoViewIfNeeded();
  await sign.click();
  const dialog = page.getByRole('dialog');
  const confirm = dialog.getByRole('button', { name: 'Sign as Dr Asha Raman (synthetic)' });
  await confirm.scrollIntoViewIfNeeded(); // a tall dialog scrolls inside the viewport; nothing is cut off
  const box = (await dialog.boundingBox())!;
  expect(box.x).toBeGreaterThanOrEqual(0);
  expect(box.x + box.width).toBeLessThanOrEqual(640);
  const confirmBox = (await confirm.boundingBox())!;
  expect(confirmBox.x).toBeGreaterThanOrEqual(0);
  expect(confirmBox.x + confirmBox.width).toBeLessThanOrEqual(640);
  expect(await fits()).toBe(true);
  expect(await axeViolations(page)).toEqual([]);
});

for (const locale of ['en', 'ar'])
  test(`the sign dialog and the signed note: axe clean, 44 px targets, no animation under reduced motion (${locale})`, async ({ page }) => {
    await page.emulateMedia({ reducedMotion: 'reduce' });
    const mock = await open(page, { version: 1, sections: { chiefComplaint: 'Thirst', assessment: 'Review', plan: 'Rest' }, recordingConsent: 'Declined' }, locale);
    mock.reauthed = true;
    const ar = locale === 'ar';
    if (ar) await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    await page.getByRole('button', { name: ar ? 'توقيع الملاحظة' : 'Sign note' }).click();
    const dialog = page.getByRole('dialog');
    await expect(dialog).toBeVisible();
    expect(await dialog.evaluate(d => d.getAnimations().length)).toBe(0);
    for (const target of await dialog.locator('button').all()) expect((await target.boundingBox())!.height).toBeGreaterThanOrEqual(44);
    expect(await axeViolations(page)).toEqual([]);
    await dialog.getByRole('button', { name: ar ? /^التوقيع بصفتي/ : /^Sign as/ }).click();
    await expect(page.getByRole('status').filter({ hasText: ar ? 'موقّعة v1' : 'SIGNED v1' })).toBeVisible();
    expect(await axeViolations(page)).toEqual([]);
  });

// MDV-P2-009 keyboard pass: focus never falls to the page, and a legal signature is never one Enter away.
test('keyboard: the sign dialog opens with focus on the dialog, not on a button, so Enter on open signs nothing', async ({ page }) => {
  const mock = await open(page, { version: 1, sections: { chiefComplaint: 'Thirst', assessment: 'Review', plan: 'Rest' }, recordingConsent: 'Declined' });
  mock.reauthed = true;
  const sign = note(page).getByRole('button', { name: 'Sign note' });
  await tabTo(page, sign);
  await page.keyboard.press('Enter');
  const dialog = page.getByRole('dialog', { name: 'Sign note for Meera Iyer (synthetic)?' });
  await expect(dialog).toBeFocused();
  await page.keyboard.press('Enter');
  await page.keyboard.press('Space');
  await expect(dialog).toBeVisible();
  expect(mock.signs).toEqual([]);
  await page.keyboard.press('Tab');
  await expect(dialog.getByRole('button', { name: 'Sign as Dr Asha Raman (synthetic)' })).toBeFocused();
});

test('keyboard: focus lands on the SIGNED banner after signing, then the addendum field, the new version, and back to the button on cancel', async ({ page }) => {
  const mock = await open(page, { version: 1, sections: { chiefComplaint: 'Thirst', assessment: 'Review', plan: 'Rest' }, recordingConsent: 'Declined' });
  mock.reauthed = true;
  await tabTo(page, note(page).getByRole('button', { name: 'Sign note' }));
  await page.keyboard.press('Enter');
  await page.keyboard.press('Tab');
  await page.keyboard.press('Enter');
  await expect(note(page).getByRole('status').filter({ hasText: 'SIGNED v1' })).toBeFocused();
  await expect(note(page).getByText('Changes save automatically.')).toHaveCount(0); // nothing saves in a signed note

  const add = history(page).getByRole('button', { name: 'Add addendum' });
  await tabTo(page, add);
  await page.keyboard.press('Enter');
  await expect(history(page).getByLabel('Addendum', { exact: true })).toBeFocused();
  await page.keyboard.type('Synthetic correction');
  await page.keyboard.press('Tab');
  await page.keyboard.type('Synthetic reason');
  await page.keyboard.press('Enter');
  await expect(history(page).getByRole('listitem').filter({ hasText: 'Addendum to version 1' })).toBeFocused();

  await tabTo(page, add);
  await page.keyboard.press('Enter');
  await tabTo(page, history(page).getByRole('button', { name: 'Cancel' }));
  await page.keyboard.press('Enter');
  await expect(add).toBeFocused();
});

test('keyboard: following the print link puts focus on the page content, not the page start', async ({ page }) => {
  await open(page, signedSeed);
  await tabTo(page, history(page).getByRole('link', { name: 'Print view and PDF' }));
  await page.keyboard.press('Enter');
  await expect(page.getByRole('article')).toBeVisible();
  await expect(page.locator('#main')).toBeFocused();
});
