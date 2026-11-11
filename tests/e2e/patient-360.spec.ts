import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';

// MDV-P2-004: Patient 360 (E-01). CI has no API: the session and the chart are mocked in the shapes the server sends. The care-relationship check,
// the receptionist's 403, the audit rows and the source on every fact are covered by PatientChartTests on PostgreSQL.
const session = (role: string, level: string) => ({
  user: { id: 'u-rao', displayName: 'Dr. Arjun Rao (synthetic)' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role, flags: [] as string[], country: 'IN',
    capabilities: { RegisterBookPatients: 'Read', CheckInQueue: 'Full', Patient360Chart: level } },
});
const at = (day: number) => `2026-10-${String(day).padStart(2, '0')}T04:30:00Z`;
const points = (values: number[], source = 'ClinicianVerified') => values.map((value, i) => ({ value, takenAt: at(i + 1), source }));

const chart = (over: Record<string, unknown> = {}) => ({
  patient: { id: 'p-1', fullName: 'Meera Iyer (synthetic)', mrn: '000001', dateOfBirth: '1972-03-12', ageYears: 54, sexAtBirth: 'Female' },
  treatmentConsent: true, timeZone: 'Asia/Kolkata',
  chart: {
    allergyState: 'Listed',
    allergies: [{ substance: 'Penicillin', reaction: 'Rash', status: 'Confirmed', source: 'ClinicianVerified' }],
    problems: [{ icd10Code: 'E11.9', description: 'Type 2 diabetes mellitus', verification: 'Verified', onsetOn: '2019-04-02', source: 'ClinicianVerified' },
      { icd10Code: null, description: 'Knee pain', verification: 'PatientReported', onsetOn: null, source: 'PatientReported' }],
    medicines: [{ name: 'Metformin', dose: '500 mg', state: 'Active', source: 'Imported', stoppedAt: null }, { name: 'Atorvastatin', dose: '10 mg', state: 'Stopped', source: 'SyncedFromHis', stoppedAt: '2026-10-12T04:30:00Z' }],
    lastVitals: [{ code: 'Weight', value: 71.5, unit: 'kg', takenAt: at(3), source: 'ClinicianVerified', flag: null, range: null },
      { code: 'RespiratoryRate', value: 24, unit: '/min', takenAt: at(3), source: 'ClinicianVerified', flag: 'High', range: '12–20' },
      { code: 'Height', value: 158, unit: 'cm', takenAt: at(1), source: 'ClinicianVerified', flag: null, range: null }],
    trends: [{ code: 'HbA1c', unit: '%', points: points([8.1, 7.6, 7.4], 'SyncedFromHis') },
      { code: 'BloodPressureSystolic', unit: 'mmHg', points: points([132, 128, 124]) }, { code: 'BloodPressureDiastolic', unit: 'mmHg', points: points([84, 82, 80]) },
      { code: 'Weight', unit: 'kg', points: points([72.4, 72, 71.5]) }],
    recentEncounters: [{ id: 'e-0', startedAt: at(3), status: 'Ended' }], openEncounterId: null,
    lastLabs: [{ code: 'Egfr', value: 52, unit: 'mL/min/1.73 m²', takenAt: at(2), source: 'Imported', flag: 'Low', range: '≥ 60' },
      { code: 'Sodium', value: 140, unit: 'mmol/L', takenAt: at(2), source: 'SyncedFromHis', flag: null, range: '135–145' }],
    bmi: { value: 28.6, olderCode: 'Height', olderAt: at(1) }, ...over,
  },
});

async function open(page: Page, { locale = 'en', level = 'Full', response = { json: chart() } as { status?: number; json: unknown }, path = '/patients/p-1' } = {}) {
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: session('SoloDoctor', level) }));
  await page.route('**/v1/patients/p-1/chart', r => r.fulfill(response));
  await page.goto(path);
}

test('the banner shows the allergy with icon and text, and every fact carries its source', async ({ page }) => {
  await open(page);
  const banner = page.getByRole('region', { name: 'Patient Meera Iyer (synthetic)' });
  await expect(banner.getByRole('heading', { name: /Meera Iyer/ })).toBeVisible();
  await expect(banner).toContainText(/Allergy: \u2068Penicillin\u2069 · verified/);
  await expect(banner.locator('svg').first()).toBeVisible(); // icon beside the words, never colour alone
  for (const [section, tags] of [['Problems', ['Clinician verified', 'Patient reported']], ['Medicines', ['Imported', 'Synced from HIS']], ['Last vitals', ['Clinician verified']]] as const) {
    const card = page.getByRole('region', { name: section, exact: true });
    for (const tag of tags) await expect(card.getByText(tag, { exact: true }).first()).toBeVisible();
  }
  const problems = page.getByRole('region', { name: 'Problems', exact: true });
  await expect(problems.getByRole('listitem')).toHaveCount(2);
  for (const item of await problems.getByRole('listitem').all()) await expect(item.getByText(/Clinician verified|Patient reported|Imported|Synced from HIS/).first()).toBeVisible(); // no untagged value
  await expect(page.getByRole('img', { name: /HbA1c: latest .?7.4 %.? on .*3 readings/ })).toBeVisible();
  await expect(page.getByRole('img', { name: /Blood pressure: latest .?124\/80 mmHg/ })).toBeVisible();
  await expect(page.getByText('To start a consultation, use the visit on the board.')).toBeVisible();
});

test('MDV-P2-031: a reading outside its range is flagged in words with the range, and BMI names the older measure', async ({ page }) => {
  await open(page);
  const vitals = page.getByRole('region', { name: 'Last vitals', exact: true });
  await expect(vitals.getByRole('listitem').filter({ hasText: 'Respiratory rate' })).toContainText(/High, usual .?12–20 \/min/);
  await expect(vitals.getByRole('listitem').filter({ hasText: 'Weight' })).not.toContainText('usual');
  await expect(vitals).toContainText(/BMI .?28.6.?uses the height of 01 Oct 2026/);
  const labs = page.getByRole('region', { name: 'Results', exact: true });
  await expect(labs.getByRole('listitem').filter({ hasText: 'eGFR' })).toContainText(/Low, usual .?≥ 60 mL\/min\/1.73 m²/);
  await expect(labs.getByRole('listitem').filter({ hasText: 'Sodium' })).not.toContainText('usual');
  await expect(labs).toContainText('The reference ranges are a draft awaiting clinical review.');
  await page.unroute('**/v1/patients/p-1/chart');
  await page.route('**/v1/patients/p-1/chart', r => r.fulfill({ json: chart({ lastLabs: [], bmi: null }) }));
  await page.reload();
  await expect(page.getByRole('region', { name: 'Results', exact: true })).toContainText('No results recorded.');
  await expect(page.getByText(/BMI/)).toHaveCount(0); // no BMI without both a weight and a height
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

test('at 200 % zoom the layout stays one usable column with nothing cut off', async ({ page }) => {
  await page.setViewportSize({ width: 640, height: 800 }); // 1280 px at 200 %
  await open(page);
  await expect(page.getByRole('region', { name: 'Problems', exact: true })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
for (const [locale, name] of [['en', 'Patient Meera Iyer (synthetic)'], ['ar', 'المريض Meera Iyer (synthetic)']] as const)
  test(`axe finds no violations (${locale}); 44 px targets; two columns on a tablet`, async ({ page }) => {
    await page.setViewportSize({ width: 1024, height: 768 });
    await open(page, { locale, response: { json: chart({ openEncounterId: 'e-9' }) } });
    await expect(page.getByRole('region', { name })).toBeVisible();
    if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    for (const l of await page.getByRole('main').getByRole('link').all()) expect((await l.boundingBox())!.height).toBeGreaterThanOrEqual(44);
    const [a, b] = await page.getByRole('main').locator('section[aria-label]').evaluateAll(els => els.slice(1, 3).map(e => e.getBoundingClientRect().top));
    expect(a).toBe(b); // the cards sit side by side
    await page.addScriptTag({ path: axePath });
    const violations = await page.evaluate(async () =>
      (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations.map(v => `${v.id} (${v.nodes.length})`));
    expect(violations).toEqual([]);
  });
