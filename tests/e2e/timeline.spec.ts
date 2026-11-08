import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';

// MDV-P2-005: the timeline and trends (E-02). CI has no API: endpoints are mocked in the shapes the server sends, and the filter query the
// screen sends is checked. Ordering, "Load earlier" without gaps or repeats, the gate and the audit are covered by PatientTimelineTests.
const session = {
  user: { id: 'u-rao', displayName: 'Dr. Arjun Rao (synthetic)' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role: 'SoloDoctor' }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role: 'SoloDoctor', flags: [] as string[], country: 'IN',
    capabilities: { RegisterBookPatients: 'Read', CheckInQueue: 'Full', Patient360Chart: 'Full' } },
};
const day = (d: number) => `2026-10-${String(d).padStart(2, '0')}T04:30:00Z`;
const entry = (n: number, kind: string, source = 'ClinicianVerified') => kind === 'Encounter'
  ? { id: `e${n}`, kind, at: day(n), code: 'Consultation', title: null, detail: null, author: 'Dr. Arjun Rao (synthetic)', source }
  : kind === 'Lab' ? { id: `e${n}`, kind, at: day(n), code: 'HbA1c', title: null, detail: '7.4 %', author: null, source }
  : { id: `e${n}`, kind, at: day(n), code: 'MedicineActive', title: 'Metformin', detail: '500 mg', author: null, source };
const points = (values: number[]) => values.map((value, i) => ({ value, takenAt: day(i + 1), source: 'ClinicianVerified' }));
const chart = {
  patient: { id: 'p-1', fullName: 'Meera Iyer (synthetic)', mrn: '000001', dateOfBirth: '1972-03-12', ageYears: 54, sexAtBirth: 'Female' }, treatmentConsent: true, timeZone: 'Asia/Kolkata',
  chart: { allergyState: 'NoKnownAllergies', allergies: [], problems: [], medicines: [], lastVitals: [], trends: [], recentEncounters: [], openEncounterId: null },
};

async function open(page: Page, { locale = 'en', all = [] as ReturnType<typeof entry>[], pageSize = 2 } = {}) {
  const queries: string[] = [];
  await page.addInitScript(l => { localStorage.setItem('medvya.locale', l); }, locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: session }));
  await page.route('**/v1/patients/p-1/chart', r => r.fulfill({ json: chart }));
  await page.route('**/v1/patients/p-1/trends', r => r.fulfill({ json: { timeZone: 'Asia/Kolkata', trends: [
    { code: 'HbA1c', unit: '%', points: points([8.1, 7.6, 7.4]) },
    { code: 'BloodPressureSystolic', unit: 'mmHg', points: points([132, 128]) }, { code: 'BloodPressureDiastolic', unit: 'mmHg', points: points([84, 82]) }] } }));
  await page.route('**/v1/patients/p-1/timeline*', r => {
    const url = new URL(r.request().url());
    queries.push(url.search);
    const kinds = url.searchParams.get('kinds')?.split(',') ?? [], source = url.searchParams.get('source'), before = url.searchParams.get('before');
    const rows = all.filter(e => (kinds.length === 0 || kinds.includes(e.kind)) && (!source || e.source === source));
    const start = before ? Number(before) : 0;
    const slice = rows.slice(start, start + pageSize);
    return r.fulfill({ json: { entries: slice, next: start + pageSize < rows.length ? String(start + pageSize) : null, timeZone: 'Asia/Kolkata' } });
  });
  await page.goto('/patients/p-1/timeline');
  return queries;
}

const many = [entry(9, 'Encounter'), entry(8, 'Lab', 'SyncedFromHis'), entry(7, 'Medicine', 'Imported'), entry(6, 'Encounter'), entry(5, 'Medicine', 'PatientReported')];

test('each entry shows date, title, subtitle, author and source tag; filters combine, persist and can be cleared', async ({ page }) => {
  const queries = await open(page, { all: many, pageSize: 10 });
  const list = page.getByRole('region', { name: 'Timeline entries' });
  await expect(list.getByRole('listitem')).toHaveCount(5);
  const first = list.getByRole('listitem').first();
  await expect(first).toContainText('Consultation');
  await expect(first).toContainText('Encounters · Dr. Arjun Rao (synthetic)');
  await expect(first).toContainText('Clinician verified');
  await expect(first).toContainText('09 Oct 2026 10:00'); // facility time
  await expect(list.getByRole('listitem').nth(1)).toContainText('7.4 %');

  const filters = page.getByRole('region', { name: 'Filter the timeline' });
  await filters.getByRole('button', { name: 'Labs' }).click();
  await filters.getByRole('button', { name: 'Medicines' }).click();
  await expect(list.getByRole('listitem')).toHaveCount(3);
  await filters.getByRole('combobox', { name: 'Source' }).click();
  await page.getByRole('option', { name: 'Imported' }).click();
  await expect(list.getByRole('listitem')).toHaveCount(1);
  expect(queries.at(-1)).toBe('?kinds=Lab%2CMedicine&source=Imported');

  // They stay applied when the doctor goes to the chart and comes back.
  await page.getByRole('link', { name: 'Back to the chart' }).click();
  await expect(page).toHaveURL(/\/patients\/p-1$/);
  await page.getByRole('link', { name: 'Timeline' }).click();
  await expect(filters.getByRole('button', { name: 'Labs' })).toHaveAttribute('aria-pressed', 'true');
  await expect(list.getByRole('listitem')).toHaveCount(1);

  await filters.getByRole('button', { name: 'Labs' }).click();
  await filters.getByRole('button', { name: 'Medicines' }).click();
  await filters.getByRole('combobox', { name: 'Source' }).click();
  await page.getByRole('option', { name: 'Clinician verified' }).click();
  await filters.getByRole('button', { name: 'Medicines' }).click();
  await expect(page.getByText('Nothing matches these filters.')).toBeVisible();
  await page.getByRole('button', { name: 'Clear filters' }).first().click();
  await expect(list.getByRole('listitem')).toHaveCount(5);
});

test('"Load earlier…" adds older entries below without moving what the doctor is reading', async ({ page }) => {
  await page.setViewportSize({ width: 1024, height: 360 });
  const all = Array.from({ length: 12 }, (_, i) => entry(20 - i, 'Encounter'));
  await open(page, { all, pageSize: 6 });
  const list = page.getByRole('region', { name: 'Timeline entries' });
  await expect(list.getByRole('listitem')).toHaveCount(6);
  const more = page.getByRole('button', { name: 'Load earlier…' });
  await more.scrollIntoViewIfNeeded(); // the doctor has read to the end of the page
  const third = list.getByRole('listitem').nth(5);
  const before = (await third.boundingBox())!.y;
  await more.click();
  await expect(list.getByRole('listitem')).toHaveCount(12);
  expect(Math.abs((await third.boundingBox())!.y - before)).toBeLessThan(2);
  await expect(page.getByRole('button', { name: 'Load earlier…' })).toHaveCount(0);
  await expect(page.getByText('That is the start of the record.')).toBeVisible();
});

test('trends: switch measure; every value is also in a table with units and dates', async ({ page }) => {
  await open(page, { all: many });
  const trends = page.getByRole('region', { name: 'Trends', exact: true });
  await expect(trends.getByRole('img', { name: /HbA1c, 3 readings in %/ })).toBeVisible();
  const table = trends.getByRole('table', { name: 'HbA1c readings in %, newest first' });
  await expect(table.getByRole('row')).toHaveCount(4);
  await expect(table.getByRole('row').nth(1)).toContainText('03 Oct 2026');
  await expect(table.getByRole('row').nth(1)).toContainText('7.4');
  await trends.getByRole('button', { name: 'Blood pressure' }).click();
  await expect(trends.getByRole('table', { name: 'Blood pressure readings in mmHg, newest first' }).getByRole('row').nth(1)).toContainText('128/82');
  await trends.getByRole('button', { name: 'Weight' }).click();
  await expect(trends.getByText('No Weight readings yet.')).toBeVisible();
});

test('an empty record says so', async ({ page }) => {
  await open(page);
  await expect(page.getByText('Nothing in the record yet.')).toBeVisible();
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
for (const locale of ['en', 'ar'])
  test(`axe finds no violations (${locale}); 44 px targets`, async ({ page }) => {
    await open(page, { locale, all: many, pageSize: 3 });
    await expect(page.getByRole('region', { name: locale === 'ar' ? 'عناصر الخط الزمني' : 'Timeline entries' }).getByRole('listitem').first()).toBeVisible();
    if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    for (const b of await page.getByRole('main').getByRole('button').all()) expect((await b.boundingBox())!.height).toBeGreaterThanOrEqual(44);
    await page.addScriptTag({ path: axePath });
    const violations = await page.evaluate(async () =>
      (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations.map(v => `${v.id} (${v.nodes.length})`));
    expect(violations).toEqual([]);
  });
