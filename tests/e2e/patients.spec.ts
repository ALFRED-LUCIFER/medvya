import { createRequire } from 'node:module';
import { expect, test, type Page, type Request } from '@playwright/test';
import { t } from './support/desk';

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
  { id: meeraId, initials: 'MI', fullName: 'Meera Iyer (synthetic)', ageYears: 54, ageEstimated: false, sexAtBirth: 'Female', mrnTail: '4482', mobileTail: '0101', status: 'Complete', lastVisitOn: '2026-11-12', duplicate: null },
  { id: '5e1d0001-0004-4000-8000-000000000002', initials: 'DN', fullName: 'Deepak Nair (synthetic)', ageYears: 47, ageEstimated: true, sexAtBirth: 'Male', mrnTail: '4483', mobileTail: '0201', status: 'Incomplete', lastVisitOn: null, duplicate: null },
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
  await page.route(`**/v1/schedule/patients/${meeraId}/visits*`, r => r.fulfill({ json: { visits: [{ appointmentId: 'a-1', start: '2026-11-12T04:40:00Z', state: 'Done' }, { appointmentId: 'a-2', start: '2026-10-01T04:40:00Z', state: 'NoShow' }] } }));
  await page.route(`**/v1/patients/${meeraId}/consents`, r => r.fulfill({ json: { policyVersion: '2026-11-draft', decisions: [] } }));
  return searches;
}

const found = (body: SearchBody) => ({
  json: { matchedOn: body.query ? 'Name' : 'Directory', items: body.query === 'nobody' ? [] : rows, page: body.page, pageSize: 20, total: body.query === 'nobody' ? 0 : 2 },
});

test('the desk finds a patient from the header, sees the full name and opens the record', async ({ page }) => {
  const searches = await deskWith(page, found);
  await page.goto('/front-desk');
  await page.getByRole('link', { name: 'Search patients' }).click();
  await expect(page).toHaveURL('/patients/directory');
  await expect(page.getByRole('heading', { name: 'Patients', level: 1 })).toBeVisible();
  // MDV-P2-051: Patients is the menu's page too, and Register patient is a button on it, not a menu item.
  await expect(page.getByRole('navigation').getByRole('link', { name: 'Patients' })).toHaveAttribute('href', '/patients');
  await expect(page.getByRole('main').getByRole('link', { name: 'Register patient' })).toHaveAttribute('href', '/patients/new');

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

  const results = page.getByRole('main').getByRole('table');
  await expect(results).toContainText('Meera Iyer (synthetic)'); // MDV-AUDIT-1100: the name, with the phone and MRN tails, tells similar rows apart
  await expect(results).not.toContainText('+91'); // never the whole phone number
  await expect(page.getByRole('link', { name: /Meera Iyer \(synthetic\), 54 y, Female, MRN ending 4482, phone ending 0101, Last visit 12 Nov 2026\./ })).toBeVisible();
  await expect(page.getByRole('link', { name: /Deepak Nair \(synthetic\), about 47 y, Male, MRN ending 4483, phone ending 0201, No visit yet\..*incomplete registration/ })).toBeVisible();

  await page.getByRole('link', { name: /Meera Iyer \(synthetic\), 54 y/ }).click();
  await expect(page).toHaveURL(`/patients/directory/${meeraId}`);
  await expect(page.getByRole('heading', { name: 'Meera Iyer (synthetic)', level: 1 })).toBeVisible();
  // AC3 (MDV-P2-051): the one Patient record, opened on Details; the desk's tabs hold nothing clinical (no Summary, no Timeline).
  const tabs = page.getByRole('group', { name: 'Patient record' }).getByRole('link');
  await expect(tabs).toHaveText(['Details', 'Documents']);
  await expect(tabs.first()).toHaveAttribute('aria-current', 'page');
  await expect(tabs.last()).toHaveAttribute('href', `/patients/${meeraId}/docs`);
  await expect(page.getByText('+919000000101')).toBeVisible();
  await expect(page.getByText('Clinical notes are never shown here.')).toBeVisible();
  // MDV-1088: visit dates and status only; the desk has no diagnosis, medicines or note to open.
  const visits = page.getByRole('region', { name: 'Visits' });
  await expect(visits.getByRole('listitem')).toHaveCount(2);
  await expect(visits).toContainText('Completed');
  await expect(visits).toContainText('No-show');
  await expect(visits.getByRole('link')).toHaveCount(0);
});

test('filters reset to page one and reach the server as a filter, not as text', async ({ page }) => {
  const searches = await deskWith(page, found);
  await page.goto('/patients/directory');
  const show = page.getByRole('combobox', { name: 'Show' });
  await show.click();
  await page.getByRole('option', { name: 'Incomplete registration' }).click();
  await expect(show).toHaveText('Incomplete registration');
  await expect.poll(() => searches.at(-1)?.postDataJSON()).toEqual({ query: null, filter: 'Incomplete', page: 1 });
});

test('no match offers registration; a number that is too long and a refused role explain themselves', async ({ page }) => {
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
  await expect(page.getByText('This number is too long to search. Enter part of the phone number, the MRN or the Aadhaar number.')).toBeVisible();
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
  await page.keyboard.press('Tab'); // "How search works"
  await page.keyboard.press('Tab'); // Search button
  await page.keyboard.press('Tab'); // the filter
  await page.keyboard.press('Tab'); // the results table's scroll area (focusable so a wide table scrolls by keyboard)
  await page.keyboard.press('Tab');
  await expect(page.getByRole('link', { name: /Meera Iyer \(synthetic\), 54 y/ })).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(page.getByRole('heading', { name: 'Meera Iyer (synthetic)' })).toBeVisible();
});

// MDV-P2-059 AC5 (audit B-16): search and filter share one 44 px row, so on a 1024 x 768 laptop the list starts by 300 px; cells do not repeat their column's name.
test('at 1024 x 768 the first patient row starts at or above 300 px', async ({ page }) => {
  await deskWith(page, found);
  await page.setViewportSize({ width: 1024, height: 768 });
  await page.goto('/patients/directory');
  const search = (await page.getByRole('search').boundingBox())!;
  expect(search.height).toBe(44);
  const first = page.getByRole('main').getByRole('table').getByRole('row').nth(1);
  expect((await first.boundingBox())!.y).toBeLessThanOrEqual(300);
  await expect(first.getByRole('cell').nth(2)).toHaveText('··4482');
  await expect(first.getByRole('cell').nth(3)).toHaveText('··0101');
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
      await expect(page.getByRole('main').getByRole('table').getByRole('link')).toHaveCount(2);
      expect(await page.evaluate(() => document.documentElement.dir)).toBe(locale === 'ar' ? 'rtl' : 'ltr');
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
      for (const link of await page.getByRole('main').getByRole('table').getByRole('link').all()) expect((await link.boundingBox())!.height).toBeGreaterThanOrEqual(44);
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
  await page.route(`**/v1/patients/${meeraId}/consents`, r => r.fulfill({ json: { policyVersion: '2026-11-draft', decisions: [] } }));
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
  await expect(page.getByRole('main').getByRole('table').getByRole('link')).toHaveCount(2); // the previous page stays
  await expect(status).toHaveText('Searching…');
  release();
  await expect(page.getByText('Page 2 of 3')).toBeVisible();
  await expect(status).toHaveText('Patients: 45');
  await expect(page.getByRole('heading', { name: 'Patients: 45' })).toBeFocused();
});

// MDV-P1-101 (audit F-08): two people with one name, age and sex, found by the same phone ending. The server sends the main record first.
const twin = { initials: 'DN', fullName: 'Deepak Nair (synthetic)', ageYears: 47, ageEstimated: false, sexAtBirth: 'Male', mobileTail: '0102', status: 'Complete' };
const twins = [
  { ...twin, id: '5e1d0001-0101-4000-8000-000000000001', mrnTail: '4483', lastVisitOn: '2026-10-19', duplicate: 'MainRecord' },
  { ...twin, id: '5e1d0001-0101-4000-8000-000000000002', mrnTail: '4490', lastVisitOn: null, duplicate: 'PossibleDuplicate' },
];
const twinsFound = () => ({ json: { matchedOn: 'Number', items: twins, page: 1, pageSize: 20, total: 2 } });

test('the directory tells two people with one name apart by MRN ending, last visit and the duplicate mark, and shows no date of birth', async ({ page }) => {
  const searches = await deskWith(page, twinsFound);
  await page.goto('/patients/directory');
  const search = page.getByRole('searchbox', { name: 'Name, MRN, phone or ID' });
  await search.fill('0102');
  await search.press('Enter');
  const table = page.getByRole('main').getByRole('table'), rows = table.getByRole('row');
  // Decision 15: a list at a shared desk shows no date of birth, not even as a column.
  await expect(table.getByRole('columnheader')).toHaveText(['Patient', 'Age', 'Sex', 'MRN', 'Phone', 'Last visit', 'Registration']);
  await expect(rows.nth(1)).toContainText('4483');
  await expect(rows.nth(1)).toContainText('19 Oct 2026');
  await expect(rows.nth(1)).toContainText('Main record');
  await expect(rows.nth(2)).toContainText('4490');
  await expect(rows.nth(2)).toContainText('No visit yet');
  await expect(rows.nth(2)).toContainText('Possible duplicate');
  await expect(page.getByRole('link', { name: /Deepak Nair \(synthetic\), 47 y, Male, MRN ending 4483, phone ending 0102, Last visit 19 Oct 2026, Main record\./ })).toBeVisible();
  await expect(page.getByRole('link', { name: /Male, MRN ending 4490, phone ending 0102, No visit yet, Possible duplicate\./ })).toBeVisible();
  // Neither the search text nor anything about the patients reaches an address.
  for (const url of [page.url(), ...searches.map(s => s.url())]) expect(url).not.toMatch(/0102|Deepak|Nair|1979/);
});

for (const locale of ['en', 'ar'])
  test(`the walk-in picker shows two lines a row, the main record first, 48 px rows that are one target, axe clean (${locale})`, async ({ page }) => {
    const searches = await deskWith(page, twinsFound, locale);
    await page.route('**/v1/schedule?*', r => r.fulfill({ json: { facilityId: 'f-1', timeZone: 'Asia/Kolkata', date: '2026-10-19', start: '2026-10-19T02:30:00Z', end: '2026-10-19T15:30:00Z', columns: [] } }));
    await page.setViewportSize({ width: 820, height: 1180 }); // a tablet at the desk
    await page.goto('/front-desk/walk-in');
    await page.getByRole('searchbox').fill('0102');
    await page.getByRole('searchbox').press('Enter');
    const picks = page.getByRole('list', { name: t(locale, 'booking.patientResults') }).getByRole('button');
    await expect(picks).toHaveCount(2);
    if (locale === 'en') {
      await expect(picks.nth(0)).toHaveAccessibleName('Deepak Nair (synthetic), 47 y, Male, MRN 4483, phone 0102, Last visit 19 Oct 2026, Main record. Choose for this booking');
      await expect(picks.nth(1)).toHaveAccessibleName('Deepak Nair (synthetic), 47 y, Male, MRN 4490, phone 0102, No visit yet, Possible duplicate. Choose for this booking');
      // Decision 15: no date of birth in a search list.
      await expect(page.getByRole('list', { name: t(locale, 'booking.patientResults') })).not.toContainText('Born');
      await expect(picks.nth(0)).toContainText('Last visit 19 Oct 2026');
      await expect(picks.nth(0)).toContainText('Main record');
      await expect(picks.nth(1)).toContainText('Possible duplicate');
      await expect(picks.nth(1)).not.toContainText('+91'); // the phone and the MRN stay masked
    } else await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    for (const pick of await picks.all()) {
      const [button, row] = [await pick.boundingBox(), await pick.locator('xpath=..').boundingBox()];
      expect(button!.height).toBeGreaterThanOrEqual(48);
      // The whole row is the target: the button fills the list item except the 1 px divider between rows.
      expect(button!.width).toBe(row!.width);
      expect(row!.height - button!.height).toBeLessThanOrEqual(1);
    }
    expect(await axe(page)).toEqual([]);
    for (const url of [page.url(), ...searches.map(s => s.url())]) expect(url).not.toMatch(/0102|Deepak|Nair|1979/);
    // Choosing is still one click.
    await picks.nth(0).click();
    await expect(picks.nth(0)).toHaveAttribute('aria-pressed', 'true');
  });
