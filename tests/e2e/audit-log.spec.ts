import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';

// MDV-P1-035. CI has no API: the access-log endpoints are mocked with the synthetic cast (docs/00-overview/personas.md).
// The server rules (403 and Denied audit for other roles, patient filter, paging, watermarked audited export) are tested on SQL Server in AccessLogTests.cs.
const facility = '11111111-1111-4111-8111-111111111111';
const admin = {
  user: { id: 'u-lakshmi', displayName: 'K. Lakshmi (synthetic)' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: facility, facilityName: 'Adyar', role: 'PracticeAdmin' }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: facility, role: 'PracticeAdmin', flags: [], capabilities: { StaffRolesIntegrations: 'Config', AuditLog: 'Read' } },
};
const row = (over: object) => ({
  id: 'e', occurredAt: '2026-11-04T05:30:00Z', actorUserId: 'u-rao', actorName: 'Dr. Arjun Rao (synthetic)', role: 'SoloDoctor', location: 'Velachery', action: 'patient.read', resourceType: 'patient',
  patientId: 'p-1', patientMrn: '000001', patientName: null, purpose: 'treatment', outcome: 'Allowed', detail: null, ...over,
});
const logPage = (items: object[], next: string | null = null) => ({ items, next, actions: ['patient.read', 'patient.update', 'patient.chart.allergy-flag', 'rx.check', 'something.new'] });
const staff = [{ membershipId: 'a', userId: 'u-rao', name: 'Dr. Arjun Rao (synthetic)', email: null, role: 'SoloDoctor', customRoleId: null, customRoleName: null, facilityId: facility, facilityName: 'Adyar', status: 'Active', mfa: 'enrolled', invitationId: null, invitationState: null, invitationExpiresAt: null }];

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
async function axe(page: Page) {
  await page.addScriptTag({ path: axePath });
  return page.evaluate(async () =>
    (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
      .map(v => `${v.id} (${v.nodes.length})`));
}

async function setUp(page: Page, locale = 'en') {
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: admin }));
  await page.route('**/v1/staff', r => r.fulfill({ json: staff }));
}

test('the access log shows who did what, writes the outcome out, pages, and exports with the audited POST', async ({ page }) => {
  await setUp(page);
  const urls: string[] = [];
  await page.route('**/v1/audit/access-log**', r => {
    const url = r.request().url();
    if (r.request().method() === 'POST') return r.fulfill({ body: 'x', contentType: 'text/csv' });
    urls.push(new URL(url).pathname + new URL(url).search);
    return r.fulfill({ json: url.includes('after=c1')
      ? logPage([row({ id: 'e3', action: 'patient.update' })])
      : logPage([row({ id: 'e1' }), row({ id: 'e2', actorName: 'S. Thomas RN (synthetic)', role: 'Nurse', outcome: 'Denied', action: 'patient.read' }),
        row({ id: 'e4', action: 'audit.export', role: null, detail: 'patient=p-1' })], 'c1') });
  });
  const exported = page.waitForRequest(r => r.url().endsWith('/v1/audit/access-log/export') && r.method() === 'POST');
  await page.goto('/settings?tab=audit');
  await expect(page.getByRole('heading', { name: 'Practice access log' })).toBeVisible();
  const denied = page.getByRole('row', { name: /S\. Thomas/ });
  await expect(denied).toContainText('Denied'); // written out, not colour alone
  await expect(denied).toContainText('Velachery'); // the location where the record was read (MDV-P1-072)
  await expect(denied).toContainText('MRN 000001');
  const legacy = page.getByRole('row', { name: /Access log exported/ }); // MDV-P1-052: the action in words, not its code
  await expect(legacy).toContainText('Not recorded'); // a row from before roles were recorded never shows the current role
  await expect(legacy).toContainText('patient=p-1');
  await expect(page.getByRole('cell', { name: /Meera/ })).toHaveCount(0); // no name until a patient is chosen
  await expect(page.getByRole('cell', { name: /audit\.|patient\./ })).toHaveCount(0); // no raw action codes
  expect(await axe(page)).toEqual([]);

  await page.getByRole('button', { name: 'Load more' }).click();
  await expect(page.getByRole('row', { name: /Patient record updated/ })).toBeVisible();
  expect(urls.some(u => u.includes('after=c1'))).toBe(true);

  await page.getByRole('button', { name: 'Export CSV' }).click();
  // MDV-P2-050 AC4: the export holds every row whatever the view.
  expect(JSON.parse((await exported).postData() ?? '{}')).toMatchObject({ patientId: null, action: null, patientRecords: false });
});

// MDV-P2-050: the log opens on charts opened or changed (routine list reads left out on the server); All activity shows the rest. Every
// option and row is a plain name: no dotted code, not even for a code the screen does not know yet.
for (const locale of ['en', 'ar'])
  test(`the access log opens on charts opened or changed, names every action, and widens to all activity (${locale})`, async ({ page }) => {
    await setUp(page, locale);
    const urls: string[] = [];
    await page.route('**/v1/audit/access-log**', r => { urls.push(new URL(r.request().url()).search); return r.fulfill({ json: logPage([row({}), row({ id: 'e9', action: 'something.new' })]) }); });
    await page.goto('/settings?tab=audit');
    await expect.poll(() => urls.at(-1)).toContain('chart=true');
    await expect(page.getByRole('cell', { name: /[a-z]+\.[a-z]/ })).toHaveCount(0);
    expect(await axe(page)).toEqual([]);
    await page.getByRole('button', { name: locale === 'en' ? 'Filters' : 'عوامل التصفية' }).click(); // folded by default (MDV-P2-054)
    await page.getByRole('combobox', { name: locale === 'en' ? 'Action' : 'الإجراء', exact: true }).click();
    await expect(page.getByRole('option', { name: /[a-z_]+\.[a-z_-]+/ })).toHaveCount(0); // the filter's options too
    if (locale === 'ar') return;
    await expect(page.getByRole('option', { name: 'Opened or changed a chart' })).toBeVisible();
    await expect(page.getByRole('option', { name: 'Allergy flag shown in a list' })).toBeVisible();
    await page.getByRole('option', { name: 'All activity' }).click();
    await expect.poll(() => urls.at(-1)).not.toContain('chart=true');
    await expect(page.getByRole('row', { name: /Other recorded activity/ })).toBeVisible();
  });

test('choosing a patient asks the per-patient route', async ({ page }) => {
  await setUp(page);
  await page.route('**/v1/patients/search', r => r.fulfill({ json: { matchedOn: 'Name', page: 1, pageSize: 20, total: 1, items: [{ id: 'p-1', initials: 'MI', fullName: 'Meera Iyer (synthetic)', ageYears: 54, ageEstimated: false, sexAtBirth: 'Female', mrnTail: '0001', mobileTail: '0101', status: 'Complete' }] } }));
  const paths: string[] = [];
  await page.route('**/v1/audit/access-log**', r => {
    paths.push(new URL(r.request().url()).pathname);
    return r.fulfill({ json: logPage([row({ patientName: 'Meera Iyer (synthetic)' })]) });
  });
  await page.goto('/settings?tab=audit');
  await page.getByRole('button', { name: 'Filters' }).click();
  await page.getByPlaceholder('Name, MRN, phone or ID').fill('Meera');
  await page.getByRole('button', { name: 'Search', exact: true }).click();
  await page.getByRole('button', { name: /Meera Iyer/ }).click();
  await expect(page.getByText('Showing events on the record of Meera Iyer (synthetic)')).toBeVisible();
  expect(paths.at(-1)).toBe('/v1/audit/access-log/patients/p-1');
  await page.getByRole('button', { name: 'Show all patients' }).click();
  await expect.poll(() => paths.at(-1)).toBe('/v1/audit/access-log');
});

test('a role without the audit log sees the refusal, and Arabic lays the table out right to left', async ({ page }) => {
  await setUp(page, 'ar');
  await page.route('**/v1/audit/access-log**', r => r.fulfill({ json: logPage([row({})]) }));
  await page.goto('/settings?tab=audit');
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
  await expect(page.getByRole('row', { name: /مسموح/ })).toBeVisible();
  expect(await axe(page)).toEqual([]);

  const other = await page.context().newPage();
  await other.addInitScript(() => localStorage.setItem('medvya.locale', 'en'));
  await other.route('**/v1/me/context', r => r.fulfill({ json: admin }));
  await other.route('**/v1/staff', r => r.fulfill({ json: staff }));
  await other.route('**/v1/audit/access-log**', r => r.fulfill({ status: 403, json: {} }));
  await other.goto('/settings?tab=audit');
  await expect(other.getByRole('alert')).toContainText('Your role does not include the audit log');
});

// Found on Azure dev: the date followed the browser's zone and the time the clinic's, so an event after 18:30 UTC showed the right time on the day before.
test.describe('in a browser on another zone', () => {
  test.use({ timezoneId: 'UTC' });
  test('a row shows the day and the time in the clinic\'s zone', async ({ page }) => {
    await page.addInitScript(() => localStorage.setItem('medvya.locale', 'en'));
    await page.route('**/v1/me/context', r => r.fulfill({ json: { ...admin, active: { ...admin.active, timeZone: 'Asia/Kolkata' } } }));
    await page.route('**/v1/staff', r => r.fulfill({ json: staff }));
    await page.route('**/v1/audit/access-log**', r => r.fulfill({ json: logPage([row({ occurredAt: '2026-11-10T19:06:00Z' })]) }));
    await page.goto('/settings?tab=audit');
    await expect(page.getByRole('region', { name: 'Access log' }).locator('time')).toHaveText('11 Nov 2026 00:36'); // 00:36 on 11 Nov in Chennai
  });
});

// MDV-P2-054: a compact table (36 px rows with a mouse, 48 px on a coarse pointer), sorted by time or staff from the keyboard with the order
// announced, paged over 50 rows, stacked with labelled fields under 768 px. Dr. Arjun Rao is the cast name (docs/00-overview/personas.md).
const many = (n: number) => Array.from({ length: n }, (_, i) =>
  row({ id: `m${i}`, actorName: 'Dr. Arjun Rao', occurredAt: new Date(Date.UTC(2026, 10, 4, 10) - i * 60_000).toISOString() }));
const table = (page: Page, locale = 'en') => page.getByRole('region', { name: locale === 'en' ? 'Access log' : 'سجل الوصول', exact: true });

test('at 1440 x 900 with a mouse the table starts in the first 300 px and shows at least 15 rows of 36 px', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await setUp(page);
  await page.route('**/v1/audit/access-log**', r => r.fulfill({ json: logPage(many(50), 'c1') }));
  await page.goto('/settings?tab=audit');
  await expect(page.getByText('Showing: Opened or changed a chart · Everyone · ')).toBeVisible(); // the folded filters, in words
  const box = (await table(page).boundingBox())!;
  expect(box.y).toBeLessThanOrEqual(300);
  const rows = table(page).getByRole('row');
  expect(Math.round((await rows.nth(1).boundingBox())!.height)).toBe(36);
  let visible = 0;
  for (const r of (await rows.all()).slice(1)) if (((await r.boundingBox())?.y ?? 900) + 36 <= 900) visible++;
  expect(visible).toBeGreaterThanOrEqual(15);
  expect(await table(page).evaluate(e => e.scrollWidth - e.clientWidth)).toBe(0);
  expect(await axe(page)).toEqual([]);
});

for (const locale of ['en', 'ar'])
  test(`Load more opens the page it loaded, the log is paged by 50, and a keyboard sort is announced (${locale})`, async ({ page }) => {
    await setUp(page, locale);
    const all = many(60); // the server sends 50 at a time
    await page.route('**/v1/audit/access-log**', r => r.fulfill({ json: r.request().url().includes('after=c1') ? logPage(all.slice(50)) : logPage(all.slice(0, 50), 'c1') }));
    await page.goto('/settings?tab=audit');
    const t = table(page, locale);
    await expect(t.getByRole('rowheader')).toHaveCount(50);
    await page.getByRole('button', { name: locale === 'en' ? 'Load more' : 'عرض المزيد' }).click();
    await expect(t.getByRole('rowheader')).toHaveCount(10); // page 2 of 2: the ten it loaded
    await expect(page.getByRole('button', { name: locale === 'en' ? 'Load more' : 'عرض المزيد' })).toHaveCount(0); // nothing older
    const pager = page.getByRole('navigation', { name: locale === 'en' ? 'Pages' : 'الصفحات' });
    await pager.getByRole('button').first().click();
    await expect(t.getByRole('rowheader')).toHaveCount(50);
    await pager.getByRole('button').last().click();
    const time = t.getByRole('columnheader').first();
    await time.getByRole('button').press('Enter');
    await expect(time).toHaveAttribute('aria-sort', 'ascending');
    if (locale === 'en') await expect(t.getByRole('status')).toHaveText('Sorted by Time, ascending');
    else await expect(t.getByRole('status')).not.toBeEmpty();
    await expect(t.getByRole('rowheader')).toHaveCount(50); // a sort goes back to page 1
    await expect(t.getByRole('rowheader').first().locator('time')).toHaveAttribute('datetime', all.at(-1)!.occurredAt); // oldest loaded first
    expect(await axe(page)).toEqual([]);
  });

test.describe('on a coarse pointer', () => {
  test.use({ hasTouch: true, isMobile: true, viewport: { width: 1024, height: 768 } });
  test('the compact audit table ignores its density: rows are 48 px', async ({ page }) => {
    await setUp(page);
    await page.route('**/v1/audit/access-log**', r => r.fulfill({ json: logPage(many(3)) }));
    await page.goto('/settings?tab=audit');
    expect(await page.evaluate(() => matchMedia('(any-pointer: coarse)').matches)).toBe(true);
    const r = table(page).getByRole('row').nth(1);
    await expect(r).toBeVisible();
    expect(Math.round((await r.boundingBox())!.height)).toBeGreaterThanOrEqual(48);
  });
});

test.describe('at 375 px', () => {
  test.use({ viewport: { width: 375, height: 812 } });
  for (const locale of ['en', 'ar'])
    test(`audit rows are stacked with the time first and every field labelled, and nothing scrolls sideways (${locale})`, async ({ page }) => {
      await setUp(page, locale);
      await page.route('**/v1/audit/access-log**', r => r.fulfill({ json: logPage([row({}), row({ id: 'e2', outcome: 'Denied' })]) }));
      await page.goto('/settings?tab=audit');
      const t = table(page, locale);
      await expect(t.getByRole('rowheader').first()).toBeInViewport();
      const cells = t.getByRole('row').nth(1).getByRole('cell');
      await expect(cells.first().getByText(locale === 'en' ? 'Staff' : 'الموظف', { exact: true })).toBeVisible(); // the column's name above its value
      await expect(cells.last().getByText(locale === 'en' ? 'Outcome' : 'النتيجة', { exact: true })).toBeVisible();
      expect(await t.evaluate(e => e.scrollWidth - e.clientWidth)).toBeLessThanOrEqual(0);
      expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(0);
      expect(await axe(page)).toEqual([]);
    });
});
