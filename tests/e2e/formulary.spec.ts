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
  const brands: { brand: string; generic: string }[] = [];
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: owner(country) }));
  await page.route('**/v1/notifications/inbox?*', r => r.fulfill({ json: { unread: 0, items: [] } }));
  await page.route('**/v1/settings/formulary?*', r => {
    const q = (new URL(r.request().url()).searchParams.get('q') ?? '').toLowerCase();
    const mine = added.map(a => item(String(a.generic), String(a.strength), String(a.form), String(a.regClass), [], 'ClinicAdded'));
    const listed = [...starter[country], ...mine].map(i => ({ ...i, brands: brands.filter(b => b.generic === i.generic).map(b => b.brand) }));
    return r.fulfill({ json: listed.filter(i => i.generic.toLowerCase().includes(q)) });
  });
  // MDV-P1-088: all rows or none; a row the server cannot resolve is named by its place (BrandsAdded, or problem details with a code a row).
  await page.route('**/v1/settings/formulary/brands', r => {
    const { rows } = r.request().postDataJSON() as { rows: { brand: string; generic: string }[] };
    const errors = Object.fromEntries(rows.flatMap((row, i) => (starter[country].some(s => s.generic === row.generic) ? [] : [[`rows[${i}].generic`, ['not_listed']]])));
    if (Object.keys(errors).length > 0) return r.fulfill({ status: 400, json: { errors } });
    brands.push(...rows);
    return r.fulfill({ json: { added: rows.length } });
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
  return { added, sets, brands };
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
  await page.getByRole('textbox', { name: 'Brand names' }).fill('Amaryl, Glimy');
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

test('an admin pastes brand rows: every row is added or none, and a refused row is named', async ({ page }) => {
  const { brands } = await open(page);
  const rows = page.getByLabel('Brand rows');
  const add = page.getByRole('button', { name: 'Add brands' });
  await add.click();
  await expect(page.getByRole('alert').filter({ hasText: 'Paste at least one row.' })).toBeVisible();
  await rows.fill('Synthetic Para, Paracetamol, 500 mg, Tablet');
  await add.click();
  await expect(page.getByRole('alert').filter({ hasText: 'Row 1 needs five values: brand, generic name, strength, form and allergy class.' })).toBeVisible();
  expect(brands).toEqual([]); // not sent

  await rows.fill('Synthetic Para, Paracetamol, 500 mg, Tablet, none\nSynthetic X, Not in the list, 1 mg, Tablet, none');
  await add.click();
  const refusal = page.getByRole('alert').filter({ hasText: 'Nothing was added. Correct these rows and try again.' });
  await expect(refusal).toContainText('Row 2: no medicine in your list has this generic name, strength and form. Add the medicine first.');
  await expect(rows).toHaveAttribute('aria-invalid', 'true');
  await expect(page.getByRole('row', { name: /Paracetamol/ })).not.toContainText('Synthetic Para'); // the good row was not added either

  await rows.fill('Synthetic Para, Paracetamol, 500 mg, Tablet, none\n\nSynthetic Met\tMetformin\t500 mg\tTablet\tnone'); // commas, or tabs from a spreadsheet
  await add.click();
  await expect(page.getByRole('status').filter({ hasText: 'Brand names added: 2' })).toBeVisible();
  await expect(refusal).toBeHidden();
  expect(brands).toEqual([
    { brand: 'Synthetic Para', generic: 'Paracetamol', strength: '500 mg', form: 'Tablet', allergyClass: 'none' },
    { brand: 'Synthetic Met', generic: 'Metformin', strength: '500 mg', form: 'Tablet', allergyClass: 'none' },
  ]);
  await expect(page.getByRole('row', { name: /Paracetamol/ })).toContainText('Synthetic Para'); // the brand shows on the generic's row
  await expect(rows).toHaveValue('');
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
for (const locale of ['en', 'ar'])
  test(`axe finds no violations with brand rows refused (${locale})`, async ({ page }) => {
    await open(page, locale === 'ar' ? 'AE' : 'IN', locale);
    await page.getByLabel(locale === 'ar' ? 'صفوف الأسماء التجارية' : 'Brand rows').fill('Synthetic X, Not in the list, 1 mg, Tablet, none');
    await page.getByRole('button', { name: locale === 'ar' ? 'إضافة الأسماء التجارية' : 'Add brands' }).click();
    await expect(page.getByRole('alert').filter({ hasText: locale === 'ar' ? 'لم يُضف شيء.' : 'Nothing was added.' })).toBeVisible();
    await page.addScriptTag({ path: axePath });
    const violations = await page.evaluate(async () =>
      (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations.map(v => `${v.id} (${v.nodes.length})`));
    expect(violations).toEqual([]);
  });
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

// MDV-P2-054: the medicine list is a compact table (36 px rows with a mouse, 48 px on a coarse pointer), sorted from the keyboard
// with the order announced, and stacked rows on a phone: no sideways scroll, the strength (the dose) always in view.
const rowHeights = (page: Page) =>
  page.getByRole('region', { name: 'Medicine list' }).locator('tbody tr').evaluateAll(rows => rows.map(r => Math.round(r.getBoundingClientRect().height)));

test('a header sorts by keyboard, the order changes and the sort is announced', async ({ page }) => {
  await open(page);
  const table = page.getByRole('region', { name: 'Medicine list' });
  const names = () => table.getByRole('rowheader').allTextContents();
  await expect(table.getByRole('rowheader')).toHaveText(['Metformin', 'Tramadol', 'Paracetamol']); // the server's order
  const generic = table.getByRole('button', { name: 'Generic name' });
  await generic.focus();
  await page.keyboard.press('Enter');
  await expect(table.getByRole('columnheader', { name: 'Generic name' })).toHaveAttribute('aria-sort', 'ascending');
  expect(await names()).toEqual(['Metformin', 'Paracetamol', 'Tramadol']);
  await expect(table.getByRole('status')).toHaveText('Sorted by Generic name, ascending');
  await page.keyboard.press('Enter');
  expect(await names()).toEqual(['Tramadol', 'Paracetamol', 'Metformin']);
  await expect(table.getByRole('status')).toHaveText('Sorted by Generic name, descending');
  await table.getByRole('button', { name: 'Class' }).press('Enter'); // ordinary first, then the schedules from H to H1
  expect(await names()).toEqual(['Paracetamol', 'Metformin', 'Tramadol']);
  await expect(table.getByRole('columnheader', { name: 'Generic name' })).toHaveAttribute('aria-sort', 'none');
});

test('a full answer of 50 says so and asks to narrow the search', async ({ page }) => {
  await open(page);
  await expect(page.getByText(/Showing the first/)).toBeHidden();
  await page.route('**/v1/settings/formulary?*', r =>
    r.fulfill({ json: Array.from({ length: 50 }, (_, i) => item(`Synthetic medicine ${i}`, '1 mg', 'Tablet', 'None', [])) }));
  await page.getByLabel('Search by generic or brand name').fill('syn');
  await expect(page.getByText('Showing the first 50 medicines. Type more of the name to narrow the list.')).toBeVisible();
});

test('compact rows are 36 px with a mouse', async ({ page }) => {
  await open(page);
  const paracetamol = page.getByRole('row', { name: /Paracetamol/ }); // one line of text; a class with a print warning takes two
  await expect(paracetamol).toBeVisible();
  expect(Math.round((await paracetamol.boundingBox())!.height)).toBe(36);
});

test.describe('on a coarse pointer', () => {
  test.use({ hasTouch: true, isMobile: true, viewport: { width: 1024, height: 768 } });
  test('the compact table ignores its density: rows are 48 px', async ({ page }) => {
    await open(page);
    await expect(page.getByRole('row', { name: /Paracetamol/ })).toBeVisible();
    expect(await page.evaluate(() => matchMedia('(any-pointer: coarse)').matches)).toBe(true);
    for (const h of await rowHeights(page)) expect(h).toBeGreaterThanOrEqual(48);
  });
});

test.describe('at 375 px', () => {
  test.use({ viewport: { width: 375, height: 812 } });
  for (const locale of ['en', 'ar'])
    test(`rows are stacked: no sideways scroll, every field in view, axe clean (${locale})`, async ({ page }) => {
      await open(page, locale === 'ar' ? 'AE' : 'IN', locale);
      const region = page.getByRole('region', { name: locale === 'ar' ? 'قائمة الأدوية' : 'Medicine list' });
      const tramadol = region.getByRole('row', { name: /Tramadol/ });
      await expect(tramadol).toBeVisible();
      expect(await region.evaluate(e => e.scrollWidth - e.clientWidth)).toBeLessThanOrEqual(0);
      expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(0);
      // The identity (generic name) and the dose (strength) are inside the viewport, each strength labelled.
      for (const cell of [tramadol.getByRole('rowheader'), tramadol.getByRole('cell').filter({ hasText: '50 mg' })]) {
        await cell.scrollIntoViewIfNeeded();
        const box = (await cell.boundingBox())!;
        expect(box.x).toBeGreaterThanOrEqual(0);
        expect(box.x + box.width).toBeLessThanOrEqual(375);
      }
      await expect(tramadol.getByRole('cell').filter({ hasText: '50 mg' })).toContainText(locale === 'ar' ? 'التركيز' : 'Strength');
      await page.addScriptTag({ path: axePath });
      const violations = await page.evaluate(async () =>
        (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations.map(v => `${v.id} (${v.nodes.length})`));
      expect(violations).toEqual([]);
    });
});

// MDV-P2-054: order sets are a compact table too, sorted by name from the keyboard and paged over 50 sets.
test('order sets sort by name, and over 50 sets are paged', async ({ page }) => {
  const { sets } = await open(page);
  sets.push(...Array.from({ length: 55 }, (_, i) => ({ id: `s-${i}`, name: `Set ${String(i).padStart(2, '0')}`, items: ['HbA1c'] })));
  await page.reload();
  const table = page.getByRole('region', { name: 'Order set list' });
  await expect(table.getByRole('rowheader')).toHaveCount(50);
  const pager = page.getByRole('navigation', { name: 'Pages' });
  await expect(pager).toContainText('Page 1 of 2');
  await pager.getByRole('button', { name: 'Next page' }).click();
  await expect(table.getByRole('rowheader')).toHaveCount(5);
  await expect(table.getByRole('rowheader').first()).toHaveText('Set 50');
  await table.getByRole('button', { name: 'Name' }).press('Enter');
  await table.getByRole('button', { name: 'Name' }).press('Enter'); // descending, back on page 1
  await expect(table.getByRole('columnheader', { name: 'Name' })).toHaveAttribute('aria-sort', 'descending');
  await expect(table.getByRole('status')).toHaveText('Sorted by Name, descending');
  await expect(pager).toContainText('Page 1 of 2');
  await expect(table.getByRole('rowheader').first()).toHaveText('Set 54');
  const row = table.getByRole('row', { name: /Set 54/ });
  expect(Math.round((await row.boundingBox())!.height)).toBe(36);
});

test.describe('order sets at 375 px', () => {
  test.use({ viewport: { width: 375, height: 812 } });
  test('rows are stacked with the tests labelled, and nothing scrolls sideways', async ({ page }) => {
    const { sets } = await open(page);
    sets.push({ id: 's-1', name: 'Diabetes review labs', items: ['HbA1c', 'Fasting glucose', 'Serum creatinine'] });
    await page.reload();
    const table = page.getByRole('region', { name: 'Order set list' });
    await expect(table.getByRole('cell')).toContainText('Tests');
    expect(await table.evaluate(e => e.scrollWidth - e.clientWidth)).toBeLessThanOrEqual(0);
  });
});
