import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';
import { mockNote } from './support/note';

// MDV-P2-041: the consultation on one page (CONSULTATION STUDIO). CI has no API: the encounter, note, reconciliation and chart are mocked in the
// shapes the server sends. The guided steps are consult-note.spec.ts; both views edit the same note through the same autosave.
const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
const doctor = {
  user: { id: 'u-rao', displayName: 'Dr. Arjun Rao (synthetic)' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role: 'SoloDoctor' }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role: 'SoloDoctor', flags: [] as string[],
    capabilities: { RegisterBookPatients: 'Full', CheckInQueue: 'Full', Patient360Chart: 'Full', ClinicalNotes: 'Sign' } },
};
const at = '2026-10-19T04:35:00Z';

async function open(page: Page, seed: Parameters<typeof mockNote>[1] = {}, locale = 'en') {
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: doctor }));
  await page.route('**/v1/encounters/e-1', r => r.fulfill({ json: {
    encounter: { id: 'e-1', appointmentId: 'a1', patientId: 'p-a1', facilityId: 'f-1', leadClinicianId: 'u-rao', status: 'Active', authorityMode: 'Medvya', startedAt: at, endedAt: null, room: 'Room 3' },
    timeZone: 'Asia/Kolkata', treatmentConsent: true,
    patient: { id: 'p-a1', fullName: 'Meera Iyer (synthetic)', mrn: '000001', dateOfBirth: '1972-03-12', ageYears: 54, sexAtBirth: 'Female' },
    allergies: { state: 'Listed', allergies: [{ substance: 'Amoxicillin', reaction: 'Rash', status: 'Confirmed', source: 'ClinicianVerified' }] } } }));
  await page.route('**/v1/patients/p-a1/chart', r => r.fulfill({ json: {
    patient: { id: 'p-a1', fullName: 'Meera Iyer (synthetic)', mrn: '000001', dateOfBirth: '1972-03-12', ageYears: 54, sexAtBirth: 'Female' }, treatmentConsent: true, timeZone: 'Asia/Kolkata',
    chart: { allergyState: 'Listed', allergies: [], lastVitals: [], trends: [], recentEncounters: [], openEncounterId: 'e-1',
      problems: [{ icd10Code: 'E11.9', description: 'Type 2 diabetes (synthetic)', verification: 'Verified', onsetOn: null, source: 'ClinicianVerified', status: 'Active' }],
      medicines: [{ name: 'Metformin', dose: '500 mg', state: 'Active', source: 'ClinicianVerified', stoppedAt: null }] } } }));
  const mock = await mockNote(page, seed);
  await page.goto('/consult/e-1');
  return mock;
}
const note = (page: Page) => page.getByRole('region', { name: 'Consultation note' });
const bar = (page: Page) => note(page).getByRole('navigation', { name: 'Note sections' });
const axe = async (page: Page) => {
  await page.addScriptTag({ path: axePath });
  return page.evaluate(async () =>
    (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: { target: string[] }[] }[] }> } }).axe.run()).violations.map(v => `${v.id} (${v.nodes.map(n => n.target.join(' ')).join(', ')})`));
};

test('opens on one page: the consent first, a section bar of seven numbered sections, and the side panel beside them', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await open(page, { templateKey: 'diabetes-review' });
  await expect(page.getByRole('region', { name: 'Patient identity' })).toContainText('Amoxicillin'); // the patient strip with the allergies
  await expect(page.getByRole('button', { name: 'One page' })).toHaveAttribute('aria-pressed', 'true');
  await expect(bar(page).getByRole('link')).toHaveText(['1 · VitalsTo do', '2 · HistoryTo do', '3 · Diabetes reviewTo do', '4 · ExaminationTo do', '5 · AssessmentTo do', '6 · PlanTo do', '7 · Review and signTo do']);
  const consent = note(page).getByRole('group', { name: 'Recording consent for this visit' });
  expect((await consent.boundingBox())!.y).toBeLessThan((await bar(page).boundingBox())!.y); // the consent comes first
  await expect(note(page).getByRole('heading', { name: '3 · Diabetes review' })).toBeVisible();
  await expect(note(page).getByLabel('Self-monitoring')).toBeVisible(); // the template's own sections sit in its numbered section
  const panel = note(page).getByRole('complementary', { name: 'Consultation tools' });
  const sections = note(page).getByRole('heading', { name: '1 · Vitals' });
  expect((await panel.boundingBox())!.y).toBeLessThan((await sections.boundingBox())!.y + 200); // two columns: the panel is beside, not below
  await expect(panel.getByRole('tab')).toHaveText(['Assist', 'Chart', 'Checklist 4/8']);
});

test('a section bar link moves focus to its section; a section is ticked in words once finished', async ({ page }) => {
  const mock = await open(page);
  await bar(page).getByRole('link', { name: /^6 · Plan/ }).click();
  await expect(note(page).getByRole('heading', { name: '6 · Plan' })).toBeFocused();
  await bar(page).getByRole('link', { name: /^2 · History/ }).focus();
  await page.keyboard.press('Enter');
  await expect(note(page).getByRole('heading', { name: '2 · History' })).toBeFocused();
  await note(page).getByLabel('Chief complaint').fill('Thirst (synthetic)');
  await expect(bar(page).getByRole('link', { name: '2 · History Done' })).toBeVisible();
  await expect.poll(() => mock.puts.at(-1)?.sections.chiefComplaint).toBe('Thirst (synthetic)'); // the same autosave
});

test('switching to the guided steps and back keeps every typed word and saves the same note', async ({ page }) => {
  const mock = await open(page);
  await note(page).getByLabel('Chief complaint').fill('Thirst (synthetic)');
  await note(page).getByRole('radio', { name: 'Patient declined: I type the note' }).check();
  await note(page).getByLabel('Plan').fill('Diet advice'); // typed, the switch comes before the autosave delay
  await page.getByRole('button', { name: 'Guided steps' }).click();
  await expect(note(page).getByRole('navigation', { name: 'Note sections' })).toHaveCount(0);
  await expect(note(page).getByLabel('Chief complaint')).toHaveValue('Thirst (synthetic)');
  await expect(note(page).getByLabel('Plan')).toHaveValue('Diet advice');
  await expect(note(page).getByRole('radio', { name: 'Patient declined: I type the note' })).toBeChecked();
  await note(page).getByLabel('Assessment').fill('Review (synthetic)');
  await page.getByRole('button', { name: 'One page' }).click();
  await expect(note(page).getByLabel('Assessment')).toHaveValue('Review (synthetic)');
  await expect.poll(() => mock.puts.at(-1)).toMatchObject({ recordingConsent: 'Declined', sections: { chiefComplaint: 'Thirst (synthetic)', plan: 'Diet advice', assessment: 'Review (synthetic)' } });
  await page.reload(); // the choice of view is remembered on this device; the note is the server's
  await expect(page.getByRole('button', { name: 'One page' })).toHaveAttribute('aria-pressed', 'true');
  await expect(note(page).getByLabel('Plan')).toHaveValue('Diet advice');
});

test('declined consent hides Assist and keeps the manual note; agreed shows it', async ({ page }) => {
  await open(page);
  const panel = note(page).getByRole('complementary', { name: 'Consultation tools' });
  await expect(panel.getByRole('region', { name: 'Add to the note' })).toBeVisible();
  await note(page).getByRole('radio', { name: 'Patient declined: I type the note' }).check();
  await expect(note(page)).toContainText('Typing mode: Assist is off for this visit.');
  await expect(panel.getByRole('tab', { name: 'Assist' })).toHaveCount(0);
  await expect(panel.getByRole('region', { name: 'Add to the note' })).toHaveCount(0);
  await expect(panel.getByRole('tab', { name: 'Chart', selected: true })).toBeVisible();
  await expect(note(page).getByLabel('Plan')).toBeEditable(); // the manual note path as today
  await note(page).getByRole('radio', { name: 'Patient agreed to recording' }).check();
  await panel.getByRole('tab', { name: 'Assist' }).click();
  await expect(panel.getByRole('region', { name: 'Add to the note' })).toBeVisible();
});

test('the chart tab summarises active problems and medicines; the checklist tab is the review-and-sign checklist', async ({ page }) => {
  await open(page, { version: 1, sections: { chiefComplaint: 'Thirst', assessment: 'Review', plan: 'Rest' } });
  const panel = note(page).getByRole('complementary', { name: 'Consultation tools' });
  await panel.getByRole('tab', { name: 'Chart' }).click();
  await expect(panel).toContainText('Type 2 diabetes (synthetic)');
  await expect(panel).toContainText('Metformin 500 mg');
  await panel.getByRole('tab', { name: 'Checklist 7/8' }).click();
  await expect(panel.getByRole('region', { name: 'Review and sign' }).getByRole('listitem').filter({ hasText: 'Recording consent recorded' })).toContainText('To do');
});

test('verified vitals go into Objective from the Vitals section only on request; sign from section 7', async ({ page }) => {
  const mock = await open(page, { version: 1, sections: { chiefComplaint: 'Thirst', assessment: 'Review', plan: 'Rest' }, recordingConsent: 'Declined' });
  mock.vitals = [{ code: 'Pulse', value: 82, unit: '/min', takenAt: at, source: 'ClinicianVerified', recordedByName: 'Nurse Priya (synthetic)' }];
  mock.reauthed = true;
  await page.reload();
  await expect(note(page).getByLabel('Objective')).toHaveValue('');
  await note(page).getByRole('button', { name: 'Add to Objective' }).click();
  await expect(note(page).getByLabel('Objective')).toHaveValue(/Pulse 82/);
  await expect(bar(page).getByRole('link', { name: '1 · Vitals Done' })).toBeVisible();
  await note(page).getByRole('button', { name: 'Sign note' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Sign as Dr Asha Raman (synthetic)' }).click();
  await expect(note(page).getByRole('status').filter({ hasText: 'SIGNED v1' })).toBeFocused();
  await expect(bar(page).getByRole('link', { name: '7 · Review and sign Done' })).toBeVisible();
});

test('phone: one column, the panel below the sections, no sideways scrolling', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await open(page);
  const panel = note(page).getByRole('complementary', { name: 'Consultation tools' });
  await expect(panel).toBeVisible();
  expect((await panel.boundingBox())!.y).toBeGreaterThan((await note(page).getByRole('heading', { name: '7 · Review and sign' }).boundingBox())!.y);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

for (const locale of ['en', 'ar'])
  test(`axe finds no violations on the one page (${locale}); right to left in Arabic; targets are 44 px`, async ({ page }) => {
    await open(page, { version: 1, sections: { chiefComplaint: 'Thirst', plan: 'Rest' } }, locale);
    const ar = locale === 'ar';
    await expect(page.getByRole('navigation', { name: ar ? 'أقسام الملاحظة' : 'Note sections' })).toBeVisible();
    if (ar) await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    for (const target of await page.getByRole('main').locator('nav a, [role=tab], button:not([role=switch]), textarea').all()) expect((await target.boundingBox())!.height).toBeGreaterThanOrEqual(44);
    expect(await axe(page)).toEqual([]);
  });
