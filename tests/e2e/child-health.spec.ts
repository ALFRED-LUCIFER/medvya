import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';

// MDV-P2-036: a child's immunisations and milestones on Patient 360 (E-01). CI has no API: the session, the chart and the card are mocked in the
// shapes the server sends. Due and overdue from the date of birth, the deferral reason, tenant isolation and the role checks are covered by
// ChildHealthTests on PostgreSQL.
const session = (vitals = 'Full') => ({
  user: { id: 'u-rao', displayName: 'Dr. Arjun Rao (synthetic)' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role: 'SoloDoctor' }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role: 'SoloDoctor', flags: [] as string[], country: 'IN',
    capabilities: { RegisterBookPatients: 'Read', CheckInQueue: 'Full', Patient360Chart: 'Full', VitalsAllergiesEntry: vitals } },
});

const chart = (ageYears: number) => ({
  patient: { id: 'p-1', fullName: 'Aarav Iyer (synthetic)', mrn: '000003', dateOfBirth: '2022-07-19', ageYears, sexAtBirth: 'Male', preferredLanguage: null },
  treatmentConsent: true, timeZone: 'Asia/Kolkata', lifeStage: ageYears < 18 ? 'Child' : 'Adult', historySections: ['PastMedical'], pregnancy: { open: null, ruledOut: null },
  chart: { allergyState: 'NoKnownAllergies', allergies: [], problems: [], medicines: [], lastVitals: [], trends: [], recentEncounters: [], openEncounterId: null, lastLabs: [], bmi: null,
    history: [], bloodGroup: null, careTeam: [] },
});

const d = (vaccine: string, dose: number, state: string, more: Record<string, unknown> = {}) =>
  ({ vaccine, dose, state, dueFrom: '2022-07-19', overdueFrom: '2022-08-30', givenOn: null, batch: null, by: null, deferReason: null, deferredOn: null, ...more });

const card = (over: Record<string, unknown> = {}) => ({
  child: true, schedule: 'IAP 2023', signedOff: false,
  bands: [
    { key: 'birth', doses: [d('BCG', 1, 'Given', { givenOn: '2022-07-19', batch: 'B-2210', by: 'Nurse Priya (synthetic)' }), d('OPV', 0, 'Given', { givenOn: '2022-07-19' })] },
    { key: 'm16to18', doses: [d('DTP', 4, 'Overdue', { deferReason: 'Fever on the day (synthetic)', deferredOn: '2024-01-10' })] },
    { key: 'y4to6', doses: [d('DTP', 5, 'DueNow'), d('MMR', 3, 'DueNow')] },
  ],
  milestones: { key: 'y4', signedOff: false, milestones: [
    { key: 'hopsOnOneFoot', answer: 'Yes', answeredBy: 'Parent', recordedBy: 'Nurse Priya (synthetic)', recordedAt: '2026-10-01T04:30:00Z' },
    { key: 'dressesWithLittleHelp', answer: 'Unsure', answeredBy: 'Parent', recordedBy: null, recordedAt: '2026-10-01T04:30:00Z' },
    { key: 'tellsAStory', answer: null, answeredBy: null, recordedBy: null, recordedAt: null },
  ] },
  ...over,
});

async function open(page: Page, { locale = 'en', ageYears = 4, vitals = 'Full' } = {}) {
  const asked: string[] = [];
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: session(vitals) }));
  await page.route('**/v1/patients/p-1/chart', r => r.fulfill({ json: chart(ageYears) }));
  await page.route('**/v1/patients/p-1/child-health', r => { asked.push(r.request().url()); return r.fulfill({ json: card() }); });
  await page.goto('/patients/p-1');
  return asked;
}

const imm = (page: Page) => page.getByRole('region', { name: 'Immunisations' });

test('each band reads given, overdue or due in words with an icon, and the schedule says it awaits review', async ({ page }) => {
  await open(page);
  await expect(imm(page)).toContainText(/Schedule: .?IAP 2023/);
  await expect(imm(page)).toContainText('The schedule is a draft awaiting clinical review');
  const band = (name: string) => imm(page).getByRole('listitem').filter({ has: page.getByText(name, { exact: true }) }).first();
  await expect(band('Birth')).toContainText(/BCG.? dose 1Given/);
  await expect(band('Birth')).toContainText(/batch .?B-2210.? · Nurse Priya/);
  await expect(band('16–18 months')).toContainText('Overdue');
  await expect(band('16–18 months')).toContainText('Deferred: Fever on the day (synthetic)');
  await expect(band('4–6 years')).toContainText('Due now');
  await expect(band('4–6 years').locator('svg').first()).toBeVisible(); // icon beside the word, never colour alone
  const dev = page.getByRole('region', { name: 'Development · 4-year milestones' });
  await expect(dev).toContainText('The milestone list is a draft awaiting clinical review.');
  await expect(dev.getByRole('listitem').filter({ hasText: 'Tells a simple story' })).toContainText('Not answered');
  await expect(dev.getByRole('listitem').filter({ hasText: 'Dresses with little help' })).toContainText('Unsure · Parent');
});

test('a deferral needs a reason before it is sent, and a given dose sends its day and batch', async ({ page }) => {
  await open(page);
  const sent: unknown[] = [];
  await page.route('**/v1/patients/p-1/immunisations', r => { sent.push(r.request().postDataJSON()); return r.fulfill({ json: card() }); });
  await imm(page).getByLabel('Dose', { exact: true }).click();
  await page.getByRole('option', { name: /MMR.? dose 3/ }).click();
  await imm(page).getByLabel('Outcome').click();
  await page.getByRole('option', { name: 'Deferred' }).click();
  await imm(page).getByRole('button', { name: 'Save dose' }).click();
  await expect(imm(page)).toContainText('A deferral needs a reason.');
  expect(sent).toEqual([]);
  await imm(page).getByLabel('Reason for deferring').fill('Unwell today (synthetic)');
  await imm(page).getByRole('button', { name: 'Save dose' }).click();
  await expect.poll(() => sent.length).toBe(1);
  expect(sent[0]).toEqual({ vaccine: 'MMR', dose: 3, outcome: 'Deferred', givenOn: null, batch: null, givenBy: null, reason: 'Unwell today (synthetic)' });

  await imm(page).getByLabel('Dose', { exact: true }).click();
  await page.getByRole('option', { name: /DTP.? dose 5/ }).click();
  await imm(page).getByLabel('Date given').fill('2026-10-19');
  await imm(page).getByLabel('Batch number (optional)').fill('B-77');
  await imm(page).getByRole('button', { name: 'Save dose' }).click();
  await expect.poll(() => sent.length).toBe(2);
  expect(sent[1]).toEqual({ vaccine: 'DTP', dose: 5, outcome: 'Given', givenOn: '2026-10-19', batch: 'B-77', givenBy: null, reason: null });
});

test('a milestone answer says who answered, and a 409 is told in words', async ({ page }) => {
  await open(page);
  const sent: unknown[] = [];
  await page.route('**/v1/patients/p-1/milestones', r => { sent.push(r.request().postDataJSON()); return r.fulfill({ json: card() }); });
  const dev = page.getByRole('region', { name: 'Development · 4-year milestones' });
  await dev.getByLabel('Answered by').click();
  await page.getByRole('option', { name: 'Clinician' }).click();
  await dev.getByRole('group', { name: 'Answer for: Tells a simple story in sentences' }).getByRole('button', { name: 'Yes' }).click();
  await expect.poll(() => sent).toEqual([{ band: 'y4', milestone: 'tellsAStory', answer: 'Yes', answeredBy: 'Clinician' }]);

  await page.route('**/v1/patients/p-1/immunisations', r => r.fulfill({ status: 409, json: { code: 'already_given' } }));
  await imm(page).getByLabel('Dose', { exact: true }).click();
  await page.getByRole('option', { name: /MMR.? dose 3/ }).click();
  await imm(page).getByLabel('Date given').fill('2026-10-19');
  await imm(page).getByRole('button', { name: 'Save dose' }).click();
  await expect(imm(page).getByRole('alert')).toHaveText('This dose is already recorded as given.');
});

test('an adult has no child card and the card is never asked for', async ({ page }) => {
  const asked = await open(page, { ageYears: 18 });
  await expect(page.getByRole('region', { name: 'Problems', exact: true })).toBeVisible();
  await expect(page.getByRole('region', { name: 'Immunisations' })).toHaveCount(0);
  expect(asked).toEqual([]);
});

test('without vitals entry the card is read-only', async ({ page }) => {
  await open(page, { vitals: 'None' });
  await expect(imm(page)).toBeVisible();
  await expect(page.getByRole('button', { name: 'Save dose' })).toHaveCount(0);
  await expect(page.getByRole('group', { name: /Answer for/ })).toHaveCount(0);
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
for (const [locale, title] of [['en', 'Immunisations'], ['ar', 'التطعيمات']] as const)
  test(`axe finds no violations (${locale}); 44 px targets`, async ({ page }) => {
    await open(page, { locale });
    await expect(page.getByRole('region', { name: title })).toBeVisible();
    if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    for (const b of await page.getByRole('region', { name: title }).getByRole('button').all()) expect((await b.boundingBox())!.height).toBeGreaterThanOrEqual(44);
    await page.addScriptTag({ path: axePath });
    const violations = await page.evaluate(async () =>
      (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations.map(v => `${v.id} (${v.nodes.length})`));
    expect(violations).toEqual([]);
  });
