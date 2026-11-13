import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';

// MDV-P2-035: a child's growth on Patient 360 (E-01). CI has no API: the session, the chart and the card are mocked in the shapes the server
// sends. The z-scores against WHO's tables, the flags, the over-5 rule, the role checks and tenant isolation are covered by GrowthTests.
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

// Lines are illustrative shapes for drawing only; the numbers the screen states come from the points.
const lines = (base: number, step: number) => [3, 15, 50, 85, 97].map((centile, i) => ({
  centile, points: Array.from({ length: 61 }, (_, m) => ({ ageDays: Math.round(m * 30.4375), value: Number((base + i * 0.4 + m * step).toFixed(2)) })),
}));
const point = (takenOn: string, ageDays: number, value: number, z: number, centile: number) => ({ takenOn, ageDays, value, z, centile });
const growth = (over: Record<string, unknown> = {}) => ({
  child: true, signedOff: false, noCentiles: null,
  measurements: [
    { takenOn: '2025-07-19', ageDays: 1096, weightKg: 13.1, lengthCm: 94, position: 'Standing', headCm: null },
    { takenOn: '2026-10-19', ageDays: 1553, weightKg: 13.6, lengthCm: 99.5, position: null, headCm: null },
  ],
  series: [
    { indicator: 'WeightForAge', points: [point('2025-07-19', 1096, 13.1, -0.62, 26.8), point('2026-10-19', 1553, 13.6, -1.98, 2.4)], lines: lines(10, 0.17), flags: ['BelowThird', 'CrossedDown'] },
    { indicator: 'LengthForAge', points: [point('2025-07-19', 1096, 94, -0.2, 42.1), point('2026-10-19', 1553, 99.5, -0.9, 18.4)], lines: lines(48, 1), flags: [] },
    { indicator: 'BmiForAge', points: [point('2025-07-19', 1096, 14.83, -0.5, 30.9), point('2026-10-19', 1553, 13.74, -1.5, 6.7)], lines: lines(13, 0.02), flags: [] },
  ],
  velocity: { changeKg: 0.5, days: 457, from: '2025-07-19', to: '2026-10-19' },
  ...over,
});

async function open(page: Page, { locale = 'en', ageYears = 4, vitals = 'Full', card = growth() } = {}) {
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: session(vitals) }));
  await page.route('**/v1/patients/p-1/chart', r => r.fulfill({ json: chart(ageYears) }));
  await page.route('**/v1/patients/p-1/child-health', r => r.fulfill({ json: { child: true, schedule: 'IAP 2023', signedOff: false, bands: [], milestones: null } }));
  await page.route('**/v1/patients/p-1/growth', r => (r.request().method() === 'GET' ? r.fulfill({ json: card }) : r.fallback()));
  await page.goto('/patients/p-1');
}

const card = (page: Page) => page.getByRole('region', { name: 'Growth', exact: true });

test('the chart has a text alternative with the latest centile and the trend, states it in words and flags the fall', async ({ page }) => {
  await open(page);
  await expect(card(page).getByRole('img')).toHaveAttribute('aria-label',
    'Weight for age chart with the 3rd, 15th, 50th, 85th and 97th centile lines: latest centile 2 on 19 Oct 2026, down from centile 27 on 19 Jul 2025.');
  await expect(card(page)).toContainText('Latest Weight for age: 13.6 kg · centile 2');
  await expect(card(page).getByRole('listitem').filter({ hasText: 'Below the 3rd centile' }).locator('svg')).toBeVisible(); // icon and words, never colour alone
  await expect(card(page)).toContainText('Crossed down two or more major centile lines');
  await expect(card(page)).toContainText(/Weight velocity: .?\+0\.5.? kg in 457 days, between the last two weights/);
  await expect(card(page)).toContainText('The WHO growth references and the flags are a draft awaiting clinical review.');
  await card(page).getByRole('button', { name: 'Length/height for age' }).click();
  await expect(card(page).getByRole('img')).toHaveAttribute('aria-label', /latest centile 18 on 19 Oct 2026, down from centile 42/);
  await expect(card(page).getByRole('button', { name: 'Length/height for age' })).toHaveAttribute('aria-pressed', 'true');
  const rows = card(page).getByRole('row');
  await expect(rows.nth(1)).toContainText('99.5 · from nurse intake');
  await expect(rows.nth(2)).toContainText('94 · standing');
});

test('a child over five shows the measurements without centiles and says why', async ({ page }) => {
  await open(page, { ageYears: 6, card: growth({ noCentiles: 'over_five', series: [] }) });
  await expect(card(page)).toContainText('Centiles are not shown: the WHO standards used here end at 5 years.');
  await expect(card(page).getByRole('img')).toHaveCount(0);
  await expect(card(page).getByRole('button', { name: 'Weight for age' })).toHaveCount(0);
  await expect(card(page).getByRole('row')).toHaveCount(3);
});

test('a length needs its position before it is sent, and the measurements go in the body', async ({ page }) => {
  await open(page);
  const sent: unknown[] = [];
  await page.route('**/v1/patients/p-1/growth', r => {
    if (r.request().method() !== 'POST') return r.fallback();
    sent.push(r.request().postDataJSON());
    return r.fulfill({ json: growth() });
  });
  await card(page).getByLabel('Date measured').fill('2026-10-19');
  await card(page).getByLabel('Length or height (cm)').fill('99.5');
  await card(page).getByRole('button', { name: 'Save measurements' }).click();
  await expect(card(page)).toContainText('Say whether the length was measured lying or standing.');
  expect(sent).toEqual([]);
  await card(page).getByLabel('Measured', { exact: true }).click();
  await page.getByRole('option', { name: 'standing' }).click();
  await card(page).getByLabel('Weight (kg)').fill('13,6');
  await card(page).getByRole('button', { name: 'Save measurements' }).click();
  await expect.poll(() => sent).toEqual([{ takenOn: '2026-10-19', weightKg: 13.6, lengthCm: 99.5, position: 'Standing', headCm: null }]);
});

test('without vitals entry the card is read-only', async ({ page }) => {
  await open(page, { vitals: 'None' });
  await expect(card(page)).toBeVisible();
  await expect(page.getByRole('button', { name: 'Save measurements' })).toHaveCount(0);
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
for (const [locale, title] of [['en', 'Growth'], ['ar', 'النمو الجسدي']] as const)
  test(`axe finds no violations (${locale}); 44 px targets`, async ({ page }) => {
    await open(page, { locale });
    const region = page.getByRole('region', { name: title, exact: true });
    await expect(region).toBeVisible();
    if (locale === 'ar') {
      await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
      await expect(region.getByRole('img')).toHaveAttribute('aria-label', /المئين 2/);
    }
    for (const b of await region.getByRole('button').all()) expect((await b.boundingBox())!.height).toBeGreaterThanOrEqual(44);
    await page.addScriptTag({ path: axePath });
    const violations = await page.evaluate(async () =>
      (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations.map(v => `${v.id} (${v.nodes.length})`));
    expect(violations).toEqual([]);
  });
