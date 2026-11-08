import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';

// MDV-P2-003 (D-02): the ⌘K overlay searches from any screen, shows initials, age and the MRN tail only, opens a chart with the keyboard,
// and offers "Request access" for a patient outside the care relationship. CI has no API: the session and the lookup are mocked in the shapes
// the server sends; the payload shape, the care-relationship verdicts, the audit row and the 300 ms budget are covered by
// GlobalSearchEndpointTests and PatientSearchPerformanceTests on PostgreSQL.
const doctor = {
  user: { id: 'u-rao', displayName: 'Dr. Arjun Rao (synthetic)' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role: 'SoloDoctor' }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role: 'SoloDoctor', flags: [] as string[], country: 'IN',
    capabilities: { RegisterBookPatients: 'Full', CheckInQueue: 'Full', Patient360Chart: 'Full' } },
};
const row = (id: string, initials: string, access: string) => ({ id, initials, ageYears: 54, ageEstimated: false, mrnTail: '0001', access });

async function open(page: Page, { locale = 'en', items = [row('p-1', 'MI', 'Open'), row('p-2', 'LP', 'CanRequest')] } = {}) {
  const calls = { lookups: [] as { url: string; body: unknown }[], requests: [] as unknown[] };
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: doctor }));
  await page.route('**/v1/front-desk/board?*', r => r.fulfill({ json: { facilityId: 'f-1', timeZone: 'Asia/Kolkata', date: '2026-10-19', now: '2026-10-19T04:30:00Z', cards: [] } }));
  await page.route('**/v1/patients/lookup', r => {
    calls.lookups.push({ url: r.request().url(), body: r.request().postDataJSON() });
    return r.fulfill({ json: { items, total: items.length } });
  });
  await page.route('**/v1/patients/p-2/access-requests', r => { calls.requests.push(r.request().postDataJSON()); return r.fulfill({ json: { id: 'r-1', status: 'Pending' } }); });
  await page.route('**/v1/patients/p-1', r => r.fulfill({ status: 404, json: {} }));
  await page.goto('/front-desk');
  await expect(page.locator('[aria-keyshortcuts]')).toBeVisible(); // the shortcut is live once the shell has rendered
  return calls;
}

test('Ctrl+K opens the search from any screen; arrows, Enter and Esc work; results hold only initials, age and MRN tail', async ({ page }) => {
  const calls = await open(page);
  await page.keyboard.press('Control+k');
  const search = page.getByRole('dialog', { name: 'Search patients' });
  await expect(search).toBeVisible();
  const input = search.getByRole('combobox', { name: 'Name, MRN or phone' });
  await expect(input).toBeFocused();
  await input.fill('me');
  await expect(search.getByRole('status').first()).toHaveText('Patients found: 2');
  const rows = search.getByRole('option');
  await expect(rows).toHaveCount(2);
  await expect(rows.first()).toContainText('MI');
  await expect(rows.first()).toContainText('54 y');
  await expect(rows.first()).toContainText('MRN ··0001');
  expect(await search.innerText()).not.toMatch(/Meera|Iyer|\+91/);
  await input.press('ArrowDown');
  await expect(rows.first()).toHaveAttribute('aria-selected', 'true');
  await input.press('ArrowDown');
  await expect(rows.nth(1)).toHaveAttribute('aria-selected', 'true');
  await input.press('ArrowUp');
  // The search text goes in the POST body, never in the address.
  expect(calls.lookups).toEqual([{ url: expect.not.stringContaining('me'), body: { query: 'me' } }]);
  expect(calls.lookups[0]!.url).not.toContain('?');
  await input.press('Enter');
  await expect(page).toHaveURL(/\/patients\/directory\/p-1$/);
  await expect(search).toHaveCount(0);

  await page.keyboard.press('Control+k');
  await expect(page.getByRole('dialog', { name: 'Search patients' })).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(page.getByRole('dialog')).toHaveCount(0);
});

test('a patient outside the care relationship shows Request access, not the chart', async ({ page }) => {
  const calls = await open(page);
  await page.getByRole('button', { name: 'Search patients' }).click();
  const search = page.getByRole('dialog', { name: 'Search patients' });
  await search.getByRole('combobox').fill('la');
  await search.getByRole('option').nth(1).click();
  await expect(search.getByRole('button', { name: 'Open chart' })).toHaveCount(0);
  const ask = search.getByRole('alert').filter({ hasText: 'Ask an owner for access' });
  await ask.getByRole('combobox', { name: 'Why you need access' }).click();
  await page.getByRole('option', { name: 'Covering for a colleague' }).click();
  await ask.getByRole('button', { name: 'Request access' }).click();
  await expect(search.getByText('Access requested. An owner of the clinic will decide.')).toBeVisible();
  await expect(search.getByRole('option').nth(1)).toContainText('Access requested');
  expect(calls.requests).toEqual([{ reason: 'CoveringColleague' }]);
});

test('an Open row also offers Start visit and Book, and no match offers registering', async ({ page }) => {
  await open(page);
  await page.getByRole('button', { name: 'Search patients' }).click();
  const search = page.getByRole('dialog', { name: 'Search patients' });
  await search.getByRole('combobox').fill('me');
  await search.getByRole('option').first().click();
  await expect(search.getByRole('link', { name: 'Start visit' })).toHaveAttribute('href', '/front-desk/walk-in?patient=p-1');
  await expect(search.getByRole('link', { name: 'Book' })).toHaveAttribute('href', '/schedule');
});

test('no match says so and offers to register a patient', async ({ page }) => {
  await open(page, { items: [] });
  await page.keyboard.press('Control+k');
  await page.getByRole('combobox', { name: 'Name, MRN or phone' }).fill('zz');
  await expect(page.getByRole('dialog').getByText('No patients match.')).toBeVisible();
  await expect(page.getByRole('dialog').getByRole('link', { name: 'Register a new patient' })).toBeVisible();
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
for (const locale of ['en', 'ar'])
  test(`axe finds no violations in the open search (${locale}); 44 px targets`, async ({ page }) => {
    await open(page, { locale });
    await page.keyboard.press('Control+k');
    const search = page.getByRole('dialog');
    await search.getByRole('combobox').fill('me');
    await expect(search.getByRole('option')).toHaveCount(2);
    await search.getByRole('option').first().click();
    if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    for (const b of await search.getByRole('button').all()) expect((await b.boundingBox())!.height).toBeGreaterThanOrEqual(44);
    await search.getByRole('option').nth(1).click();
    await page.addScriptTag({ path: axePath });
    const violations = await page.evaluate(async () =>
      (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
        .map(v => `${v.id} (${v.nodes.length})`));
    expect(violations).toEqual([]);
  });
