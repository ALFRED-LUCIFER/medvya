import { createRequire } from 'node:module';
import { expect, test, type Page, type Request } from '@playwright/test';

// MDV-P1-018 directory and privacy-safe search. CI has no API: /v1/me/context and /v1/patients/* are mocked with the synthetic
// cast (docs/00-overview/personas.md); the server side is covered by PatientEndpointTests on SQL Server.
const receptionist = {
  user: { id: 'u-joseph', displayName: 'A. Joseph' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role: 'Receptionist' }],
  active: {
    membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role: 'Receptionist', flags: [],
    capabilities: { RegisterBookPatients: 'Full', CheckInQueue: 'Full', InvoicesPayments: 'Full', Patient360Chart: 'None', ClinicalNotes: 'None' },
  },
};
const meeraId = '5e1d0001-0004-4000-8000-000000000001';
const rows = [
  { id: meeraId, initials: 'MI', ageYears: 54, ageEstimated: false, sexAtBirth: 'Female', mrnTail: '4482', status: 'Complete' },
  { id: '5e1d0001-0004-4000-8000-000000000002', initials: 'DN', ageYears: 47, ageEstimated: true, sexAtBirth: 'Male', mrnTail: '4483', status: 'Incomplete' },
];
const meera = {
  id: meeraId, mrn: 'DQ-10-4482', fullName: 'Meera Iyer (synthetic)', arabicName: null, dateOfBirth: '1972-03-12', dateOfBirthEstimated: false,
  ageYears: 54, sexAtBirth: 'Female', genderIdentity: null, preferredLanguage: 'Tamil', nationality: 'IN', mobile: '+919000000101',
  alternatePhone: null, email: null, status: 'Complete', registeredOn: '2026-01-01',
  address: { line1: '4 Synthetic Street', city: 'Chennai', district: 'Chennai', region: 'Tamil Nadu', postalCode: '600020' },
  identifiers: [{ scheme: 'Abha', lastFour: '0101', expiresOn: null }],
  payer: { kind: 'PrivateInsurance', payerName: 'TPA (synthetic)', plan: null, memberNumberLastFour: '7302', validTo: null, network: null },
};

type SearchBody = { query: string | null; filter: string; page: number };

async function deskWith(page: Page, search: (body: SearchBody) => { status?: number; json: unknown }, locale = 'en') {
  const searches: Request[] = [];
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: receptionist }));
  await page.route('**/v1/patients/search**', r => {
    searches.push(r.request());
    const { status = 200, json } = search(r.request().postDataJSON() as SearchBody);
    return r.fulfill({ status, json });
  });
  await page.route(`**/v1/patients/${meeraId}`, r => r.fulfill({ json: meera }));
  return searches;
}

const found = (body: SearchBody) => ({
  json: { matchedOn: body.query ? 'Name' : 'Directory', items: body.query === 'nobody' ? [] : rows, page: body.page, pageSize: 20, total: body.query === 'nobody' ? 0 : 2 },
});

test('the desk finds a patient from the header, sees only initials and opens the record', async ({ page }) => {
  const searches = await deskWith(page, found);
  await page.goto('/front-desk');
  await page.getByRole('link', { name: 'Search patients' }).click();
  await expect(page).toHaveURL('/patients/directory');
  await expect(page.getByRole('heading', { name: 'Patients', level: 1 })).toBeVisible();

  const search = page.getByRole('searchbox', { name: 'Name, MRN, phone or ID' });
  await search.fill('meera');
  await search.press('Enter');
  await expect(page.getByRole('heading', { name: 'Name matches: 2' })).toBeVisible();

  // Search text only ever travels in the POST body.
  const last = searches.at(-1)!;
  expect(last.method()).toBe('POST');
  expect(new URL(last.url()).search).toBe('');
  expect(last.postDataJSON()).toEqual({ query: 'meera', filter: 'All', page: 1 });
  expect(page.url()).not.toContain('meera');

  const results = page.getByRole('main').getByRole('list');
  await expect(results).not.toContainText('Meera');
  await expect(page.getByRole('link', { name: /M I, 54 y, Female, MRN ending 4482/ })).toBeVisible();
  await expect(page.getByRole('link', { name: /D N, about 47 y, Male, MRN ending 4483.*incomplete registration/ })).toBeVisible();

  await page.getByRole('link', { name: /M I, 54 y/ }).click();
  await expect(page).toHaveURL(`/patients/directory/${meeraId}`);
  await expect(page.getByRole('heading', { name: 'Meera Iyer (synthetic)', level: 1 })).toBeVisible();
  await expect(page.getByText('+919000000101')).toBeVisible();
  await expect(page.getByText('Clinical notes are never shown here.')).toBeVisible();
});

test('filters reset to page one and reach the server as a filter, not as text', async ({ page }) => {
  const searches = await deskWith(page, found);
  await page.goto('/patients/directory');
  await page.getByRole('button', { name: 'Incomplete registration' }).click();
  await expect(page.getByRole('button', { name: 'Incomplete registration' })).toHaveAttribute('aria-pressed', 'true');
  await expect.poll(() => searches.at(-1)?.postDataJSON()).toEqual({ query: null, filter: 'Incomplete', page: 1 });
});

test('no match offers registration; a partial number and a refused role explain themselves', async ({ page }) => {
  await deskWith(page, body =>
    body.query === '98470 213' ? { status: 400, json: { errors: [{ field: 'query', code: 'full_number_required' }] } }
    : body.query === 'forbidden' ? { status: 403, json: {} }
    : found(body));
  await page.goto('/patients/directory');
  const search = page.getByRole('searchbox');

  await search.fill('nobody');
  await search.press('Enter');
  await expect(page.getByRole('status')).toHaveText('No patients match.');
  await expect(page.getByRole('link', { name: 'Register a new patient' })).toHaveAttribute('href', '/patients/new');

  await search.fill('98470 213');
  await search.press('Enter');
  await expect(page.getByText('Enter the full phone number or ID. Partial numbers are not searched.')).toBeVisible();
  await expect(search).toHaveAttribute('aria-invalid', 'true');

  await search.fill('forbidden');
  await search.press('Enter');
  await expect(page.getByRole('status')).toHaveText('You don’t have access to this facility.');
});

test('keyboard only: search, then open the first result', async ({ page }) => {
  await deskWith(page, found);
  await page.goto('/patients/directory');
  await page.getByRole('searchbox').focus();
  await page.keyboard.type('meera');
  await page.keyboard.press('Enter');
  await expect(page.getByRole('heading', { name: 'Name matches: 2' })).toBeVisible();
  await page.keyboard.press('Tab'); // Search button
  for (let i = 0; i < 3; i++) await page.keyboard.press('Tab'); // the three filters
  await page.keyboard.press('Tab');
  await expect(page.getByRole('link', { name: /M I, 54 y/ })).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(page.getByRole('heading', { name: 'Meera Iyer (synthetic)' })).toBeVisible();
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
async function axe(page: Page) {
  await page.addScriptTag({ path: axePath });
  return page.evaluate(async () =>
    (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
      .map(v => `${v.id} (${v.nodes.length})`));
}

for (const locale of ['en', 'ar'])
  for (const width of [1366, 375])
    test(`directory and record are axe clean and mirror in RTL (${locale}, ${width}px)`, async ({ page }) => {
      await deskWith(page, found, locale);
      await page.setViewportSize({ width, height: 900 });
      await page.goto('/patients/directory');
      await expect(page.getByRole('main').getByRole('list').getByRole('link')).toHaveCount(2);
      expect(await page.evaluate(() => document.documentElement.dir)).toBe(locale === 'ar' ? 'rtl' : 'ltr');
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
      for (const link of await page.getByRole('main').getByRole('list').getByRole('link').all()) expect((await link.boundingBox())!.height).toBeGreaterThanOrEqual(44);
      expect(await axe(page)).toEqual([]);

      await page.goto(`/patients/directory/${meeraId}`);
      await expect(page.getByRole('heading', { name: 'Meera Iyer (synthetic)' })).toBeVisible();
      expect(await axe(page)).toEqual([]);
    });

test('identifiers stay masked until an explicit, audited reveal', async ({ page }) => {
  await deskWith(page, found);
  const reveals: string[] = [];
  await page.route(`**/v1/patients/${meeraId}/identifiers/reveal`, r => {
    reveals.push(r.request().method());
    return r.fulfill({ json: { identifiers: [{ scheme: 'Abha', value: '91000000000101' }], memberNumber: 'SYN-TPA-7302' } });
  });
  await page.goto(`/patients/directory/${meeraId}`);
  await expect(page.getByText('••0101')).toBeVisible();
  await expect(page.getByText('••7302')).toBeVisible();
  await expect(page.getByText('91000000000101')).toHaveCount(0);
  await expect(page.getByText('01 Jan 2026')).toBeVisible(); // registeredOn, a facility-local date

  await page.getByRole('button', { name: 'Show full numbers' }).click();
  await expect(page.getByText('91000000000101')).toBeVisible();
  await expect(page.getByText('SYN-TPA-7302')).toBeVisible();
  await expect(page.getByRole('status')).toHaveText('Full numbers shown.');
  expect(reveals).toEqual(['POST']);
});

test('a role with Read access only sees no reveal button', async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('medvya.locale', 'en'));
  await page.route('**/v1/me/context', r => r.fulfill({ json: { ...receptionist, active: { ...receptionist.active, role: 'Nurse', capabilities: { RegisterBookPatients: 'Read' } } } }));
  await page.route(`**/v1/patients/${meeraId}`, r => r.fulfill({ json: meera }));
  await page.goto(`/patients/directory/${meeraId}`);
  await expect(page.getByText('••0101')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Show full numbers' })).toHaveCount(0);
});

test('paging keeps the list on screen and focus on the results, with one live status region', async ({ page }) => {
  let release: () => void = () => {};
  await deskWith(page, body => ({ json: { matchedOn: 'Directory', items: rows, page: body.page, pageSize: 20, total: 45 } }));
  await page.goto('/patients/directory');
  const status = page.getByRole('status');
  await expect(status).toHaveCount(1);
  await expect(status).toHaveText('Patients: 45');

  // Hold the next page so the in-between state is visible.
  await page.route('**/v1/patients/search**', async r => {
    await new Promise<void>(resolve => { release = resolve; });
    return r.fulfill({ json: { matchedOn: 'Directory', items: rows, page: 2, pageSize: 20, total: 45 } });
  });
  await page.getByRole('button', { name: 'Next' }).focus();
  await page.keyboard.press('Enter');
  await expect(page.getByRole('heading', { name: 'Patients: 45' })).toBeFocused();
  await expect(page.locator('section[aria-labelledby="patient-results"]')).toHaveAttribute('aria-busy', 'true');
  await expect(page.getByRole('main').getByRole('list').getByRole('link')).toHaveCount(2); // the previous page stays
  await expect(status).toHaveText('Searching…');
  release();
  await expect(page.getByText('Page 2 of 3')).toBeVisible();
  await expect(status).toHaveText('Patients: 45');
  await expect(page.getByRole('heading', { name: 'Patients: 45' })).toBeFocused();
});
