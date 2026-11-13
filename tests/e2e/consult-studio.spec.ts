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

async function open(page: Page, seed: Parameters<typeof mockNote>[1] = {}, locale = 'en', banner = () => ({ state: 'Listed', allergies: [{ substance: 'Amoxicillin', reaction: 'Rash', status: 'Confirmed', source: 'ClinicianVerified' }] })) {
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: doctor }));
  await page.route('**/v1/encounters/e-1', r => r.fulfill({ json: {
    encounter: { id: 'e-1', appointmentId: 'a1', patientId: 'p-a1', facilityId: 'f-1', leadClinicianId: 'u-rao', status: 'Active', authorityMode: 'Medvya', startedAt: at, endedAt: null, room: 'Room 3' },
    timeZone: 'Asia/Kolkata', treatmentConsent: true,
    patient: { id: 'p-a1', fullName: 'Meera Iyer (synthetic)', mrn: '000001', dateOfBirth: '1972-03-12', ageYears: 54, sexAtBirth: 'Female' },
    allergies: banner() } }));
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

test('opens on one page: no recording consent step, a section bar of seven numbered sections, and the side panel beside them', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await open(page, { templateKey: 'diabetes-review' });
  await expect(page.getByRole('region', { name: 'Patient identity' })).toContainText('Amoxicillin'); // the patient strip with the allergies
  await expect(page.getByRole('button', { name: 'One page' })).toHaveAttribute('aria-pressed', 'true');
  await expect(bar(page).getByRole('link')).toHaveText(['1 · VitalsTo do', '2 · HistoryTo do', '3 · Diabetes reviewTo do', '4 · ExaminationTo do', '5 · AssessmentTo do', '6 · PlanTo do', '7 · Review and signTo do']);
  await expect(note(page).getByRole('group', { name: 'Recording consent for this visit' })).toHaveCount(0); // MDV-AUDIT-1092: nothing is recorded yet
  await expect(note(page).getByRole('heading', { name: '3 · Diabetes review' })).toBeVisible();
  await expect(note(page).getByLabel('Self-monitoring')).toBeVisible(); // the template's own sections sit in its numbered section
  const panel = note(page).getByRole('complementary', { name: 'Consultation tools' });
  const sections = note(page).getByRole('heading', { name: '1 · Vitals' });
  expect((await panel.boundingBox())!.y).toBeLessThan((await sections.boundingBox())!.y + 200); // two columns: the panel is beside, not below
  await expect(panel.getByRole('tab')).toHaveText(['Assist', 'Chart', 'Checklist 4/7']);
});

test('Sign note and End consultation are one bar that stays on screen however long the note is (MDV audit P1-04)', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await open(page);
  const sign = note(page).getByRole('button', { name: 'Sign note' }), end = page.getByRole('button', { name: 'End consultation' });
  await expect(end).toHaveCount(1); // one place for it, not two
  for (const button of [sign, end]) await expect(button).toBeInViewport({ ratio: 1 }); // without scrolling
  const [a, b] = [await sign.boundingBox(), await end.boundingBox()];
  expect(Math.abs(a!.y - b!.y)).toBeLessThan(60); // side by side
  await page.mouse.wheel(0, 600);
  await expect(sign).toBeInViewport({ ratio: 1 }); // still there after scrolling the note
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
  await note(page).getByLabel('Plan').fill('Diet advice'); // typed, the switch comes before the autosave delay
  await page.getByRole('button', { name: 'Guided steps' }).click();
  await expect(note(page).getByRole('navigation', { name: 'Note sections' })).toHaveCount(0);
  await expect(note(page).getByLabel('Chief complaint')).toHaveValue('Thirst (synthetic)');
  await expect(note(page).getByLabel('Plan')).toHaveValue('Diet advice');
  await note(page).getByLabel('Assessment').fill('Review (synthetic)');
  await page.getByRole('button', { name: 'One page' }).click();
  await expect(note(page).getByLabel('Assessment')).toHaveValue('Review (synthetic)');
  await expect.poll(() => mock.puts.at(-1)).toMatchObject({ sections: { chiefComplaint: 'Thirst (synthetic)', plan: 'Diet advice', assessment: 'Review (synthetic)' } });
  await page.reload(); // the choice of view is remembered on this device; the note is the server's
  await expect(page.getByRole('button', { name: 'One page' })).toHaveAttribute('aria-pressed', 'true');
  await expect(note(page).getByLabel('Plan')).toHaveValue('Diet advice');
});

test('the chart tab summarises active problems and medicines; the checklist tab is the review-and-sign checklist', async ({ page }) => {
  await open(page, { version: 1, sections: { chiefComplaint: 'Thirst', assessment: 'Review', plan: 'Rest' } });
  const panel = note(page).getByRole('complementary', { name: 'Consultation tools' });
  await panel.getByRole('tab', { name: 'Chart' }).click();
  await expect(panel).toContainText('Type 2 diabetes (synthetic)');
  await expect(panel).toContainText('Metformin 500 mg');
  await panel.getByRole('tab', { name: 'Checklist 7/7' }).click();
  const items = panel.getByRole('region', { name: 'Review and sign' }).getByRole('listitem');
  await expect(items).toHaveCount(7);
  await expect(items.filter({ hasText: 'Recording consent' })).toHaveCount(0); // MDV-AUDIT-1092: no row until recording exists
  await expect(items.filter({ hasText: 'To do' })).toHaveCount(0);
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
  // Signed (MDV audit UX-11): only what was signed. No "To do" or "Done" on the sections, no Vitals prefill, the consent in words, not radios.
  await expect(bar(page).getByRole('link')).toHaveText(['1 · History', '2 · General OPD', '3 · Examination', '4 · Assessment', '5 · Plan', '6 · Review and sign']);
  await expect(note(page).getByRole('radio')).toHaveCount(0);
  await expect(note(page)).toContainText('Recording consent for this visit: Patient declined: I type the note');
  await expect(note(page)).not.toContainText('No recording for this visit');
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

// MDV-P2-042: the structured review of systems (History) and examination by system (Examination), and the template suggested for the patient.
const ros = (page: Page) => note(page).getByRole('group', { name: 'Review of systems' });
const exam = (page: Page) => note(page).getByRole('group', { name: 'Examination by system' });
const item = (section: ReturnType<typeof ros>, name: string) => section.getByRole('group', { name, exact: true });
const press = (section: ReturnType<typeof ros>, name: string, answer: string) => item(section, name).getByRole('button', { name: answer, exact: true });

test('the review of systems sits in History: Yes or No per symptom, pressed again it clears, an unanswered one is not sent', async ({ page }) => {
  const mock = await open(page, { templateKey: 'diabetes-review', suggestedTemplateKey: 'diabetes-review' });
  await expect(note(page)).toContainText("Suggested for this patient's life stage and problems. You can change it.");
  const history = note(page).getByRole('heading', { name: '2 · History' }).locator('..');
  await expect(history.getByRole('group', { name: 'Review of systems' })).toBeVisible();
  await press(ros(page), 'Tiredness', 'Yes').click();
  await press(ros(page), 'Chest pain', 'No').click();
  await expect(press(ros(page), 'Tiredness', 'Yes')).toHaveAttribute('aria-pressed', 'true');
  await expect.poll(() => mock.puts.at(-1)?.sections.reviewOfSystems).toBe('{"tiredness":{"answer":"yes"},"chestPain":{"answer":"no"}}');
  await press(ros(page), 'Tiredness', 'Yes').click(); // pressed again: blank, not "no"
  await expect.poll(() => mock.puts.at(-1)?.sections.reviewOfSystems).toBe('{"chestPain":{"answer":"no"}}');
  await expect(note(page)).not.toContainText('Suggested for this patient'); // saved: the doctor's note now, no longer a suggestion
});

test('the examination sits in Examination: Abnormal waits for its note, Not examined is kept; the guided steps edit the same answers', async ({ page }) => {
  const mock = await open(page, { templateKey: 'diabetes-review', version: 1, sections: { chiefComplaint: 'Thirst', assessment: 'Review', plan: 'Rest' }, recordingConsent: 'Declined' });
  const section = note(page).getByRole('heading', { name: '4 · Examination' }).locator('..');
  await expect(section.getByRole('group', { name: 'Examination by system' })).toBeVisible();
  await press(exam(page), 'Heart', 'Normal').click();
  await press(exam(page), 'Nervous system', 'Not examined').click();
  await press(exam(page), 'Feet', 'Abnormal').click();
  await expect(exam(page)).toContainText('Describe the abnormal finding. It is saved once it has a note.');
  await expect(bar(page).getByRole('link', { name: '4 · Examination Done' })).toBeVisible();
  await expect.poll(() => mock.puts.at(-1)?.sections.examination).toBe('{"heart":{"answer":"normal"},"nervousSystem":{"answer":"notExamined"}}');
  await note(page).getByRole('button', { name: 'Sign note' }).click(); // signing points to the finding without a note
  await expect(note(page)).toContainText('An abnormal finding has no note yet. Describe it before you sign.');
  await expect(exam(page).getByLabel('Finding for Feet (required)')).toBeFocused();
  await page.getByRole('button', { name: 'Guided steps' }).click();
  await exam(page).getByLabel('Finding for Feet (required)').fill('Reduced monofilament both soles');
  await expect.poll(() => mock.puts.at(-1)?.sections.examination)
    .toBe('{"heart":{"answer":"normal"},"feet":{"answer":"abnormal","note":"Reduced monofilament both soles"},"nervousSystem":{"answer":"notExamined"}}');
  await expect(press(exam(page), 'Heart', 'Normal')).toHaveAttribute('aria-pressed', 'true');
});

test('a signed note shows the answers in words and leaves an unanswered item out', async ({ page }) => {
  await open(page, { templateKey: 'diabetes-review', version: 2, status: 'Signed', recordingConsent: 'Declined',
    signature: { versionNo: 1, signedBy: 'u-rao', signedAt: at, contentHash: 'a'.repeat(64), signerName: 'Dr Asha Raman (synthetic)' },
    sections: { chiefComplaint: 'Thirst', assessment: 'Review', plan: 'Rest', reviewOfSystems: '{"tinglingFeet":{"answer":"yes"}}',
      examination: '{"feet":{"answer":"abnormal","note":"Reduced sensation"},"nervousSystem":{"answer":"notExamined"}}' } });
  await expect(note(page)).toContainText('Tingling feet: Yes');
  await expect(note(page)).toContainText('Feet: Abnormal. Reduced sensation');
  await expect(note(page)).toContainText('Nervous system: Not examined');
  await expect(note(page)).not.toContainText('Chest pain');
  await expect(note(page).getByRole('button', { name: 'Yes' })).toHaveCount(0);
});

for (const locale of ['en', 'ar'])
  test(`axe finds no violations on the review of systems and examination (${locale}); targets are 44 px`, async ({ page }) => {
    await open(page, { templateKey: 'older-adult-review', version: 1, sections: { chiefComplaint: 'Dizzy', reviewOfSystems: '{"dizzyOnStanding":{"answer":"yes"}}', examination: '{"joints":{"answer":"abnormal"}}' } }, locale);
    const ar = locale === 'ar';
    const main = page.getByRole('main'); // the note region's name is in the page's language
    await expect(main.getByRole('group', { name: ar ? 'مراجعة الأجهزة' : 'Review of systems' })).toBeVisible();
    await expect(main.getByRole('group', { name: ar ? 'الفحص حسب الجهاز' : 'Examination by system' })).toBeVisible();
    await expect(main.getByRole('heading', { name: ar ? '3 · مراجعة كبار السن' : '3 · Older adult review' })).toBeVisible();
    if (ar) await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    for (const target of await main.locator('[role=group] [role=group] button, input[id$="-note"]').all()) {
      const box = (await target.boundingBox())!;
      expect(Math.min(box.height, box.width)).toBeGreaterThanOrEqual(44);
    }
    expect(await axe(page)).toEqual([]);
  });

// MDV audit P0-2: a clinic with no nurse. The doctor records vitals and allergies in the Vitals section; "no known allergies" clears the checklist item.
for (const locale of ['en', 'ar']) {
  test(`the doctor confirms no known allergies in the Vitals section and the checklist follows (${locale})`, async ({ page }) => {
    let mock: Awaited<ReturnType<typeof open>> | null = null;
    mock = await open(page, { version: 1, sections: { chiefComplaint: 'Thirst', assessment: 'Review', plan: 'Rest' }, recordingConsent: 'Declined' }, locale,
      () => (mock?.noKnownAllergies ? { state: 'NoKnownAllergies', allergies: [] } : { state: 'NotRecorded', allergies: [] }));
    const vitals = page.locator('#studio-vitals').locator('..');
    // MDV-AUDIT-1091: the optional readings are folded; the allergy check (substance and reaction) is never folded.
    await expect(vitals.getByRole('textbox')).toHaveCount(2);
    expect(await axe(page)).toEqual([]);
    await vitals.locator('summary').click();
    await expect(vitals.getByRole('textbox')).toHaveCount(10); // eight readings, the allergy substance and reaction
    if (locale === 'en') {
      const sign = note(page).getByRole('button', { name: 'Sign note' });
      await expect(sign).toBeDisabled();
      await vitals.getByRole('button', { name: 'Confirm: no known allergies' }).click();
      await expect(vitals).toContainText('No known allergies, confirmed at this visit.');
      await expect(sign).toBeEnabled();
    }
    expect(await axe(page)).toEqual([]);
  });
}
