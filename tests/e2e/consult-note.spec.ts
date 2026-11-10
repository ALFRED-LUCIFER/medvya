import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';
import { mockNote } from './support/note';

// MDV-P2-009: the consultation note editor on the consultation page, and the doctor's template choice (MDV-P2-025). CI has no API: the note and
// template endpoints are mocked in the shapes the server sends (versioning, 409 stale_note and the audit rows are ConsultNoteTests on PostgreSQL).
const doctor = {
  user: { id: 'u-rao', displayName: 'Dr. Arjun Rao (synthetic)' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role: 'SoloDoctor' }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role: 'SoloDoctor', flags: [] as string[],
    capabilities: { RegisterBookPatients: 'Full', CheckInQueue: 'Full', Patient360Chart: 'Full', ClinicalNotes: 'Sign' } },
};
const at = '2026-10-19T04:35:00Z';

async function open(page: Page, seed: Parameters<typeof mockNote>[1] = {}, locale = 'en', context: typeof doctor = doctor) {
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: context }));
  await page.route('**/v1/encounters/e-1', r => r.fulfill({ json: {
    encounter: { id: 'e-1', appointmentId: 'a1', patientId: 'p-a1', facilityId: 'f-1', leadClinicianId: 'u-rao', status: 'Active', authorityMode: 'Medvya', startedAt: at, endedAt: null, room: 'Room 3' },
    timeZone: 'Asia/Kolkata', treatmentConsent: true,
    patient: { id: 'p-a1', fullName: 'Meera Iyer (synthetic)', mrn: '000001', dateOfBirth: '1972-03-12', ageYears: 54, sexAtBirth: 'Female' },
    allergies: { state: 'NotRecorded', allergies: [] } } }));
  const mock = await mockNote(page, seed);
  await page.goto('/consult/e-1');
  return mock;
}
const note = (page: Page) => page.getByRole('region', { name: 'Consultation note' });

test('typing autosaves, shows the state, and names the manual path and the required sections', async ({ page }) => {
  const mock = await open(page);
  await expect(note(page)).toContainText('No recording for this visit. You can write the note manually.');
  await expect(note(page).getByText('(required)')).toHaveCount(3);
  await note(page).getByLabel('Chief complaint').fill('Thirst and tiredness (synthetic)');
  await expect(note(page).getByRole('status')).toHaveText('Saved');
  expect(mock.puts).toEqual([{ templateKey: 'general-opd', manual: true, baseVersion: 0, sections: { chiefComplaint: 'Thirst and tiredness (synthetic)' } }]);
  await note(page).getByLabel('Plan').fill('Review in two weeks');
  await expect.poll(() => mock.puts.length).toBe(2);
  await expect(note(page).getByRole('status')).toHaveText('Saved');
  expect(mock.puts.at(-1)).toMatchObject({ baseVersion: 1, sections: { chiefComplaint: 'Thirst and tiredness (synthetic)', plan: 'Review in two weeks' } });
  expect(await page.evaluate(() => JSON.stringify([localStorage, sessionStorage]))).not.toContain('Thirst'); // no note text in browser storage
});

test('a failed save keeps the text on the page and retries', async ({ page }) => {
  const mock = await open(page);
  mock.next = 'offline';
  await note(page).getByLabel('Plan').fill('Rest and fluids');
  await expect(note(page).getByRole('alert')).toContainText('Not saved. Your text is kept on this page.');
  await expect(note(page).getByLabel('Plan')).toHaveValue('Rest and fluids');
  await note(page).getByRole('button', { name: 'Retry now' }).click();
  await expect(note(page).getByRole('status')).toHaveText('Saved');
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
  await expect(note(page).getByRole('status')).toHaveText('Saved');
  await expect(note(page).getByLabel('Plan')).toHaveValue('My new plan');
  await expect(note(page).getByLabel('Assessment')).toHaveValue('Their assessment');
  expect(mock.puts).toEqual([{ templateKey: 'general-opd', manual: true, baseVersion: 2, sections: { plan: 'My new plan', assessment: 'Their assessment' } }]);
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
  await expect(note(page).getByRole('status')).toHaveText('Saved');
  await expect(note(page).getByLabel('Plan')).toHaveValue('Diet advice');
  await expect(note(page).getByLabel('Self-monitoring')).toHaveCount(0);
  expect(mock.puts).toEqual([{ templateKey: 'general-opd', manual: true, baseVersion: 1, sections: { plan: 'Diet advice' } }]);
});

test('a note that is not a draft is read-only', async ({ page }) => {
  await open(page, { version: 3, status: 'Signed', sections: { plan: 'Signed plan' } });
  await expect(note(page)).toContainText('This note is signed and read-only.');
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
  const mock = await open(page, { version: 2, sections: { chiefComplaint: 'Thirst', assessment: 'Review', plan: 'Rest' } });
  mock.reauthed = true;
  await note(page).getByLabel('Plan').fill('Rest and fluids'); // typed just before signing: saved first, then that version is signed
  await note(page).getByRole('button', { name: 'Sign note' }).click();
  const dialog = page.getByRole('dialog', { name: 'Sign this note?' });
  await expect(dialog).toContainText('permanent');
  await expect(dialog).toContainText('addenda');
  await dialog.getByRole('button', { name: 'Keep editing' }).click();
  expect(mock.signs).toEqual([]);
  await note(page).getByRole('button', { name: 'Sign note' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Sign note' }).click();
  await expect(note(page)).toContainText('Signed by Dr. Arjun Rao (synthetic) on 19 Oct 2026 at 10:10 · version 1');
  expect(mock.puts.at(-1)).toMatchObject({ baseVersion: 2, sections: { plan: 'Rest and fluids' } });
  expect(mock.signs).toEqual([{ baseVersion: 3 }]);
  await expect(note(page).getByLabel('Plan')).toHaveJSProperty('readOnly', true);
  await expect(note(page).getByRole('combobox', { name: 'Template' })).toBeDisabled();
  await expect(note(page).getByRole('button', { name: 'Sign note' })).toHaveCount(0);
});

test('sign: blank required sections are named in words and the first one is focused; nothing is sent', async ({ page }) => {
  const mock = await open(page, { version: 1, sections: { assessment: 'Review' } });
  mock.reauthed = true;
  await note(page).getByRole('button', { name: 'Sign note' }).click();
  await expect(note(page).getByRole('alert')).toHaveText('These required sections are empty: Chief complaint, Plan. Fill them in, then sign.');
  await expect(note(page).getByLabel('Chief complaint')).toBeFocused();
  await expect(page.getByRole('dialog')).toHaveCount(0);
  expect(mock.signs).toEqual([]);
  // The server's answer is trusted too (another tab cleared a section): same words, same focus.
  await note(page).getByLabel('Chief complaint').fill('Thirst');
  await note(page).getByLabel('Plan').fill('Rest');
  mock.signNext = 'blank';
  await note(page).getByRole('button', { name: 'Sign note' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Sign note' }).click();
  await expect(note(page).getByRole('alert')).toHaveText('These required sections are empty: Chief complaint, Plan. Fill them in, then sign.');
  await expect(note(page).getByLabel('Chief complaint')).toBeFocused();
});

test('sign: reauth_required sends the doctor to confirm identity, the draft survives, and signing works on return', async ({ page }) => {
  const mock = await open(page, { version: 1, sections: { chiefComplaint: 'Thirst', assessment: 'Review', plan: 'Rest' } });
  await note(page).getByLabel('Plan').fill('Rest and fluids (unsaved when Sign was pressed)');
  await note(page).getByRole('button', { name: 'Sign note' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Sign note' }).click();
  await expect(page.getByText('Confirm your identity', { exact: true })).toBeVisible(); // the provider's stub page
  expect(mock.reauths).toEqual([{ returnUrl: '/consult/e-1' }]); // the opaque consult path only
  expect(mock.signs).toHaveLength(1);
  expect(mock.note.sections.plan).toBe('Rest and fluids (unsaved when Sign was pressed)'); // saved before the redirect
  await page.goto('/consult/e-1'); // the provider returns here
  await expect(note(page).getByLabel('Plan')).toHaveValue('Rest and fluids (unsaved when Sign was pressed)'); // reloaded from the server
  await note(page).getByRole('button', { name: 'Sign note' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Sign note' }).click();
  await expect(note(page)).toContainText('version 1');
  expect(mock.signs).toHaveLength(2);
  expect(await page.evaluate(() => JSON.stringify([localStorage, sessionStorage]))).not.toContain('Rest and fluids');
});

test('sign: a stale note keeps the doctor\'s text and uses the editor\'s reload and reapply', async ({ page }) => {
  const mock = await open(page, { version: 1, sections: { chiefComplaint: 'Thirst', assessment: 'Review', plan: 'Rest' } });
  mock.reauthed = true;
  mock.signNext = 'stale';
  await expect(note(page).getByLabel('Plan')).toHaveValue('Rest');
  mock.note = { ...mock.note, version: 2, sections: { ...mock.note.sections, plan: 'Their plan' } }; // another device saved meanwhile
  await note(page).getByRole('button', { name: 'Sign note' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Sign note' }).click();
  await expect(note(page).getByRole('alert')).toContainText('changed elsewhere');
  await expect(note(page).getByLabel('Plan')).toHaveValue('Rest');
  await note(page).getByRole('button', { name: 'Reload and reapply my changes' }).click();
  await expect(note(page).getByRole('status').filter({ hasText: 'Saved' })).toBeVisible();
  await expect(note(page).getByLabel('Plan')).toHaveValue('Their plan'); // the doctor changed nothing here, so theirs shows; nothing is signed unseen
  expect(mock.note.status).toBe('Draft');
});

test('sign: a refusal is said in words and the note stays a draft', async ({ page }) => {
  const mock = await open(page, { version: 1, sections: { chiefComplaint: 'Thirst', assessment: 'Review', plan: 'Rest' } });
  mock.reauthed = true;
  mock.signNext = 'denied';
  await note(page).getByRole('button', { name: 'Sign note' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Sign note' }).click();
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

test('a role without Sign sees no Sign button, only why', async ({ page }) => {
  const nurse = { ...doctor, active: { ...doctor.active, capabilities: { ...doctor.active.capabilities, ClinicalNotes: 'Full' } } };
  await open(page, { version: 1, sections: { plan: 'x' } }, 'en', nurse);
  await expect(note(page)).toContainText('Only a doctor can sign this note.');
  await expect(note(page).getByRole('button', { name: 'Sign note' })).toHaveCount(0);
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
for (const locale of ['en', 'ar'])
  test(`axe finds no violations on the note (${locale}); targets are 44 px`, async ({ page }) => {
    await open(page, { sections: { plan: 'x' }, version: 1 }, locale);
    await page.getByText(locale === 'ar' ? 'قوالبي' : 'My templates', { exact: true }).click();
    await page.getByRole('button', { name: locale === 'ar' ? 'توقيع الملاحظة' : 'Sign note' }).click(); // blank required sections: the alert in words
    await expect(page.locator('textarea').first()).toBeVisible();
    if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    for (const target of await page.getByRole('main').locator('button:not([role=switch]), [role=combobox], label:has([role=switch]), textarea, summary').all()) expect((await target.boundingBox())!.height).toBeGreaterThanOrEqual(44);
    await page.addScriptTag({ path: axePath });
    const violations = await page.evaluate(async () =>
      (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
        .map(v => `${v.id} (${v.nodes.length})`));
    expect(violations).toEqual([]);
  });
