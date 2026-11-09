import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';

// MDV-P2-014 (F-13): the practice searches the country list, sees each class and what it prints, adds its own medicine and builds an
// order set. CI has no API, so the endpoints are mocked in the shapes the server sends (FormularyItemView, OrderSetView).
type Country = 'IN' | 'AE';
const owner = (country: Country) => ({
  user: { id: 'u-o', displayName: 'K. Lakshmi (synthetic)' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role: 'PracticeAdmin' }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role: 'PracticeAdmin', country, flags: [] as string[], capabilities: { StaffRolesIntegrations: 'Config' } },
});
const item = (generic: string, strength: string, form: string, regClass: string, printRequirements: string[], source = 'Starter') =>
  ({ id: `${generic}-${strength}`, generic, strength, form, brands: [] as string[], nationalCode: null, regClass, printRequirements, atc: null, defaultSig: null, source, active: true });
const starter = {
  IN: [item('Metformin', '500 mg', 'Tablet', 'ScheduleH', ['warning_schedule_h']), item('Tramadol', '50 mg', 'Capsule', 'ScheduleH1', ['warning_schedule_h1', 'pharmacist_keeps_copy']), item('Paracetamol', '500 mg', 'Tablet', 'None', [])],
  AE: [item('Tramadol', '50 mg', 'Capsule', 'Controlled', ['warning_controlled', 'pharmacist_keeps_copy']), item('Paracetamol', '500 mg', 'Tablet', 'None', [])],
};

async function open(page: Page, country: Country = 'IN', locale = 'en') {
  const added: Record<string, unknown>[] = [];
  const sets: { id: string; name: string; items: string[] }[] = [];
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: owner(country) }));
  await page.route('**/v1/notifications/inbox?*', r => r.fulfill({ json: { unread: 0, items: [] } }));
  await page.route('**/v1/settings/formulary?*', r => {
    const q = (new URL(r.request().url()).searchParams.get('q') ?? '').toLowerCase();
    const mine = added.map(a => item(String(a.generic), String(a.strength), String(a.form), String(a.regClass), [], 'ClinicAdded'));
    return r.fulfill({ json: [...starter[country], ...mine].filter(i => i.generic.toLowerCase().includes(q)) });
  });
  await page.route('**/v1/settings/formulary', r => {
    const body = r.request().postDataJSON() as Record<string, unknown>;
    if (body.generic === 'Metformin') return r.fulfill({ status: 409, json: { code: 'already_listed' } });
    if (country === 'AE' && body.regClass === 'ScheduleH') return r.fulfill({ status: 400, json: { errors: { regClass: ['not_in_country'] } } });
    added.push(body);
    return r.fulfill({ status: 201, json: item(String(body.generic), String(body.strength), String(body.form), String(body.regClass), [], 'ClinicAdded') });
  });
  await page.route('**/v1/settings/order-sets', r => {
    if (r.request().method() === 'POST') {
      const body = r.request().postDataJSON() as { name: string; items: string[] };
      const set = { id: `set-${sets.length}`, ...body };
      sets.push(set);
      return r.fulfill({ status: 201, json: set });
    }
    return r.fulfill({ json: sets });
  });
  await page.goto('/settings?tab=medicines');
  return { added, sets };
}

test('the list labels each class and what it prints, and a search narrows it', async ({ page }) => {
  await open(page);
  const table = page.getByRole('region', { name: 'Medicine list' });
  await expect(table.getByRole('row')).toHaveCount(4); // header and three medicines
  await expect(table.getByRole('row', { name: /Tramadol/ })).toContainText('Schedule H1');
  await expect(table.getByRole('row', { name: /Tramadol/ })).toContainText('Prints the Schedule H1 warning');
  await expect(table.getByRole('row', { name: /Metformin/ })).toContainText('Prints the Schedule H warning');
  await page.getByLabel('Search by generic or brand name').fill('para');
  await expect(table.getByRole('row')).toHaveCount(2);
  await page.getByLabel('Search by generic or brand name').fill('zzz');
  await expect(page.getByText('No medicine matches')).toBeVisible();
});

test('the UAE clinic sees the controlled class and is offered no Indian schedule', async ({ page }) => {
  await open(page, 'AE');
  await expect(page.getByRole('row', { name: /Tramadol/ })).toContainText('Controlled medicine');
  await page.getByLabel('Class').click();
  await expect(page.getByRole('option')).toHaveText(['Prescription medicine', 'Controlled medicine']);
});

test('an admin adds a medicine, blanks stay blank, and a duplicate is refused in words', async ({ page }) => {
  const { added } = await open(page);
  await page.getByRole('button', { name: 'Add medicine' }).click();
  await expect(page.getByText('This is required.').first()).toBeVisible();
  await page.getByLabel('Generic name', { exact: true }).last().fill('Glimepiride');
  await page.getByLabel('Strength, for example 500 mg').fill('2 mg');
  await page.getByLabel('Form', { exact: true }).click();
  await page.getByRole('option', { name: 'Tablet' }).click();
  await page.getByLabel('Class', { exact: true }).click();
  await page.getByRole('option', { name: 'Schedule H', exact: true }).click();
  await page.getByLabel('Brand names').fill('Amaryl, Glimy');
  await page.getByRole('button', { name: 'Add medicine' }).click();
  await expect(page.getByText('Medicine added to your clinic list.')).toBeVisible();
  expect(added[0]).toMatchObject({ generic: 'Glimepiride', brands: ['Amaryl', 'Glimy'], regClass: 'ScheduleH', nationalCode: null, atc: null, defaultSig: null });
  await expect(page.getByRole('row', { name: /Glimepiride/ })).toContainText('Clinic-added');

  await page.getByLabel('Generic name', { exact: true }).last().fill('Metformin');
  await page.getByLabel('Strength, for example 500 mg').fill('500 mg');
  await page.getByLabel('Form', { exact: true }).click();
  await page.getByRole('option', { name: 'Tablet' }).click();
  await page.getByLabel('Class', { exact: true }).click();
  await page.getByRole('option', { name: 'Schedule H', exact: true }).click();
  await page.getByRole('button', { name: 'Add medicine' }).click();
  await expect(page.getByRole('alert').filter({ hasText: 'This is already in your list.' })).toBeVisible();
});

test('an admin builds an order set for the order composer', async ({ page }) => {
  const { sets } = await open(page);
  await page.getByLabel('Name', { exact: true }).fill('Diabetes review labs');
  await page.getByLabel('Tests, one per line').fill('HbA1c\nFasting glucose\n\nSerum creatinine');
  await page.getByRole('button', { name: 'Add order set' }).click();
  await expect(page.getByText('Order set added.')).toBeVisible();
  expect(sets[0]).toMatchObject({ name: 'Diabetes review labs', items: ['HbA1c', 'Fasting glucose', 'Serum creatinine'] });
  await expect(page.getByRole('row', { name: /Diabetes review labs/ })).toContainText('HbA1c, Fasting glucose, Serum creatinine');
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
for (const locale of ['en', 'ar'])
  test(`axe finds no violations on the medicines tab (${locale})`, async ({ page }) => {
    await open(page, locale === 'ar' ? 'AE' : 'IN', locale);
    await expect(page.getByRole('row', { name: /Tramadol/ })).toBeVisible();
    if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    await page.addScriptTag({ path: axePath });
    const violations = await page.evaluate(async () =>
      (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations.map(v => `${v.id} (${v.nodes.length})`));
    expect(violations).toEqual([]);
  });
