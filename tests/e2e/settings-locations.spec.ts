import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';

// MDV-P1-070: the owner adds a location (second branch) with its tax number and registration certificate; the server refuses a wrong
// country, a bad tax number and a bad file under the field. CI has no API, so the endpoints are mocked in the shapes the server sends.
const owner = {
  user: { id: 'u-o', displayName: 'K. Lakshmi (synthetic)' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Adyar', role: 'PracticeAdmin' }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', country: 'IN', role: 'PracticeAdmin', flags: [] as string[], capabilities: { StaffRolesIntegrations: 'Full' } },
};
const certificate = { name: 'registration.pdf', mimeType: 'application/pdf', buffer: Buffer.from('%PDF-1.4 synthetic') };

async function open(page: Page, refusal?: { status: number; errors: Record<string, string[]> }, locale = 'en') {
  const locations = [{ id: 'f-1', name: 'Adyar', address: null, timeZone: 'Asia/Kolkata' }];
  const posted: Record<string, unknown>[] = [];
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: owner }));
  await page.route('**/v1/notifications/inbox?*', r => r.fulfill({ json: { unread: 0, items: [] } }));
  await page.route('**/v1/schedule/templates*', r => r.fulfill({ json: [] }));
  await page.route('**/v1/settings/locations', r => {
    if (r.request().method() !== 'POST') return r.fulfill({ json: locations });
    posted.push(r.request().postDataJSON() as Record<string, unknown>);
    if (refusal) return r.fulfill({ status: refusal.status, json: { errors: refusal.errors } });
    const body = posted.at(-1) as { name: string };
    locations.push({ id: 'f-2', name: body.name, address: null, timeZone: 'Asia/Kolkata' });
    return r.fulfill({ status: 201, json: locations.at(-1) });
  });
  await page.goto('/settings?tab=locations');
  return posted;
}

test('the owner adds a second branch with tax number and certificate, and it appears in the list', async ({ page }) => {
  const posted = await open(page);
  await page.getByLabel('Location name').fill('Velachery (synthetic)');
  await page.getByLabel('Tax number of this location').fill('27AAAAA0000A1Z5');
  await page.getByLabel('Registration certificate').setInputFiles(certificate);
  await page.getByRole('button', { name: 'Add location' }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Location added: Velachery (synthetic).' })).toBeVisible();
  await expect(page.getByRole('listitem').filter({ hasText: 'Velachery (synthetic)' }).filter({ has: page.getByRole('button', { name: 'Edit Velachery (synthetic)' }) })).toBeVisible();
  expect(posted[0]).toMatchObject({ name: 'Velachery (synthetic)', countryCode: 'IN', taxNumber: '27AAAAA0000A1Z5', certificateName: 'registration.pdf' });
  expect(typeof posted[0]?.certificate).toBe('string');
});

test('a missing certificate and a server refusal are announced under the field or beside the button', async ({ page }) => {
  await open(page, { status: 400, errors: { 'taxNumber': ['format'] } });
  await page.getByLabel('Location name').fill('Velachery (synthetic)');
  await page.getByLabel('Tax number of this location').fill('bad');
  await page.getByRole('button', { name: 'Add location' }).click();
  // MDV-P2-058: each refusal puts focus on the error summary, which names the field; the error is also under the field.
  const summary = page.getByRole('group', { name: 'There is a problem' });
  await expect(summary).toBeFocused();
  await expect(summary.getByRole('link')).toHaveText(['Registration certificate: Choose the registration certificate.']);
  await expect(page.locator('#location-certificate-error')).toHaveText('Choose the registration certificate.');
  await page.getByLabel('Registration certificate').setInputFiles(certificate);
  await page.getByRole('button', { name: 'Add location' }).click();
  await expect(summary).toBeFocused();
  await expect(page.locator('#location-tax-error')).toContainText('not a valid GSTIN or TRN');
});

test('a location in another country is refused with a plain message', async ({ page }) => {
  await open(page, { status: 400, errors: { countryCode: ['mismatch'] } });
  await page.getByLabel('Location name').fill('Dubai (synthetic)');
  await page.getByLabel('Tax number of this location').fill('27AAAAA0000A1Z5');
  await page.getByLabel('Registration certificate').setInputFiles(certificate);
  await page.getByRole('button', { name: 'Add location' }).click();
  await expect(page.getByRole('alert').filter({ hasText: 'same country as your organisation' })).toBeVisible();
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
for (const locale of ['en', 'ar'])
  test(`axe finds no violations on the locations tab (${locale})`, async ({ page }) => {
    await open(page, undefined, locale);
    await expect(page.locator('#location-certificate')).toBeVisible();
    if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    await page.addScriptTag({ path: axePath });
    const violations = await page.evaluate(async () =>
      (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations.map(v => `${v.id} (${v.nodes.length})`));
    expect(violations).toEqual([]);
  });
