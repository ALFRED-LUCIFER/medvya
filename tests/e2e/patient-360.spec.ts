import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';

// MDV-P2-004, MDV-P2-033: Patient 360 on one page (E-01). CI has no API: the session and the chart are mocked in the shapes the server sends. The care-relationship check,
// the receptionist's 403, the audit rows and the source on every fact are covered by PatientChartTests on PostgreSQL.
const session = (role: string, level: string, entry = 'None') => ({
  user: { id: 'u-rao', displayName: 'Dr. Arjun Rao (synthetic)' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role, flags: [] as string[], country: 'IN',
    capabilities: { RegisterBookPatients: 'Read', CheckInQueue: 'Full', Patient360Chart: level, VitalsAllergiesEntry: entry } },
});
const at = (day: number) => `2026-10-${String(day).padStart(2, '0')}T04:30:00Z`;
const points = (values: number[], source = 'ClinicianVerified') => values.map((value, i) => ({ value, takenAt: at(i + 1), source }));

const chart = (over: Record<string, unknown> = {}) => ({
  patient: { id: 'p-1', fullName: 'Meera Iyer (synthetic)', mrn: '000001', dateOfBirth: '1972-03-12', ageYears: 54, sexAtBirth: 'Female', preferredLanguage: null },
  treatmentConsent: true, timeZone: 'Asia/Kolkata', lifeStage: 'Adult', historySections: ['PastMedical', 'Surgical', 'Family', 'Social', 'WomensHealth'], 
  chart: {
    allergyState: 'Listed',
    allergies: [{ substance: 'Penicillin', reaction: 'Rash', status: 'Confirmed', source: 'ClinicianVerified' }],
    problems: [{ icd10Code: 'E11.9', description: 'Type 2 diabetes mellitus', verification: 'Verified', onsetOn: '2019-04-02', source: 'ClinicianVerified' },
      { icd10Code: null, description: 'Knee pain', verification: 'PatientReported', onsetOn: null, source: 'PatientReported' }],
    medicines: [{ name: 'Metformin', dose: '500 mg', state: 'Active', source: 'Imported', stoppedAt: null }, { name: 'Atorvastatin', dose: '10 mg', state: 'Stopped', source: 'SyncedFromHis', stoppedAt: '2026-10-12T04:30:00Z' }],
    lastVitals: [{ code: 'Weight', value: 71.5, unit: 'kg', takenAt: at(3), source: 'ClinicianVerified', flag: null, range: null },
      { code: 'BloodPressureSystolic', value: 124, unit: 'mmHg', takenAt: at(3), source: 'ClinicianVerified', flag: null, range: '90–130' },
      { code: 'BloodPressureDiastolic', value: 80, unit: 'mmHg', takenAt: at(3), source: 'ClinicianVerified', flag: null, range: '60–80' },
      { code: 'RespiratoryRate', value: 24, unit: '/min', takenAt: at(3), source: 'ClinicianVerified', flag: 'High', range: '12–20' },
      { code: 'Height', value: 158, unit: 'cm', takenAt: at(1), source: 'ClinicianVerified', flag: null, range: null }],
    trends: [{ code: 'HbA1c', unit: '%', points: points([8.1, 7.6, 7.4], 'SyncedFromHis') },
      { code: 'BloodPressureSystolic', unit: 'mmHg', points: points([132, 128, 124]) }, { code: 'BloodPressureDiastolic', unit: 'mmHg', points: points([84, 82, 80]) },
      { code: 'Weight', unit: 'kg', points: points([72.4, 72, 71.5]) }],
    recentEncounters: [{ id: 'e-0', startedAt: at(3), status: 'Ended' }], openEncounterId: null,
    lastLabs: [{ code: 'HbA1c', value: 7.4, unit: '%', takenAt: at(3), source: 'SyncedFromHis', flag: null, range: null },
      { code: 'Egfr', value: 52, unit: 'mL/min/1.73 m²', takenAt: at(2), source: 'Imported', flag: 'Low', range: '≥ 60' },
      { code: 'Sodium', value: 140, unit: 'mmol/L', takenAt: at(2), source: 'SyncedFromHis', flag: null, range: '135–145' }],
    bmi: { value: 28.6, olderCode: 'Height', olderAt: at(1) }, bloodGroup: null, careTeam: [],
    history: [{ section: 'Social', text: 'Teacher. Never smoked (synthetic).', author: 'Dr. Arjun Rao (synthetic)', recordedAt: at(5) },
      { section: 'Social', text: 'Teacher (synthetic).', author: null, recordedAt: at(2) }], ...over,
  },
});

const timeline = { entries: [{ id: 'x-1', kind: 'Encounter', at: at(3), code: 'Consultation', title: null, detail: null, author: 'Dr. Arjun Rao (synthetic)', source: 'ClinicianVerified' }], next: null, timeZone: 'Asia/Kolkata' };
const documents = { patient: { id: 'p-1', fullName: 'Meera Iyer (synthetic)', mrn: '000001', dateOfBirth: '1972-03-12' }, canOpen: true, timeZone: 'Asia/Kolkata',
  documents: [{ id: 'd-1', type: 'LabReport', documentDate: '2026-10-01', source: 'Imported', scanStatus: 'Clean', contentType: 'application/pdf', sizeBytes: 2048, uploadedBy: 'Dr. Arjun Rao (synthetic)', uploadedAt: '2026-10-02T04:30:00Z' }] };

async function open(page: Page, { locale = 'en', level = 'Full', entry = 'None', response = { json: chart() } as { status?: number; json: unknown }, path = '/patients/p-1', side = true } = {}) {
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: session('SoloDoctor', level, entry) }));
  await page.route('**/v1/patients/p-1/chart', r => r.fulfill(response));
  await page.route('**/v1/patients/p-1/timeline*', r => r.fulfill(side ? { json: timeline } : { json: { entries: [], next: null, timeZone: 'Asia/Kolkata' } }));
  await page.route('**/v1/patients/p-1/documents', r => r.fulfill(side ? { json: documents } : { json: { ...documents, documents: [] } }));
  await page.goto(path);
}

test('the banner shows the allergy with icon and text, and every fact carries its source', async ({ page }) => {
  await open(page);
  const banner = page.getByRole('region', { name: 'Patient Meera Iyer (synthetic)' });
  await expect(banner.getByRole('heading', { name: /Meera Iyer/ })).toBeVisible();
  await expect(banner).toContainText(/Allergy: \u2068Penicillin\u2069 · verified/);
  await expect(banner.locator('svg').first()).toBeVisible(); // icon beside the words, never colour alone
  for (const [section, tags] of [['Problems', ['Clinician verified', 'Patient reported']], ['Medicines', ['Imported', 'Synced from HIS']], ['Last vitals', ['Clinician verified']],
    ['Results and trends', ['Imported', 'Synced from HIS']], ['Timeline', ['Clinician verified']], ['Documents', ['Imported']]] as const) {
    const card = page.getByRole('region', { name: section, exact: true });
    for (const tag of tags) await expect(card.getByText(tag, { exact: true }).first()).toBeVisible();
  }
  const problems = page.getByRole('region', { name: 'Problems', exact: true });
  await expect(problems.getByRole('listitem')).toHaveCount(2);
  for (const item of await problems.getByRole('listitem').all()) await expect(item.getByText(/Clinician verified|Patient reported|Imported|Synced from HIS/).first()).toBeVisible(); // no untagged value
  await expect(page.getByRole('img', { name: /HbA1c: latest .?7.4 %.? on .*3 readings/ })).toBeVisible(); // the results table's trend
  await expect(page.getByRole('img', { name: /Blood pressure: latest .?124\/80 mmHg.? on .*3 readings/ })).toBeVisible(); // the vitals tile's trend
  await expect(page.getByRole('region', { name: 'Last vitals', exact: true }).getByRole('listitem').filter({ hasText: 'Oxygen saturation' })).toContainText('Not recorded');
  await expect(page.getByRole('region', { name: 'Chronic care' })).toContainText('Nothing for this life stage is recorded yet.');
  await expect(page.getByText('To start a consultation, use the visit on the board.')).toBeVisible();
});

test('MDV-P2-031: a reading outside its range is flagged in words with the range, and BMI names the older measure', async ({ page }) => {
  await open(page);
  const vitals = page.getByRole('region', { name: 'Last vitals', exact: true });
  await expect(vitals.getByRole('listitem').filter({ hasText: 'Respiratory rate' })).toContainText(/High, usual .?12–20 \/min/);
  await expect(vitals.getByRole('listitem').filter({ hasText: 'Weight' })).not.toContainText('usual');
  await expect(vitals).toContainText(/BMI.?28.6.?uses the height of 01 Oct 2026/);
  const labs = page.getByRole('region', { name: 'Results and trends', exact: true });
  await expect(labs.getByRole('row').filter({ hasText: 'eGFR' })).toContainText(/Low, usual .?≥ 60 mL\/min\/1.73 m²/);
  await expect(labs.getByRole('row').filter({ hasText: 'Sodium' })).toContainText('In range');
  await expect(page.getByRole('list', { name: 'Patient details' })).toContainText(/eGFR low: .?52 mL\/min\/1.73 m²/); // the banner alert, in words
  await expect(labs).toContainText('The reference ranges are a draft awaiting clinical review.');
  await page.unroute('**/v1/patients/p-1/chart');
  await page.route('**/v1/patients/p-1/chart', r => r.fulfill({ json: chart({ lastLabs: [], bmi: null }) }));
  await page.reload();
  await expect(page.getByRole('region', { name: 'Results and trends', exact: true })).toContainText('No results recorded.');
  await expect(vitals.getByRole('listitem').filter({ hasText: 'BMI' })).toHaveText('BMINot recorded'); // no BMI without both a weight and a height
});

test('MDV-P2-032: the life stage is a label with an icon and words; blood group, language and care team show only when recorded', async ({ page }) => {
  await open(page);
  const details = page.getByRole('list', { name: 'Patient details' });
  await expect(details.getByRole('listitem').first()).toHaveText('Adult');
  await expect(details.locator('svg').first()).toBeVisible();
  await expect(details).not.toContainText(/Blood group|Language|Care team/); // nothing guessed
  await page.unroute('**/v1/patients/p-1/chart');
  const recorded = chart({ bloodGroup: 'O+', careTeam: ['Dr. Arjun Rao (synthetic)'] });
  await page.route('**/v1/patients/p-1/chart', r => r.fulfill({ json: { ...recorded, lifeStage: 'Child', historySections: ['Birth', 'Feeding', 'PastMedical'], patient: { ...recorded.patient, preferredLanguage: 'Tamil' } } }));
  await page.reload();
  await expect(details.getByRole('listitem').first()).toHaveText('Child');
  await expect(details).toContainText(/Blood group .?O\+/);
  await expect(details).toContainText('Language: Tamil');
  await expect(details).toContainText('Care team: Dr. Arjun Rao (synthetic)');
  await expect(page.getByRole('region', { name: 'History' }).getByRole('heading', { level: 3 }).nth(1)).toHaveText('Birth'); // the sections follow the life stage
});

test('MDV-P2-032: history keeps every version with its author and time, and a doctor or nurse saves a new one', async ({ page }) => {
  const saved: unknown[] = [];
  await page.route('**/v1/patients/p-1/history/*', r => {
    saved.push({ section: r.request().url().split('/').at(-1), ...(r.request().postDataJSON() as object) });
    return r.fulfill({ json: { items: [{ section: 'Family', text: 'Mother: diabetes (synthetic).', author: 'Dr. Arjun Rao (synthetic)', recordedAt: at(6) }, ...chart().chart.history] } });
  });
  await open(page, { entry: 'Full' });
  const history = page.getByRole('region', { name: 'History' });
  await expect(history).toContainText('Teacher. Never smoked (synthetic).');
  await expect(history).toContainText('Dr. Arjun Rao (synthetic) · 05 Oct 2026');
  await history.getByText('Earlier versions (1)').click();
  await expect(history).toContainText('Teacher (synthetic).');
  await history.getByRole('button', { name: 'Edit Family' }).click();
  await history.getByRole('button', { name: 'Save' }).click();
  await expect(history.getByText('Write the history, or cancel.')).toBeVisible(); // blank is refused before it is sent
  await history.getByRole('textbox', { name: 'Family' }).fill('Mother: diabetes (synthetic).');
  await history.getByRole('button', { name: 'Save' }).click();
  await expect(history).toContainText('Mother: diabetes (synthetic).');
  expect(saved).toEqual([{ section: 'Family', text: 'Mother: diabetes (synthetic).' }]);
});

test('MDV-P2-032: a role without vitals and allergies entry sees the history but no edit buttons', async ({ page }) => {
  await open(page);
  await expect(page.getByRole('region', { name: 'History' })).toContainText('Teacher. Never smoked (synthetic).');
  await expect(page.getByRole('button', { name: /^Edit / })).toHaveCount(0);
});

test('"no known allergies" and "not recorded" read differently', async ({ page }) => {
  await open(page, { response: { json: chart({ allergyState: 'NoKnownAllergies', allergies: [] }) } });
  const banner = page.getByRole('region', { name: 'Patient Meera Iyer (synthetic)' });
  await expect(banner).toContainText('No known allergies');
  await expect(banner).not.toContainText('not recorded');
  await page.unroute('**/v1/patients/p-1/chart');
  await page.route('**/v1/patients/p-1/chart', r => r.fulfill({ json: chart({ allergyState: 'NotRecorded', allergies: [] }) }));
  await page.reload();
  await expect(banner).toContainText('Allergies not recorded');
  await expect(banner).not.toContainText('No known allergies');
});

test('an open consultation is one tap away', async ({ page }) => {
  await open(page, { response: { json: chart({ openEncounterId: 'e-9' }) } });
  await page.getByRole('link', { name: 'Open consultation' }).click();
  await expect(page).toHaveURL(/\/consult\/e-9$/);
});

test('refusals: no care relationship asks for access, the role is told so, and a missing patient is not found', async ({ page }) => {
  await open(page, { response: { status: 403, json: { code: 'no_care_relationship', requestPending: false } } });
  await expect(page.getByRole('alert')).toContainText("You are not this patient's clinician here");
  await expect(page.getByText('Meera')).toHaveCount(0); // nothing about the patient comes with a refusal
  const asked: unknown[] = [];
  await page.route('**/v1/patients/p-1/access-requests', r => { asked.push(r.request().postDataJSON()); return r.fulfill({ json: { id: 'r-1', status: 'Pending' } }); });
  await page.getByRole('button', { name: 'Request access' }).click();
  await expect(page.getByRole('alert')).toContainText('Access requested. An owner of the clinic will decide.');
  expect(asked).toEqual([{ reason: 'SeeingPatientToday' }]);
  await page.unroute('**/v1/patients/p-1/chart');
  await page.route('**/v1/patients/p-1/chart', r => r.fulfill({ status: 403, json: { code: 'no_access' } }));
  await page.reload();
  await expect(page.getByRole('alert')).toContainText('Your role cannot open patient charts.');
  await page.unroute('**/v1/patients/p-1/chart');
  await page.route('**/v1/patients/p-1/chart', r => r.fulfill({ status: 404, json: {} }));
  await page.reload();
  await expect(page.getByRole('alert')).toContainText('This patient was not found.');
});

test('a role without Patient 360 has no such page', async ({ page }) => {
  await open(page, { level: 'None' });
  await expect(page.getByText('Meera')).toHaveCount(0);
});

test('MDV-P2-033: the section buttons filter the page, work from the keyboard, and are kept for this user in the session only', async ({ page }) => {
  await open(page);
  const buttons = page.getByRole('group', { name: 'Show part of the chart' });
  await expect(buttons.getByRole('button')).toHaveText(['Everything', 'Summary', 'Vitals and labs', 'Chronic care', 'History', 'Timeline']);
  await expect(buttons.getByRole('button', { name: 'Everything' })).toHaveAttribute('aria-pressed', 'true');
  await buttons.getByRole('button', { name: 'History' }).focus();
  await page.keyboard.press('Enter');
  await expect(buttons.getByRole('button', { name: 'History' })).toHaveAttribute('aria-pressed', 'true');
  await expect(page.getByRole('region', { name: 'History' })).toBeVisible();
  for (const hidden of ['Problems', 'Last vitals', 'Results and trends', 'Timeline', 'Chronic care'])
    await expect(page.getByRole('region', { name: hidden, exact: true })).toHaveCount(0);
  await expect(page.getByRole('region', { name: 'Patient Meera Iyer (synthetic)' })).toBeVisible(); // the banner always shows
  await expect(page.getByRole('region', { name: 'Documents' })).toBeVisible(); // so does the right-hand column
  await page.reload();
  await expect(buttons.getByRole('button', { name: 'History' })).toHaveAttribute('aria-pressed', 'true'); // kept for the session
  expect(await page.evaluate(() => Object.entries(sessionStorage).filter(([k]) => k.includes('chart')))).toEqual([['medvya.chart.section.u-rao', 'history']]); // per user, a code, no patient data
  expect(await page.evaluate(() => Object.keys(localStorage).filter(k => k.includes('chart')))).toEqual([]);
  await buttons.getByRole('button', { name: 'Vitals and labs' }).focus();
  await page.keyboard.press('Space');
  await expect(page.getByRole('region', { name: 'Last vitals', exact: true })).toBeVisible();
  await expect(page.getByRole('region', { name: 'Results and trends', exact: true })).toBeVisible();
  await expect(page.getByRole('region', { name: 'History' })).toHaveCount(0);
});

test('MDV-P2-033: the life-stage button and slot follow the stage, and every block has an empty state', async ({ page }) => {
  const empty = chart({ allergyState: 'NotRecorded', allergies: [], problems: [], medicines: [], lastVitals: [], trends: [], lastLabs: [], bmi: null, history: [] });
  await open(page, { side: false, response: { json: { ...empty, lifeStage: 'Pregnancy', historySections: ['PastMedical', 'Obstetric'] } } });
  await expect(page.getByRole('button', { name: 'Pregnancy' })).toBeVisible();
  await expect(page.getByRole('region', { name: 'Pregnancy' })).toContainText('Nothing for this life stage is recorded yet.');
  await expect(page.getByRole('region', { name: 'Problems', exact: true })).toContainText('No problems recorded.');
  await expect(page.getByRole('region', { name: 'Medicines', exact: true })).toContainText('No medicines recorded.');
  await expect(page.getByRole('region', { name: 'Last vitals', exact: true }).getByText('Not recorded')).toHaveCount(6);
  await expect(page.getByRole('region', { name: 'Results and trends', exact: true })).toContainText('No results recorded.');
  await expect(page.getByRole('region', { name: 'Timeline' })).toContainText('Nothing in the record yet.');
  await expect(page.getByRole('region', { name: 'Documents' })).toContainText('No documents yet.');
});

test('MDV-P2-033: the timeline and documents open from the chart', async ({ page }) => {
  await open(page);
  await expect(page.getByRole('region', { name: 'Timeline' })).toContainText('Dr. Arjun Rao (synthetic)');
  await expect(page.getByRole('region', { name: 'Documents' })).toContainText('Lab report');
  await page.getByRole('link', { name: 'All documents' }).click();
  await expect(page).toHaveURL(/\/patients\/p-1\/docs$/);
});

test('a refused chart asks the server for nothing more than the chart', async ({ page }) => {
  const asked: string[] = [];
  page.on('request', r => { if (/\/v1\/patients\/p-1\/(timeline|documents)/.test(r.url())) asked.push(r.url()); });
  await open(page, { response: { status: 403, json: { code: 'no_access' } } });
  await expect(page.getByRole('alert')).toContainText('Your role cannot open patient charts.');
  expect(asked).toEqual([]);
});

for (const [width, height] of [[375, 800], [640, 800]] as const) // a phone, and 1280 px at 200 % zoom
  test(`at ${width} px the page is one column with nothing cut off`, async ({ page }) => {
    await page.setViewportSize({ width, height });
    await open(page);
    const [problems, medicines] = [page.getByRole('region', { name: 'Problems', exact: true }), page.getByRole('region', { name: 'Medicines', exact: true })];
    await expect(problems).toBeVisible();
    expect((await medicines.boundingBox())!.y).toBeGreaterThan((await problems.boundingBox())!.y); // stacked
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  });

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
for (const [locale, name] of [['en', 'Patient Meera Iyer (synthetic)'], ['ar', 'المريض Meera Iyer (synthetic)']] as const)
  test(`axe finds no violations (${locale}); 44 px targets; the right-hand column on a laptop`, async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await open(page, { locale, entry: 'Full', response: { json: chart({ openEncounterId: 'e-9', bloodGroup: 'B+', careTeam: ['Dr. Arjun Rao (synthetic)'] }) } });
    await expect(page.getByRole('region', { name })).toBeVisible();
    if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    for (const l of await page.getByRole('main').getByRole('link').all()) expect((await l.boundingBox())!.height).toBeGreaterThanOrEqual(44);
    for (const b of await page.getByRole('main').getByRole('button').all()) expect((await b.boundingBox())!.height).toBeGreaterThanOrEqual(44);
    const aside = page.getByRole('complementary');
    const [main, side] = [await aside.locator('xpath=preceding-sibling::div[1]').boundingBox(), await aside.boundingBox()];
    expect(main!.y).toBe(side!.y); // the right-hand column sits beside the chart
    if (locale === 'ar') expect(side!.x).toBeLessThan(main!.x); // and on the left in right to left
    await page.addScriptTag({ path: axePath });
    const violations = await page.evaluate(async () =>
      (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations.map(v => `${v.id} (${v.nodes.length})`));
    expect(violations).toEqual([]);
  });
