import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';
import { mockNote } from './support/note';

// MDV-P2-009: the consultation note editor on the consultation page, and the doctor's template choice (MDV-P2-025). CI has no API: the note and
// template endpoints are mocked in the shapes the server sends (versioning, 409 stale_note and the audit rows are ConsultNoteTests on PostgreSQL).
const doctor = {
  user: { id: 'u-rao', displayName: 'Dr. Arjun Rao (synthetic)' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role: 'SoloDoctor' }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role: 'SoloDoctor', flags: [] as string[],
    capabilities: { RegisterBookPatients: 'Full', CheckInQueue: 'Full', Patient360Chart: 'Full' } },
};
const at = '2026-10-19T04:35:00Z';

async function open(page: Page, seed: Parameters<typeof mockNote>[1] = {}, locale = 'en') {
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: doctor }));
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

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
for (const locale of ['en', 'ar'])
  test(`axe finds no violations on the note (${locale}); targets are 44 px`, async ({ page }) => {
    await open(page, { sections: { plan: 'x' }, version: 1 }, locale);
    await page.getByText(locale === 'ar' ? 'قوالبي' : 'My templates', { exact: true }).click();
    await expect(page.locator('textarea').first()).toBeVisible();
    if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    for (const target of await page.getByRole('main').locator('button:not([role=switch]), [role=combobox], label:has([role=switch]), textarea, summary').all()) expect((await target.boundingBox())!.height).toBeGreaterThanOrEqual(44);
    await page.addScriptTag({ path: axePath });
    const violations = await page.evaluate(async () =>
      (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
        .map(v => `${v.id} (${v.nodes.length})`));
    expect(violations).toEqual([]);
  });
